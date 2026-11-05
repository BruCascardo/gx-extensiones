using System;
using System.IO;
using System.Text;

namespace GxTabScroll
{
    /// <summary>
    /// Configuración por usuario (%APPDATA%\GxTabScroll\settings.txt). Se edita desde
    /// Tools &gt; Options &gt; GxTabScroll.
    /// </summary>
    static class Settings
    {
        public const int MinTabsPerStep = 1;
        public const int MaxTabsPerStep = 5;

        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GxTabScroll", "settings.txt");

        static bool s_Loaded;
        static bool s_Enabled = true;
        static bool s_SwitchTabs;
        static int s_TabsPerStep = MinTabsPerStep;
        static bool s_CloseWithCtrlW = true;
        static bool s_LogEnabled = true;

        /// <summary>La rueda sobre la barra de pestañas la desplaza (o cambia de pestaña).</summary>
        public static bool Enabled { get { Load(); return s_Enabled; } set { s_Enabled = value; } }

        /// <summary>La rueda cambia de pestaña y Shift+rueda desplaza la barra (al revés que por defecto).</summary>
        public static bool SwitchTabs { get { Load(); return s_SwitchTabs; } set { s_SwitchTabs = value; } }

        /// <summary>Pestañas que se desplaza la barra por cada paso de la rueda.</summary>
        public static int TabsPerStep { get { Load(); return s_TabsPerStep; } set { s_TabsPerStep = Clamp(value); } }

        /// <summary>Ctrl+W con el foco dentro de una pestaña de objeto la cierra.</summary>
        public static bool CloseWithCtrlW { get { Load(); return s_CloseWithCtrlW; } set { s_CloseWithCtrlW = value; } }

        /// <summary>Registro de diagnóstico en %APPDATA%\GxTabScroll\log.txt.</summary>
        public static bool LogEnabled { get { Load(); return s_LogEnabled; } set { s_LogEnabled = value; } }

        static int Clamp(int value) => Math.Max(MinTabsPerStep, Math.Min(MaxTabsPerStep, value));

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
                        case "SwitchTabs":
                            s_SwitchTabs = value == "true";
                            break;
                        case "TabsPerStep":
                            if (int.TryParse(value, out int tabs))
                                s_TabsPerStep = Clamp(tabs);
                            break;
                        case "CloseWithCtrlW":
                            s_CloseWithCtrlW = value != "false";
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
                    "SwitchTabs=" + (s_SwitchTabs ? "true" : "false"),
                    "TabsPerStep=" + s_TabsPerStep,
                    "CloseWithCtrlW=" + (s_CloseWithCtrlW ? "true" : "false"),
                    "Log=" + (s_LogEnabled ? "true" : "false"),
                }, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Artech.Architecture.Common.Services.CommonServices.Output.AddErrorLine(
                    "GxTabScroll: no se pudo guardar la configuración: " + ex.Message);
            }
        }
    }
}
