using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GxKbRefs
{
    /// <summary>
    /// Configuración por usuario (%APPDATA%\GxKbRefs\settings.txt). Se edita desde
    /// Tools &gt; Options &gt; GxKbRefs.
    /// </summary>
    static class Settings
    {
        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GxKbRefs", "settings.txt");

        static bool s_Loaded;
        static bool s_Enabled = true;
        static bool s_LogEnabled = true;
        static string s_ExcludedKbs = "";
        static string s_KbFolders = "";
        static bool s_IncludeFixes;
        static string s_FixesPattern = "Fixes";

        /// <summary>Agregar el checkbox "Otras KBs" a la ventana References.</summary>
        public static bool Enabled { get { Load(); return s_Enabled; } set { s_Enabled = value; } }

        /// <summary>Registro de diagnóstico en %APPDATA%\GxKbRefs\log.txt.</summary>
        public static bool LogEnabled { get { Load(); return s_LogEnabled; } set { s_LogEnabled = value; } }

        /// <summary>KBs en las que no se busca, separadas por coma (nombre de la KB o de su base, GX_KB_...).</summary>
        public static string ExcludedKbs { get { Load(); return s_ExcludedKbs; } set { s_ExcludedKbs = value ?? ""; } }

        /// <summary>
        /// Carpetas donde buscar KBs (con knowledgebase.connection, hasta dos niveles abajo), separadas por punto y coma.
        /// Vacío: la carpeta que contiene a la KB abierta.
        /// </summary>
        public static string KbFolders { get { Load(); return s_KbFolders; } set { s_KbFolders = value ?? ""; } }

        /// <summary>Si "Incluir Fixes" arranca tildado en cada ventana References.</summary>
        public static bool IncludeFixes { get { Load(); return s_IncludeFixes; } set { s_IncludeFixes = value; } }

        /// <summary>Texto en el nombre de la KB (o de su base) que la marca como KB de fixes.</summary>
        public static string FixesPattern { get { Load(); return s_FixesPattern; } set { s_FixesPattern = (value ?? "").Trim(); } }

        public static List<string> KbFolderList() =>
            KbFolders.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        public static HashSet<string> ExcludedKbSet() =>
            new HashSet<string>(
                ExcludedKbs.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);

        static void Load()
        {
            if (s_Loaded)
                return;
            s_Loaded = true;
            try
            {
                if (!File.Exists(FilePath))
                    return;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "Enabled":
                            s_Enabled = value != "false";
                            break;
                        case "Log":
                            s_LogEnabled = value != "false";
                            break;
                        case "ExcludedKbs":
                            s_ExcludedKbs = value;
                            break;
                        case "KbFolders":
                            s_KbFolders = value;
                            break;
                        case "IncludeFixes":
                            s_IncludeFixes = value == "true";
                            break;
                        case "FixesPattern":
                            s_FixesPattern = value;
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // Configuración ilegible: valores por defecto
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new[]
                {
                    "Enabled=" + (s_Enabled ? "true" : "false"),
                    "Log=" + (s_LogEnabled ? "true" : "false"),
                    "ExcludedKbs=" + s_ExcludedKbs,
                    "KbFolders=" + s_KbFolders,
                    "IncludeFixes=" + (s_IncludeFixes ? "true" : "false"),
                    "FixesPattern=" + s_FixesPattern,
                }, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Artech.Architecture.Common.Services.CommonServices.Output.AddErrorLine(
                    "GxKbRefs: no se pudo guardar la configuración: " + ex.Message);
            }
        }
    }
}
