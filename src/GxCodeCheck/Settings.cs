using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace GxCodeCheck
{
    /// <summary>
    /// Configuración por usuario (%APPDATA%\GxCodeCheck\settings.txt). Se edita desde
    /// Tools &gt; Options &gt; GxCodeCheck.
    /// </summary>
    static class Settings
    {
        public const int MaxSpecifierDelay = 3000;
        public static readonly Keys DefaultFixKey = Keys.Control | Keys.OemPeriod;

        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GxCodeCheck", "settings.txt");

        static bool s_Loaded;
        static bool s_Enabled = true;
        static int s_SpecifierDelay = 300;
        static bool s_ShowSpecifier = true;
        static bool s_QuickTypes = true;
        static bool s_CheckCallers = true;
        static bool s_Standards = true;
        static readonly HashSet<string> s_StandardsOff = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool s_ShowWarnings = true;
        static bool s_LogEnabled = true;
        static Keys s_FixKey = DefaultFixKey;
        static KbCheck.ScopeKind s_ReviewScope = KbCheck.ScopeKind.Pending;
        static string s_ReviewModule = "";
        static DateTime s_ReviewSince = DateTime.MinValue;
        static bool s_ReviewWithUsers = true;
        static bool s_ReviewWarnings;

        /// <summary>Marcar los errores en el código.</summary>
        public static bool Enabled { get { Load(); return s_Enabled; } set { s_Enabled = value; } }

        /// <summary>
        /// Milisegundos que GeneXus espera después de guardar para especificar en segundo plano (Background
        /// Build). GeneXus trae 3000; menos es menos tiempo hasta ver los errores de tipos y parámetros.
        /// </summary>
        public static int SpecifierDelay { get { Load(); return s_SpecifierDelay; } set { s_SpecifierDelay = Clamp(value); } }

        /// <summary>Marcar los errores de la especificación (Background Build al guardar, y los builds).</summary>
        public static bool ShowSpecifier { get { Load(); return s_ShowSpecifier; } set { s_ShowSpecifier = value; } }

        /// <summary>Chequeo de tipos propio al guardar, sin esperar a la especificación.</summary>
        public static bool QuickTypes { get { Load(); return s_QuickTypes; } set { s_QuickTypes = value; } }

        /// <summary>Al cambiar el parm() de un procedimiento, revisar los objetos que lo llaman.</summary>
        public static bool CheckCallers { get { Load(); return s_CheckCallers; } set { s_CheckCallers = value; } }

        /// <summary>Marcar lo que no sigue el estándar del equipo (<see cref="TypeChecker.StandardRules"/>).</summary>
        public static bool Standards { get { Load(); return s_Standards; } set { s_Standards = value; } }

        /// <summary>Las reglas del estándar apagadas, por su código.</summary>
        public static HashSet<string> StandardsOff { get { Load(); return s_StandardsOff; } }

        /// <summary>Si se marca la regla del estándar.</summary>
        public static bool IsStandardOn(string code) => Standards && !StandardsOff.Contains(code);

        /// <summary>Marcar también las advertencias, no solo los errores.</summary>
        public static bool ShowWarnings { get { Load(); return s_ShowWarnings; } set { s_ShowWarnings = value; } }

        /// <summary>El atajo que abre el cartel de lo marcado donde está el cursor, para elegir una corrección.</summary>
        public static Keys FixKey { get { Load(); return s_FixKey; } set { s_FixKey = value; } }

        /// <summary>La última revisión de código (Tools &gt; GxCodeCheck: revisar el código): qué se revisó.</summary>
        public static KbCheck.ScopeKind ReviewScope { get { Load(); return s_ReviewScope; } set { s_ReviewScope = value; } }
        public static string ReviewModule { get { Load(); return s_ReviewModule; } set { s_ReviewModule = value ?? ""; } }
        public static DateTime ReviewSince { get { Load(); return s_ReviewSince; } set { s_ReviewSince = value; } }
        public static bool ReviewWithUsers { get { Load(); return s_ReviewWithUsers; } set { s_ReviewWithUsers = value; } }
        public static bool ReviewWarnings { get { Load(); return s_ReviewWarnings; } set { s_ReviewWarnings = value; } }

        /// <summary>Registro de diagnóstico en %APPDATA%\GxCodeCheck\log.txt.</summary>
        public static bool LogEnabled { get { Load(); return s_LogEnabled; } set { s_LogEnabled = value; } }

        static int Clamp(int value) => Math.Max(0, Math.Min(MaxSpecifierDelay, value));

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
                        case "SpecifierDelay":
                            if (int.TryParse(value, out int delay))
                                s_SpecifierDelay = Clamp(delay);
                            break;
                        case "ShowSpecifier":
                            s_ShowSpecifier = value != "false";
                            break;
                        case "CheckCallers":
                            s_CheckCallers = value != "false";
                            break;
                        case "Standards":
                            s_Standards = value != "false";
                            break;
                        case "StandardsOff":
                            s_StandardsOff.UnionWith(value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()));
                            break;
                        case "QuickTypes":
                            s_QuickTypes = value != "false";
                            break;
                        case "ShowWarnings":
                            s_ShowWarnings = value != "false";
                            break;
                        case "FixShortcut":
                            if (Shortcuts.TryParse(value, out Keys fix))
                                s_FixKey = fix;
                            break;
                        case "ReviewScope":
                            if (Enum.TryParse(value, out KbCheck.ScopeKind scope))
                                s_ReviewScope = scope;
                            break;
                        case "ReviewModule":
                            s_ReviewModule = value;
                            break;
                        case "ReviewSince":
                            if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime since))
                                s_ReviewSince = since;
                            break;
                        case "ReviewWithUsers":
                            s_ReviewWithUsers = value != "false";
                            break;
                        case "ReviewWarnings":
                            s_ReviewWarnings = value == "true";
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
                    "SpecifierDelay=" + s_SpecifierDelay,
                    "ShowSpecifier=" + (s_ShowSpecifier ? "true" : "false"),
                    "QuickTypes=" + (s_QuickTypes ? "true" : "false"),
                    "CheckCallers=" + (s_CheckCallers ? "true" : "false"),
                    "Standards=" + (s_Standards ? "true" : "false"),
                    "StandardsOff=" + string.Join(",", s_StandardsOff.OrderBy(c => c)),
                    "ShowWarnings=" + (s_ShowWarnings ? "true" : "false"),
                    "FixShortcut=" + Shortcuts.Format(s_FixKey),
                    "ReviewScope=" + s_ReviewScope,
                    "ReviewModule=" + s_ReviewModule,
                    "ReviewSince=" + s_ReviewSince.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    "ReviewWithUsers=" + (s_ReviewWithUsers ? "true" : "false"),
                    "ReviewWarnings=" + (s_ReviewWarnings ? "true" : "false"),
                    "Log=" + (s_LogEnabled ? "true" : "false"),
                }, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Artech.Architecture.Common.Services.CommonServices.Output.AddErrorLine(
                    "GxCodeCheck: no se pudo guardar la configuración: " + ex.Message);
            }
        }
    }
}
