using System;
using System.IO;
using System.Text;

namespace GxEnumValues
{
    /// <summary>
    /// Configuración por usuario (%APPDATA%\GxEnumValues\settings.txt). Se edita desde
    /// Tools &gt; Options &gt; GxEnumValues.
    /// </summary>
    static class Settings
    {
        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GxEnumValues", "settings.txt");

        static bool s_Loaded;
        static bool s_Enabled = true;
        static bool s_LogEnabled = true;

        /// <summary>Agregar la búsqueda y el orden por columna al diálogo Enum Values.</summary>
        public static bool Enabled { get { Load(); return s_Enabled; } set { s_Enabled = value; } }

        /// <summary>Registro de diagnóstico en %APPDATA%\GxEnumValues\log.txt.</summary>
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
                        case "Enabled":
                            s_Enabled = value != "false";
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
                    "Enabled=" + (s_Enabled ? "true" : "false"),
                    "Log=" + (s_LogEnabled ? "true" : "false"),
                }, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Artech.Architecture.Common.Services.CommonServices.Output.AddErrorLine(
                    "GxEnumValues: no se pudo guardar la configuración: " + ex.Message);
            }
        }
    }
}
