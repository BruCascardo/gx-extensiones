using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Artech.Udm.Architecture.Common;

namespace GxKbRefs
{
    /// <summary>
    /// Arma la lista de KBs donde buscar: las bases GX_KB_* adjuntas al servidor de la KB abierta y las KBs de las
    /// carpetas configuradas (por defecto, la carpeta que contiene a la KB abierta). Una KB de carpeta que nunca se
    /// abrió en esta máquina no está adjunta al servidor: se adjunta con su .mdf, como hace GeneXus al abrirla.
    /// </summary>
    static class KbDiscovery
    {
        const string DatabasePrefix = "GX_KB_";
        const string ConnectionFile = "knowledgebase.connection";
        const int ConnectTimeoutSeconds = 15;
        const int FolderDepth = 2;

        public static List<KbDatabase> Discover(IKBConnectionInfo current, string currentLocation, ICollection<string> excluded,
                                                IEnumerable<string> folders)
        {
            SqlConnectionStringBuilder server = ServerConnection(current);
            var found = new Dictionary<string, KbDatabase>(StringComparer.OrdinalIgnoreCase);
            string currentDatabase = current.DBName ?? "";
            var attached = new HashSet<string>(AttachedDatabases(server, currentDatabase), StringComparer.OrdinalIgnoreCase);

            foreach (string folder in KbFolders(currentLocation, folders))
            {
                try
                {
                    KbDatabase kb = FromFolder(folder, server, current.ServerInstance, attached);
                    if (kb != null && !kb.DatabaseName.Equals(currentDatabase, StringComparison.OrdinalIgnoreCase))
                        found[kb.DatabaseName] = kb;
                }
                catch (Exception ex)
                {
                    Log.Write("No se pudo leer " + Path.Combine(folder, ConnectionFile) + ": " + ex.Message);
                }
            }

            foreach (string databaseName in attached)
            {
                if (!found.ContainsKey(databaseName))
                {
                    var database = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = databaseName };
                    found[databaseName] = new KbDatabase(NameOf(databaseName), databaseName, database.ConnectionString);
                }
            }

            return found.Values
                .Where(kb => !excluded.Contains(kb.Name) && !excluded.Contains(kb.DatabaseName))
                .OrderBy(kb => kb.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static string NameOf(string databaseName) =>
            databaseName.StartsWith(DatabasePrefix, StringComparison.OrdinalIgnoreCase) ? databaseName.Substring(DatabasePrefix.Length) : databaseName;

        static List<string> AttachedDatabases(SqlConnectionStringBuilder server, string currentDatabase)
        {
            var result = new List<string>();
            using (var connection = new SqlConnection(server.ConnectionString))
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select name from sys.databases " +
                    "where name like 'GX[_]KB[_]%' and state = 0 and has_dbaccess(name) = 1 and name <> @current";
                command.Parameters.Add("@current", SqlDbType.NVarChar, 128).Value = currentDatabase;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        result.Add(reader.GetString(0));
                }
            }
            return result;
        }

        /// <summary>Carpetas con knowledgebase.connection debajo de las carpetas configuradas (hasta dos niveles).</summary>
        static IEnumerable<string> KbFolders(string currentLocation, IEnumerable<string> folders)
        {
            List<string> roots = folders.Where(Directory.Exists).ToList();
            if (roots.Count == 0 && !string.IsNullOrEmpty(currentLocation))
            {
                string parent = Path.GetDirectoryName(currentLocation.TrimEnd('\\', '/'));
                if (parent != null && Directory.Exists(parent))
                    roots.Add(parent);
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots)
            {
                foreach (string folder in Below(root, FolderDepth))
                {
                    if (File.Exists(Path.Combine(folder, ConnectionFile)) && seen.Add(Path.GetFullPath(folder)))
                        yield return folder;
                }
            }
        }

        static IEnumerable<string> Below(string folder, int depth)
        {
            if (depth == 0)
                yield break;
            string[] children;
            try
            {
                children = Directory.GetDirectories(folder);
            }
            catch (Exception)
            {
                yield break;
            }
            foreach (string child in children)
            {
                yield return child;
                // Una KB no tiene otras KBs adentro: no se recorren sus carpetas
                if (!File.Exists(Path.Combine(child, ConnectionFile)))
                {
                    foreach (string grandChild in Below(child, depth - 1))
                        yield return grandChild;
                }
            }
        }

        /// <summary>
        /// La KB de una carpeta, con la conexión de su knowledgebase.connection. En el servidor de la KB abierta se usan
        /// sus datos de acceso; en otro servidor solo con seguridad integrada (la contraseña no está a mano).
        /// </summary>
        static KbDatabase FromFolder(string folder, SqlConnectionStringBuilder currentServer, string currentInstance, ICollection<string> attached)
        {
            XElement info = XDocument.Load(Path.Combine(folder, ConnectionFile)).Root;
            string databaseName = info?.Element("DBName")?.Value?.Trim();
            if (string.IsNullOrEmpty(databaseName))
                return null;
            string serverInstance = info.Element("ServerInstance")?.Value?.Trim();
            bool integrated = !"False".Equals(info.Element("IntegratedSecurity")?.Value?.Trim(), StringComparison.OrdinalIgnoreCase);
            bool sameServer = string.IsNullOrEmpty(serverInstance) ||
                              serverInstance.Equals(currentServer.DataSource, StringComparison.OrdinalIgnoreCase) ||
                              serverInstance.Equals(currentInstance, StringComparison.OrdinalIgnoreCase);

            SqlConnectionStringBuilder builder;
            if (sameServer)
                builder = new SqlConnectionStringBuilder(currentServer.ConnectionString);
            else if (integrated)
                builder = new SqlConnectionStringBuilder(currentServer.ConnectionString) { DataSource = serverInstance, IntegratedSecurity = true };
            else
                return null;
            builder.InitialCatalog = databaseName;

            // Sin adjuntar al servidor de la KB abierta: el .mdf de la carpeta de la KB (Directory puede haber
            // quedado de otra máquina) para que SqlClient la adjunte al conectarse
            string dataFile = info.Element("DataFile")?.Value?.Trim();
            if (sameServer && !attached.Contains(databaseName) && !string.IsNullOrEmpty(dataFile))
            {
                string mdf = Path.Combine(folder, dataFile);
                if (!File.Exists(mdf))
                    mdf = Path.Combine(info.Element("Directory")?.Value?.Trim() ?? folder, dataFile);
                if (File.Exists(mdf))
                    builder.AttachDBFilename = mdf;
            }
            return new KbDatabase(Path.GetFileName(folder), databaseName, builder.ConnectionString);
        }

        /// <summary>Conexión a master con los datos de acceso de la KB abierta.</summary>
        static SqlConnectionStringBuilder ServerConnection(IKBConnectionInfo current)
        {
            var builder = new SqlConnectionStringBuilder();
            foreach (string candidate in new[] { current.ConnectionStringFull, current.ConnectionStringBasic })
            {
                try
                {
                    builder = new SqlConnectionStringBuilder(candidate ?? "");
                    if (!string.IsNullOrEmpty(builder.DataSource))
                        break;
                }
                catch (ArgumentException)
                {
                    // Formato que SqlClient no entiende: se prueba con el otro o se arma a mano
                }
            }
            if (string.IsNullOrEmpty(builder.DataSource))
            {
                builder = new SqlConnectionStringBuilder { DataSource = current.ServerInstance, IntegratedSecurity = true };
            }
            builder.InitialCatalog = "master";
            builder.AttachDBFilename = "";
            // LocalDB puede estar detenida: arrancarla lleva unos segundos
            builder.ConnectTimeout = ConnectTimeoutSeconds;
            builder.ApplicationName = "GxKbRefs";
            return builder;
        }
    }
}
