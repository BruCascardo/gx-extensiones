using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace GxQuickOpen
{
    enum ThemeChoice { Auto, Light, Dark }

    /// <summary>
    /// Configuración por usuario (%APPDATA%\GxQuickOpen\settings.txt). Se edita desde
    /// Tools &gt; Options &gt; GxQuickOpen.
    /// </summary>
    static class Settings
    {
        public static readonly Keys DefaultShowKey = Keys.Control | Keys.O;

        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GxQuickOpen", "settings.txt");

        static bool s_Loaded;
        static Keys s_ShowKey = DefaultShowKey;
        static ThemeChoice s_Theme = ThemeChoice.Auto;
        static bool s_RememberSearch = true;

        /// <summary>Atajo que abre la búsqueda (Keys.None = solo desde el menú File).</summary>
        public static Keys ShowKey { get { Load(); return s_ShowKey; } set { s_ShowKey = value; } }

        /// <summary>Claro u oscuro; Auto = según el fondo del editor de código.</summary>
        public static ThemeChoice Theme { get { Load(); return s_Theme; } set { s_Theme = value; } }

        /// <summary>Volver a abrir con la última búsqueda (seleccionada, así se pisa al escribir).</summary>
        public static bool RememberSearch { get { Load(); return s_RememberSearch; } set { s_RememberSearch = value; } }

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
                        case "Shortcut":
                            if (Shortcuts.TryParse(value, out Keys show)) s_ShowKey = show;
                            break;
                        case "Theme":
                            if (Enum.TryParse(value, true, out ThemeChoice theme)) s_Theme = theme;
                            break;
                        case "RememberSearch":
                            s_RememberSearch = value != "false";
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
                    "Shortcut=" + Shortcuts.Format(s_ShowKey),
                    "Theme=" + s_Theme,
                    "RememberSearch=" + (s_RememberSearch ? "true" : "false"),
                }, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Artech.Architecture.Common.Services.CommonServices.Output.AddErrorLine(
                    "GxQuickOpen: no se pudo guardar la configuración: " + ex.Message);
            }
        }
    }

    /// <summary>Texto ⇄ combinación de teclas, independiente del idioma de Windows ("Ctrl+O", "Ctrl+Shift+P").</summary>
    static class Shortcuts
    {
        public const string NoneText = "(ninguno)";

        public static string Format(Keys keys)
        {
            Keys key = keys & Keys.KeyCode;
            if (key == Keys.None)
                return NoneText;

            var parts = new List<string>();
            if ((keys & Keys.Control) != 0) parts.Add("Ctrl");
            if ((keys & Keys.Alt) != 0) parts.Add("Alt");
            if ((keys & Keys.Shift) != 0) parts.Add("Shift");
            parts.Add(key >= Keys.D0 && key <= Keys.D9 ? ((char)('0' + (key - Keys.D0))).ToString() : key.ToString());
            return string.Join("+", parts);
        }

        public static bool TryParse(string text, out Keys keys)
        {
            keys = Keys.None;
            if (string.IsNullOrWhiteSpace(text) || text == NoneText)
                return true;

            foreach (string raw in text.Split('+'))
            {
                string part = raw.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) keys |= Keys.Control;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) keys |= Keys.Shift;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) keys |= Keys.Alt;
                else if (part.Length == 1 && char.IsDigit(part[0])) keys |= Keys.D0 + (part[0] - '0');
                else if (Enum.TryParse(part, true, out Keys key) && (key & Keys.Modifiers) == 0) keys |= key;
                else return false;
            }
            return IsValid(keys);
        }

        /// <summary>Con Ctrl o Alt, o una tecla de función: una letra sola rompería la escritura normal.</summary>
        public static bool IsValid(Keys keys)
        {
            Keys key = keys & Keys.KeyCode;
            if (key == Keys.None)
                return true;
            if (IsModifierKey(key))
                return false;
            if (key >= Keys.F1 && key <= Keys.F24)
                return true;
            return (keys & (Keys.Control | Keys.Alt)) != 0;
        }

        public static bool IsModifierKey(Keys key) =>
            key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu
            || key == Keys.LControlKey || key == Keys.RControlKey
            || key == Keys.LShiftKey || key == Keys.RShiftKey
            || key == Keys.LMenu || key == Keys.RMenu
            || key == Keys.LWin || key == Keys.RWin;
    }
}
