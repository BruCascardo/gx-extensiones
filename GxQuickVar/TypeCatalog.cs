using System;
using System.Collections.Generic;
using System.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.UI.Framework.Services;
using Artech.Genexus.Common;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Services;
using Artech.Genexus.Common.Types;

namespace GxQuickVar
{
    sealed class TypeEntry
    {
        /// <summary>Texto con el formato de GeneXus: "sdtOutput, Sistema.Global" (o "VarChar" en la raíz).</summary>
        public string Name;
        /// <summary>Solo el nombre: "sdtOutput".</summary>
        public string ObjectName;
        /// <summary>Nombre calificado con puntos: "Sistema.Global.sdtOutput".</summary>
        public string QualifiedName;
        /// <summary>Módulo ("Sistema.Global") o vacío si está en la raíz.</summary>
        public string Module;
        public TypeGroup Group;
        public string DefaultParameters;
        /// <summary>Null para los atributos (no son tipos del combo).</summary>
        public DataTypeCaps Caps;
        /// <summary>Texto a la derecha que reemplaza al módulo (los atributos muestran su tipo).</summary>
        public string Secondary;
        /// <summary>Descripción ya calculada (atributos).</summary>
        public string Description;
        /// <summary>Completa <see cref="Secondary"/> la primera vez que se muestra (leer el tipo de un atributo es caro).</summary>
        public Action<TypeEntry> Resolve;

        public void EnsureResolved()
        {
            Action<TypeEntry> resolve = Resolve;
            if (resolve == null)
                return;
            Resolve = null;
            try
            {
                resolve(this);
            }
            catch (Exception)
            {
                // Sin el tipo igual se puede elegir
            }
        }

        public bool StartsWith(string filter) =>
            ObjectName.StartsWith(filter, StringComparison.OrdinalIgnoreCase)
            || Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase)
            || QualifiedName.StartsWith(filter, StringComparison.OrdinalIgnoreCase);

        public bool Contains(string filter) =>
            Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
            || QualifiedName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        public bool IsNamed(string text) =>
            string.Equals(Name, text, StringComparison.OrdinalIgnoreCase)
            || string.Equals(QualifiedName, text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Los tipos que acepta una variable, con la misma fuente que el combo "Type" de la
    /// grilla de variables (DataTypeItemEditor): básicos, dominios, SDTs, BCs, EOs...
    /// Se cachea por modelo; el package lo invalida al grabar dominios/SDTs/transacciones.
    /// </summary>
    static class TypeCatalog
    {
        static KBModel s_Model;
        static List<TypeEntry> s_Entries;
        static readonly Dictionary<TypeEntry, string> s_Descriptions = new Dictionary<TypeEntry, string>();

        public static List<TypeEntry> Get(KBModel model)
        {
            if (s_Entries == null || s_Model != model)
            {
                s_Entries = Load(model);
                s_Model = model;
                s_Descriptions.Clear();
            }
            return s_Entries;
        }

        public static void Clear()
        {
            s_Entries = null;
            s_Model = null;
            s_Descriptions.Clear();
            ClearAttributes();
        }

        // ---- Atributos ("att RecId") ----

        const int MaxAttributes = 100;

        sealed class AttributeSearch
        {
            public List<TypeEntry> Items;
            public bool Truncated;
        }

        static KBModel s_AttributesModel;
        static readonly Dictionary<string, AttributeSearch> s_AttributeSearches = new Dictionary<string, AttributeSearch>();

        static KBModel s_AllAttributesModel;
        static int s_AllAttributesVersion;
        static List<TypeEntry> s_AllAttributes;

        public static void ClearAttributes()
        {
            s_AttributeSearches.Clear();
            s_AttributesModel = null;
            s_AllAttributes = null;
            s_AllAttributesModel = null;
        }

        /// <summary>
        /// Todos los atributos (nombre, módulo y descripción), sacados de la caché de objetos que usa el
        /// Ctrl+O de GeneXus, para buscarlos en memoria con las mismas reglas que los tipos. El tipo de
        /// cada uno se lee recién cuando se muestra. Null si la caché no está disponible.
        /// </summary>
        public static List<TypeEntry> AllAttributes(KBModel model)
        {
            IObjectsServiceCache cache = UIServices.Objects?.Cache;
            if (cache == null || !cache.IsAvailable)
                return null;
            int version = cache.Version;
            if (s_AllAttributes != null && s_AllAttributesModel == model && s_AllAttributesVersion == version)
                return s_AllAttributes;

            var entries = new List<TypeEntry>();
            foreach (IKBObject obj in cache.GetAllOrderByName(model, ObjClass.Attribute))
            {
                if (obj == null || string.IsNullOrEmpty(obj.Name))
                    continue;
                Guid guid = obj.Guid;
                entries.Add(new TypeEntry
                {
                    Name = obj.Name,
                    ObjectName = obj.Name,
                    QualifiedName = obj.Name,
                    Module = obj.QualifiedName?.ModuleName ?? string.Empty,
                    Group = TypeGroup.Attribute,
                    DefaultParameters = string.Empty,
                    Description = string.IsNullOrWhiteSpace(obj.Description) || string.Equals(obj.Description, obj.Name, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : obj.Description,
                    Resolve = entry => entry.Secondary = TypeText(Artech.Genexus.Common.Objects.Attribute.Get(model, guid)),
                });
            }
            s_AllAttributes = entries;
            s_AllAttributesModel = model;
            s_AllAttributesVersion = version;
            return entries;
        }

        static string TypeText(Artech.Genexus.Common.Objects.Attribute attribute)
        {
            if (attribute == null)
                return string.Empty;
            try
            {
                return new DataType(attribute).ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Atributos cuyo nombre empieza con <paramref name="prefix"/>, ordenados por nombre (hasta 100).
        /// Si ya se buscó un prefijo más corto con resultado completo, se filtra en memoria.
        /// </summary>
        public static List<TypeEntry> FindAttributes(KBModel model, string prefix)
        {
            if (s_AttributesModel != model)
            {
                s_AttributeSearches.Clear();
                s_AttributesModel = model;
            }
            string key = prefix.ToLowerInvariant();
            if (s_AttributeSearches.TryGetValue(key, out AttributeSearch cached))
                return cached.Items;

            AttributeSearch search = s_AttributeSearches
                .Where(kv => key.StartsWith(kv.Key, StringComparison.Ordinal) && !kv.Value.Truncated)
                .Select(kv => new AttributeSearch
                {
                    Items = kv.Value.Items.Where(e => e.ObjectName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList(),
                })
                .FirstOrDefault()
                ?? QueryAttributes(model, prefix);
            s_AttributeSearches[key] = search;
            return search.Items;
        }

        static AttributeSearch QueryAttributes(KBModel model, string prefix)
        {
            var search = new AttributeSearch { Items = new List<TypeEntry>() };
            foreach (Artech.Genexus.Common.Objects.Attribute attribute in Artech.Genexus.Common.Objects.Attribute.GetByPartialName(model, prefix))
            {
                // GetByPartialName puede traer coincidencias en cualquier parte: nos quedamos con "empieza con"
                if (!attribute.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (search.Items.Count == MaxAttributes)
                {
                    search.Truncated = true;
                    break;
                }
                search.Items.Add(AttributeEntry(attribute));
            }
            search.Items.Sort((a, b) => string.Compare(a.ObjectName, b.ObjectName, StringComparison.OrdinalIgnoreCase));
            return search;
        }

        static TypeEntry AttributeEntry(Artech.Genexus.Common.Objects.Attribute attribute)
        {
            string type = TypeText(attribute);
            return new TypeEntry
            {
                Name = attribute.Name,
                ObjectName = attribute.Name,
                QualifiedName = attribute.Name,
                Module = string.Empty,
                Group = TypeGroup.Attribute,
                DefaultParameters = string.Empty,
                Secondary = type,
                Description = DescriptionOf(attribute),
            };
        }

        /// <summary>
        /// El tipo de la lista que corresponde exactamente al texto (en cualquiera de sus formas),
        /// o null. Por nombre suelto ("sdtOutput") solo si no es ambiguo entre módulos.
        /// </summary>
        public static TypeEntry FindExact(KBModel model, string text)
        {
            List<TypeEntry> entries = Get(model);
            TypeEntry named = entries.FirstOrDefault(e => e.IsNamed(text));
            if (named != null)
                return named;

            var byObjectName = entries
                .Where(e => string.Equals(e.ObjectName, text, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            return byObjectName.Count == 1 ? byObjectName[0] : null;
        }

        /// <summary>
        /// SDTs del módulo del objeto y de sus módulos superiores, primero los más cercanos:
        /// para "Sistema.Global" → los de Sistema.Global, después los de Sistema, después los de la raíz.
        /// </summary>
        public static List<TypeEntry> ModuleSdts(KBModel model, string modulePath)
        {
            var distance = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string path = modulePath ?? string.Empty;
            for (int level = 0; ; level++)
            {
                distance[path] = level;
                if (path.Length == 0)
                    break;
                int dot = path.LastIndexOf('.');
                path = dot >= 0 ? path.Substring(0, dot) : string.Empty;
            }

            return Get(model)
                .Where(e => e.Group == TypeGroup.Sdt && distance.ContainsKey(e.Module))
                .OrderBy(e => distance[e.Module])
                .ThenBy(e => e.ObjectName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Si el texto se puede usar como tipo en este modelo (tipo de la lista o atributo existente).</summary>
        public static bool IsKnown(KBModel model, string typeText)
        {
            if (TypeSyntax.TryParseAttribute(typeText, out string attributeName))
                return attributeName.Length > 0 && Artech.Genexus.Common.Objects.Attribute.Get(model, attributeName) != null;
            return FindForText(model, typeText) != null;
        }

        /// <summary>El tipo de la lista al que se refiere un texto completo ("VarChar(100)", "sdtX collection").</summary>
        public static TypeEntry FindForText(KBModel model, string typeText)
        {
            return FindExact(model, TypeSyntax.BaseName(typeText, out _));
        }

        /// <summary>
        /// Lleva lo escrito a la forma canónica, para el historial: "varchar(100)" → "VarChar(100)",
        /// "sistema.global.sdtoutput" → "sdtOutput, Sistema.Global".
        /// </summary>
        public static string Normalize(KBModel model, string typeText)
        {
            string text = typeText.Trim();
            string baseName = TypeSyntax.BaseName(text, out string suffix);
            if (TypeSyntax.TryParseAttribute(text, out string attributeName))
            {
                var attribute = Artech.Genexus.Common.Objects.Attribute.Get(model, attributeName);
                return TypeSyntax.AttributePrefix + (attribute?.Name ?? attributeName) + suffix;
            }
            TypeEntry entry = FindExact(model, baseName);
            if (entry == null)
                return text;
            if (!TypeSyntax.HasParameters(text) || !TakesParameters(entry.Group))
                return entry.Name + suffix;
            string withoutSuffix = text.Substring(0, text.Length - suffix.Length);
            return entry.Name + withoutSuffix.Substring(withoutSuffix.IndexOf('(')) + suffix;
        }

        /// <summary>Una línea de ayuda sobre el tipo: base del dominio, descripción del SDT/BC/EO...</summary>
        public static string Describe(KBModel model, TypeEntry entry)
        {
            if (!s_Descriptions.TryGetValue(entry, out string description))
            {
                try
                {
                    description = BuildDescription(model, entry);
                }
                catch (Exception)
                {
                    description = string.Empty;
                }
                s_Descriptions[entry] = description;
            }
            return description;
        }

        static string BuildDescription(KBModel model, TypeEntry entry)
        {
            switch (entry.Group)
            {
                case TypeGroup.Attribute:
                    entry.EnsureResolved();
                    return Join("Atributo", entry.Secondary, entry.Description);
                case TypeGroup.Domain:
                    Domain domain = Domain.Get(model, entry.Caps.Name);
                    return domain == null ? "Dominio" : Join("Dominio", new DataType(domain).ToString(), DescriptionOf(domain));
                case TypeGroup.Sdt:
                    return Join("Structured Data Type", DescriptionOf(SDT.Get(model, entry.Caps.Name)));
                case TypeGroup.Bc:
                    return Join("Business Component", DescriptionOf(Transaction.Get(model, entry.Caps.Name)));
                case TypeGroup.ExternalObject:
                    return Join("Objeto externo", DescriptionOf(ExternalObject.Get(model, entry.Caps.Name)));
                default:
                    return entry.DefaultParameters.Length > 0
                        ? $"Por defecto {entry.ObjectName}({entry.DefaultParameters}) · escribí los parámetros para cambiarlos"
                        : entry.ObjectName;
            }
        }

        static string DescriptionOf(KBObject obj)
        {
            if (obj == null || string.IsNullOrWhiteSpace(obj.Description))
                return null;
            return string.Equals(obj.Description, obj.Name, StringComparison.OrdinalIgnoreCase) ? null : obj.Description;
        }

        static string Join(params string[] parts) =>
            string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        static List<TypeEntry> Load(KBModel model)
        {
            return GenexusUIServices.DataTypes.GetSortedTypes(model, typeof(Variable), true)
                .Where(caps => caps.Key.Type != eDBType.NONE)
                .Select(caps =>
                {
                    TypeGroup group = GroupOf(caps.Key.Type);
                    return new TypeEntry
                    {
                        Name = DataTypeHelper.GetTypeText(caps.Name),
                        ObjectName = caps.Name.ObjectName,
                        QualifiedName = caps.Name.ToString(),
                        Module = caps.Name.ModuleName ?? string.Empty,
                        Group = group,
                        // Los dominios informan AllowLength (heredado de su tipo base) con largo 0: "Dom(0)"
                        // no es un tipo válido. Solo los tipos básicos llevan parámetros.
                        DefaultParameters = TakesParameters(group) ? caps.DefaultParameters ?? string.Empty : string.Empty,
                        Caps = caps,
                    };
                })
                .ToList();
        }

        /// <summary>Si los tipos del grupo se escriben con (largo.decimales): VarChar(100), Numeric(10.2)...</summary>
        public static bool TakesParameters(TypeGroup group)
        {
            switch (group)
            {
                case TypeGroup.Domain:
                case TypeGroup.Sdt:
                case TypeGroup.Bc:
                case TypeGroup.ExternalObject:
                case TypeGroup.Attribute:
                    return false;
                default:
                    return true;
            }
        }

        static TypeGroup GroupOf(eDBType type)
        {
            switch (type)
            {
                case eDBType.CHARACTER:
                case eDBType.VARCHAR:
                case eDBType.LONGVARCHAR:
                    return TypeGroup.Text;
                case eDBType.NUMERIC:
                case eDBType.INT:
                case eDBType.PACKED:
                case eDBType.ZONED:
                    return TypeGroup.Number;
                case eDBType.DATE:
                case eDBType.DATETIME:
                    return TypeGroup.Date;
                case eDBType.Boolean:
                    return TypeGroup.Bool;
                case eDBType.BITMAP:
                case eDBType.BINARY:
                case eDBType.BINARYFILE:
                case eDBType.VIDEO:
                case eDBType.AUDIO:
                    return TypeGroup.Binary;
                case eDBType.GEOGRAPHY:
                case eDBType.GEOPOINT:
                case eDBType.GEOLINE:
                case eDBType.GEOPOLYGON:
                    return TypeGroup.Geo;
                case eDBType.GX_SDT:
                    return TypeGroup.Sdt;
                case eDBType.GX_BUSCOMP:
                case eDBType.GX_BUSCOMP_LEVEL:
                    return TypeGroup.Bc;
                case eDBType.GX_DOM_REF:
                    return TypeGroup.Domain;
                case eDBType.GX_EXTERNAL_OBJECT:
                    return TypeGroup.ExternalObject;
                case eDBType.GX_ATT_REF:
                    return TypeGroup.Attribute;
                default:
                    return TypeGroup.Other;
            }
        }

        public static string BadgeText(TypeGroup group)
        {
            switch (group)
            {
                case TypeGroup.Text: return "abc";
                case TypeGroup.Number: return "123";
                case TypeGroup.Date: return "fch";
                case TypeGroup.Bool: return "bool";
                case TypeGroup.Binary: return "bin";
                case TypeGroup.Geo: return "geo";
                case TypeGroup.Sdt: return "SDT";
                case TypeGroup.Bc: return "BC";
                case TypeGroup.Domain: return "dom";
                case TypeGroup.ExternalObject: return "EO";
                case TypeGroup.Attribute: return "atr";
                default: return "tipo";
            }
        }
    }
}
