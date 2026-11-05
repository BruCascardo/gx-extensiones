using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GxKbRefs
{
    /// <summary>Un objeto de otra KB: su clave en esa base y lo que se muestra en el árbol.</summary>
    sealed class KbReference
    {
        public int TypeId;
        public int EntityId;
        public string TypeName;
        public string Name;
        public string QualifiedName;
        public bool Hard;
        public bool HasMoreRefs;
        public bool IsInterface;
        /// <summary>Valor de dominio por el que llama (Dominio.VALOR), si lo referencia por medio de un servicio.</summary>
        public string Via;

        public bool Exists => TypeId > 0;

        public bool SameEntity(KbReference other) => other != null && Exists && other.TypeId == TypeId && other.EntityId == EntityId;

        /// <summary>Un objeto que no está en la KB: solo se le pueden buscar llamadas por servicio, por su nombre.</summary>
        public static KbReference Missing(string qualifiedName) => new KbReference { QualifiedName = qualifiedName, Name = qualifiedName };
    }

    /// <summary>
    /// Otra KB, leída directo de su base (solo lectura). GeneXus guarda las referencias del modelo de diseño en
    /// ModelCrossReference, la misma tabla que usa la ventana References. Los objetos de módulos referenciados son
    /// copias de interfaz (propiedad ObjectIsInterface): se descartan, sus llamadas se ven en la KB dueña del módulo.
    /// <para>
    /// Además de las referencias directas busca las llamadas por servicio: el que llama usa un valor de un dominio
    /// enumerado cuya descripción es la ruta del objeto (Servicios/WS_Cotizacion/Cotizacion/Get). La base solo
    /// registra la referencia al dominio, así que el valor se busca en el código de los objetos que lo usan.
    /// </para>
    /// </summary>
    sealed class KbDatabase
    {
        const string InterfaceProperty = "ObjectIsInterface";
        const string EnumValuesProperty = "IDEnumDefinedValues";

        // Mismo filtro que CallBrowserControl.LoadInTree: llamadas (1) y referencias fuertes de tipo 4
        const string LinkFilter = "(x.LinkType = 1 or (x.LinkType = 4 and x.ReferenceType = 1))";

        // Partes que no son código: Variables guarda todos los valores del dominio de cada variable basada en él
        const string NonCodeParts = "'Variables', 'Documentation', 'Help', 'Layout', 'WebForm', 'WinForm'";

        sealed class Container
        {
            public string Name;
            public bool IsModule;
            public bool IsInterface;
            public int ParentTypeId;
            public int ParentEntityId;
        }

        readonly string m_ConnectionString;
        readonly object m_Lock = new object();
        bool m_Prepared;
        int m_ModelId;
        int m_InterfacePropertyId;
        int m_EnumValuesPropertyId;
        Dictionary<long, Container> m_Containers;

        public string Name { get; }
        public string DatabaseName { get; }

        public KbDatabase(string name, string databaseName, string connectionString)
        {
            Name = name;
            DatabaseName = databaseName;
            m_ConnectionString = connectionString;
        }

        /// <summary>Lee una sola vez el modelo de diseño, los ids de propiedad que se usan y los módulos y carpetas.</summary>
        void Prepare(SqlConnection connection)
        {
            lock (m_Lock)
            {
                if (m_Prepared)
                    return;
                using (SqlCommand command = connection.CreateCommand())
                {
                    // Los ids de propiedad son propios de cada KB: se buscan por nombre
                    command.CommandText =
                        "select (select min(ModelId) from UdmModel where ModelParentId = 0), " + PropertyId("@interfaceName") + ", " +
                        PropertyId("@enumName");
                    command.Parameters.Add("@interfaceName", SqlDbType.VarChar, 60).Value = InterfaceProperty;
                    command.Parameters.Add("@enumName", SqlDbType.VarChar, 60).Value = EnumValuesProperty;
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        reader.Read();
                        m_ModelId = reader.IsDBNull(0) ? 1 : reader.GetInt32(0);
                        m_InterfacePropertyId = reader.IsDBNull(1) ? -1 : reader.GetInt32(1);
                        m_EnumValuesPropertyId = reader.IsDBNull(2) ? -1 : reader.GetInt32(2);
                    }
                }

                var containers = new Dictionary<long, Container>();
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select m.EntityTypeId, m.EntityId, m.ModelEntityVersionName, t.EntityTypeName, " +
                        " m.ModelParentEntityTypeId, m.ModelParentEntityId, " + InterfaceColumn("m.EntityTypeId", "m.EntityId") +
                        " from ModelEntityVersion m join EntityType t on t.EntityTypeId = m.EntityTypeId " +
                        " where m.ModelId = @model and t.EntityTypeName in ('Module', 'Folder', 'Udm.Types.Folder')";
                    AddModelParameters(command);
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            containers[KeyOf(reader.GetInt32(0), reader.GetInt32(1))] = new Container
                            {
                                Name           = reader.GetString(2),
                                IsModule       = reader.GetString(3) == "Module",
                                ParentTypeId   = reader.GetInt32(4),
                                ParentEntityId = reader.GetInt32(5),
                                IsInterface    = reader.GetInt32(6) == 1,
                            };
                        }
                    }
                }
                m_Containers = containers;
                m_Prepared = true;
            }
        }

        static string PropertyId(string nameParameter) =>
            "(select top 1 e.EntityId from Entity e join EntityType t on t.EntityTypeId = e.EntityTypeId " +
            " where t.EntityTypeName = 'Udm.Types.Property' and e.EntityGuid = " + nameParameter + ")";

        /// <summary>
        /// El objeto con ese GUID en esta KB (los módulos referenciados conservan el GUID) o, si no está, uno que solo
        /// tiene el nombre calificado para buscarle llamadas por servicio.
        /// </summary>
        public KbReference Find(Guid guid, string qualifiedName)
        {
            using (SqlConnection connection = Open())
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select top 1 e.EntityTypeId, e.EntityId, t.EntityTypeName, m.ModelEntityVersionName " +
                    "from Entity e " +
                    "join ModelEntityVersion m on m.ModelId = @model and m.EntityTypeId = e.EntityTypeId and m.EntityId = e.EntityId " +
                    "join EntityType t on t.EntityTypeId = e.EntityTypeId " +
                    "where e.EntityGuid = @guid";
                AddModelParameters(command);
                command.Parameters.Add("@guid", SqlDbType.VarChar, 60).Value = guid.ToString("D");
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return KbReference.Missing(qualifiedName);
                    return new KbReference
                    {
                        TypeId        = reader.GetInt32(0),
                        EntityId      = reader.GetInt32(1),
                        TypeName      = reader.GetString(2),
                        Name          = reader.GetString(3),
                        QualifiedName = qualifiedName,
                    };
                }
            }
        }

        /// <summary>Objetos propios de esta KB que referencian a <paramref name="target"/> (Is Referenced By), directo o por servicio.</summary>
        public List<KbReference> GetCallers(KbReference target)
        {
            using (SqlConnection connection = Open())
            {
                List<KbReference> result = target.Exists ? DirectCallers(connection, target) : new List<KbReference>();
                var known = new HashSet<long>(result.Select(r => KeyOf(r.TypeId, r.EntityId)));
                foreach (KbReference caller in ServiceCallers(connection, target))
                {
                    if (known.Add(KeyOf(caller.TypeId, caller.EntityId)))
                        result.Add(caller);
                }
                return result;
            }
        }

        List<KbReference> DirectCallers(SqlConnection connection, KbReference target)
        {
            var result = new List<KbReference>();
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "with r as ( " +
                    " select x.FromEntityTypeId t, x.FromEntityId id, max(case when x.ReferenceType = 1 then 1 else 0 end) hard " +
                    " from ModelCrossReference x " +
                    " where x.ModelId = @model and x.ToEntityTypeId = @type and x.ToEntityId = @id and " + LinkFilter +
                    " group by x.FromEntityTypeId, x.FromEntityId) " +
                    "select r.t, r.id, r.hard, et.EntityTypeName, m.ModelEntityVersionName, " +
                    " m.ModelParentEntityTypeId, m.ModelParentEntityId, " + MoreColumn + ", " + InterfaceColumn("r.t", "r.id") +
                    " from r " +
                    "join ModelEntityVersion m on m.ModelId = @model and m.EntityTypeId = r.t and m.EntityId = r.id " +
                    "join EntityType et on et.EntityTypeId = r.t";
                AddModelParameters(command);
                command.Parameters.Add("@type", SqlDbType.Int).Value = target.TypeId;
                command.Parameters.Add("@id", SqlDbType.Int).Value   = target.EntityId;
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        KbReference reference = ReadReference(reader);
                        if (!reference.IsInterface)
                            result.Add(reference);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Objetos que llaman a <paramref name="target"/> por servicio: usan un valor de dominio cuya descripción es la
        /// ruta del objeto. Se buscan los dominios con ese valor y, entre los objetos que usan cada dominio, los que
        /// tienen el valor en su código.
        /// </summary>
        List<KbReference> ServiceCallers(SqlConnection connection, KbReference target)
        {
            var result = new List<KbReference>();
            if (m_EnumValuesPropertyId < 0 || string.IsNullOrEmpty(target.QualifiedName))
                return result;
            string path = target.QualifiedName.Replace('.', '/');

            var domains = new List<(int TypeId, int EntityId, string Name, List<string> Values)>();
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select m.EntityTypeId, m.EntityId, m.ModelEntityVersionName, p.ModelEntityPropertyValue " +
                    "from ModelEntityProperty p " +
                    "join ModelEntityVersion m on m.ModelId = p.ModelId and m.EntityTypeId = p.EntityTypeId and m.EntityId = p.EntityId " +
                    "where p.ModelId = @model and p.ModelEntityPropertyId = @enum and p.ModelEntityPropertyValue like @pattern";
                AddModelParameters(command);
                command.Parameters.Add("@enum", SqlDbType.Int).Value = m_EnumValuesPropertyId;
                command.Parameters.Add("@pattern", SqlDbType.VarChar, 1000).Value = "%<Description>" + LikeEscape(path) + "</Description>%";
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        List<string> values = EnumValuesFor(reader.GetString(3), path);
                        if (values.Count > 0)
                            domains.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), values));
                    }
                }
            }

            var known = new HashSet<long>();
            foreach (var domain in domains)
            {
                // El valor como palabra entera: en el código guardado es un token (<Word>VALOR</Word>)
                var usage = new Regex(@"(?<![A-Za-z0-9_])(" + string.Join("|", domain.Values.Select(Regex.Escape)) + @")(?![A-Za-z0-9_])");
                using (SqlCommand command = connection.CreateCommand())
                {
                    // Partes de la versión actual de cada objeto propio que usa el dominio
                    command.CommandText =
                        "with r as ( " +
                        " select distinct x.FromEntityTypeId t, x.FromEntityId id from ModelCrossReference x " +
                        " where x.ModelId = @model and x.ToEntityTypeId = @type and x.ToEntityId = @id and " + LinkFilter + ") " +
                        "select r.t, r.id, 1, et.EntityTypeName, m.ModelEntityVersionName, " +
                        " m.ModelParentEntityTypeId, m.ModelParentEntityId, " + MoreColumn + ", 0, pv.EntityVersionData " +
                        "from r " +
                        "join ModelEntityVersion m on m.ModelId = @model and m.EntityTypeId = r.t and m.EntityId = r.id " +
                        "join EntityType et on et.EntityTypeId = r.t " +
                        "join EntityVersionComposition c on c.CompoundEntityTypeId = r.t and c.CompoundEntityId = r.id " +
                        " and c.CompoundEntityVersionId = m.EntityVersionId " +
                        "join EntityVersion pv on pv.EntityTypeId = c.ComponentEntityTypeId and pv.EntityId = c.ComponentEntityId " +
                        " and pv.EntityVersionId = c.ComponentEntityVersionId " +
                        "join EntityType pt on pt.EntityTypeId = c.ComponentEntityTypeId " +
                        "where datalength(pv.EntityVersionData) > 0 and pt.EntityTypeName not in (" + NonCodeParts + ") and " +
                        InterfaceColumn("r.t", "r.id") + " = 0";
                    AddModelParameters(command);
                    command.Parameters.Add("@type", SqlDbType.Int).Value = domain.TypeId;
                    command.Parameters.Add("@id", SqlDbType.Int).Value   = domain.EntityId;
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            long key = KeyOf(reader.GetInt32(0), reader.GetInt32(1));
                            if (known.Contains(key))
                                continue;
                            Match match = usage.Match(PartText((byte[])reader.GetValue(9)));
                            if (!match.Success)
                                continue;
                            KbReference reference = ReadReference(reader);
                            if (reference.IsInterface)
                                continue;
                            reference.Via = domain.Name + "." + match.Value;
                            known.Add(key);
                            result.Add(reference);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>Nombres de los valores del dominio (XML de EnumValues) cuya descripción es <paramref name="path"/>.</summary>
        static List<string> EnumValuesFor(string enumValues, string path)
        {
            try
            {
                return XDocument.Parse(enumValues).Descendants("EnumValue")
                    .Where(v => string.Equals(((string)v.Element("Description"))?.Trim(), path, StringComparison.OrdinalIgnoreCase))
                    .Select(v => ((string)v.Element("Name"))?.Trim())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Write("Valores de dominio ilegibles: " + ex.Message);
                return new List<string>();
            }
        }

        /// <summary>Texto de una parte: GeneXus la guarda con un encabezado propio y el contenido en gzip.</summary>
        static string PartText(byte[] data)
        {
            int start = -1;
            for (int i = 0; i + 1 < data.Length && i < 64; i++)
            {
                if (data[i] == 0x1F && data[i + 1] == 0x8B)
                {
                    start = i;
                    break;
                }
            }
            if (start < 0)
                return Encoding.UTF8.GetString(data);
            try
            {
                using (var gzip = new GZipStream(new MemoryStream(data, start, data.Length - start), CompressionMode.Decompress))
                using (var reader = new StreamReader(gzip, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
            catch (InvalidDataException)
            {
                return "";
            }
        }

        KbReference ReadReference(SqlDataReader reader)
        {
            var reference = new KbReference
            {
                TypeId      = reader.GetInt32(0),
                EntityId    = reader.GetInt32(1),
                Hard        = reader.GetInt32(2) == 1,
                TypeName    = reader.GetString(3),
                Name        = reader.GetString(4),
                HasMoreRefs = reader.GetInt32(7) == 1,
                IsInterface = reader.GetInt32(8) == 1,
            };
            reference.QualifiedName = Qualify(reference.Name, reader.GetInt32(5), reader.GetInt32(6), ref reference.IsInterface);
            return reference;
        }

        /// <summary>Nombre con los módulos que lo contienen (sin Root Module); de paso marca si alguno es de interfaz.</summary>
        string Qualify(string name, int parentTypeId, int parentEntityId, ref bool isInterface)
        {
            var modules = new List<string>();
            var visited = new HashSet<long>();
            long key = KeyOf(parentTypeId, parentEntityId);
            while (visited.Add(key) && m_Containers.TryGetValue(key, out Container container))
            {
                isInterface |= container.IsInterface;
                // Root Module no tiene padre y no se nombra
                if (container.IsModule && container.ParentTypeId != 0)
                    modules.Add(container.Name);
                key = KeyOf(container.ParentTypeId, container.ParentEntityId);
            }
            if (modules.Count == 0)
                return name;
            modules.Reverse();
            var text = new StringBuilder();
            foreach (string module in modules)
                text.Append(module).Append('.');
            return text.Append(name).ToString();
        }

        SqlConnection Open()
        {
            var connection = new SqlConnection(m_ConnectionString);
            try
            {
                connection.Open();
                Prepare(connection);
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        // Flecha solo si alguno de los que lo referencian es un objeto (los modelos y la KB no se muestran)
        const string MoreColumn =
            "case when exists(select 1 from ModelCrossReference x join ModelEntityVersion f on f.ModelId = x.ModelId " +
            " and f.EntityTypeId = x.FromEntityTypeId and f.EntityId = x.FromEntityId where x.ModelId = @model " +
            " and x.ToEntityTypeId = r.t and x.ToEntityId = r.id and " + LinkFilter + ") then 1 else 0 end";

        static string InterfaceColumn(string typeColumn, string idColumn) =>
            "case when exists(select 1 from ModelEntityProperty p where p.ModelId = @model and p.EntityTypeId = " + typeColumn +
            " and p.EntityId = " + idColumn + " and p.ModelEntityPropertyId = @interface and p.ModelEntityPropertyValueShort = 'True') " +
            "then 1 else 0 end";

        void AddModelParameters(SqlCommand command)
        {
            command.Parameters.Add("@model", SqlDbType.Int).Value     = m_ModelId;
            command.Parameters.Add("@interface", SqlDbType.Int).Value = m_InterfacePropertyId;
        }

        static string LikeEscape(string text) =>
            text.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

        static long KeyOf(int typeId, int entityId) => ((long)typeId << 32) | (uint)entityId;
    }
}
