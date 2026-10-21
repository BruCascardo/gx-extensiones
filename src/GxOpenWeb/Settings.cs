using System;
using System.IO;
using System.Text;

namespace GxOpenWeb
{
    /// <summary>
    /// Configuración por usuario (%APPDATA%\GxOpenWeb\settings.txt). Se edita desde
    /// Tools &gt; Options &gt; GxOpenWeb. En las URLs, {kb} se reemplaza por el nombre de la KB en minúsculas.
    /// </summary>
    static class Settings
    {
        public const string DefaultCiUrl = "";
        public const string DefaultPreprodUrl = "";

        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GxOpenWeb", "settings.txt");

        static bool s_Loaded;
        static string s_LocalUrl = string.Empty;
        static string s_CiUrl = DefaultCiUrl;
        static string s_PreprodUrl = DefaultPreprodUrl;
        static bool s_LogEnabled = true;

        /// <summary>URL base local; vacía = Web root del environment Java de la KB.</summary>
        public static string LocalUrl { get { Load(); return s_LocalUrl; } set { s_LocalUrl = value ?? string.Empty; } }

        /// <summary>URL base de CI.</summary>
        public static string CiUrl { get { Load(); return s_CiUrl; } set { s_CiUrl = value ?? string.Empty; } }

        /// <summary>URL base de Preprod.</summary>
        public static string PreprodUrl { get { Load(); return s_PreprodUrl; } set { s_PreprodUrl = value ?? string.Empty; } }

        /// <summary>Registro de diagnóstico en %APPDATA%\GxOpenWeb\log.txt.</summary>
        public static bool LogEnabled { get { Load(); return s_LogEnabled; } set { s_LogEnabled = value; } }

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
                        case "LocalUrl":
                            s_LocalUrl = value;
                            break;
                        case "CiUrl":
                            s_CiUrl = value;
                            break;
                        case "PreprodUrl":
                            s_PreprodUrl = value;
                            break;
                        case "Log":
                            s_LogEnabled = value != "false";
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
                    "LocalUrl=" + s_LocalUrl,
                    "CiUrl=" + s_CiUrl,
                    "PreprodUrl=" + s_PreprodUrl,
                    "Log=" + (s_LogEnabled ? "true" : "false"),
                }, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Artech.Architecture.Common.Services.CommonServices.Output.AddErrorLine(
                    "GxOpenWeb: no se pudo guardar la configuración: " + ex.Message);
            }
        }
    }
}
