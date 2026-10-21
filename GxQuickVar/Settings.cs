using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace GxQuickVar
{
    /// <summary>
    /// Configuración por usuario (%APPDATA%\GxQuickVar\settings.txt). Se edita desde
    /// Tools &gt; Options &gt; GxQuickVar.
    /// </summary>
    static class Settings
    {
        public static readonly Keys DefaultDeclareKey = Keys.Alt | Keys.V;
        public static readonly Keys DefaultDeclareAllKey = Keys.Alt | Keys.Shift | Keys.V;

        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GxQuickVar", "settings.txt");

        static bool s_Loaded;
        static Keys s_DeclareKey = DefaultDeclareKey;
        static Keys s_DeclareAllKey = DefaultDeclareAllKey;
        static bool s_RememberByName = true;
        static bool s_SuggestByName = true;

        /// <summary>Atajo para declarar la variable bajo el cursor (Keys.None = sin atajo).</summary>
        public static Keys DeclareKey { get { Load(); return s_DeclareKey; } set { s_DeclareKey = value; } }

        /// <summary>Atajo para declarar todas las variables sin definir del editor.</summary>
        public static Keys DeclareAllKey { get { Load(); return s_DeclareAllKey; } set { s_DeclareAllKey = value; } }

        /// <summary>Sugerir el último tipo que se usó para el mismo nombre de variable.</summary>
        public static bool RememberByName { get { Load(); return s_RememberByName; } set { s_RememberByName = value; } }

        /// <summary>Sugerir un SDT/BC/dominio que se llame igual que la variable.</summary>
        public static bool SuggestByName { get { Load(); return s_SuggestByName; } set { s_SuggestByName = value; } }

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
                        case "DeclareShortcut":
                            if (Shortcuts.TryParse(value, out Keys declare)) s_DeclareKey = declare;
                            break;
                        case "DeclareAllShortcut":
                            if (Shortcuts.TryParse(value, out Keys declareAll)) s_DeclareAllKey = declareAll;
                            break;
                        case "RememberByName":
                            s_RememberByName = value != "false";
                            break;
                        case "SuggestByName":
                            s_SuggestByName = value != "false";
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
                    "DeclareShortcut=" + Shortcuts.Format(s_DeclareKey),
                    "DeclareAllShortcut=" + Shortcuts.Format(s_DeclareAllKey),
                    "RememberByName=" + (s_RememberByName ? "true" : "false"),
                    "SuggestByName=" + (s_SuggestByName ? "true" : "false"),
                }, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Artech.Architecture.Common.Services.CommonServices.Output.AddErrorLine(
                    "GxQuickVar: no se pudo guardar la configuración: " + ex.Message);
            }
        }
    }

    /// <summary>Texto ⇄ combinación de teclas, independiente del idioma de Windows ("Ctrl+Shift+T", "Alt+V").</summary>
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
            parts.Add(KeyName(key));
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

        /// <summary>
        /// Un atajo usable dentro del editor: con Ctrl o Alt, o una tecla de función. Sin eso
        /// (una letra sola, Shift+letra) rompería la escritura normal.
        /// </summary>
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

        static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
                return ((char)('0' + (key - Keys.D0))).ToString();
            return key.ToString();
        }

        /// <summary>
        /// Atajos que ya usa GeneXus en sus menús (relevados de los manifiestos de sus packages).
        /// Dentro del editor de código gana GxQuickVar, pero conviene avisar.
        /// </summary>
        public static bool IsUsedByGeneXus(Keys keys)
        {
            return GeneXusShortcuts.Contains(keys) || IsAltMenuMnemonic(keys);
        }

        // Alt+letra de los menús principales: File, Edit, View, Insert, Build, Knowledge Manager, Tools, Window, Help
        static bool IsAltMenuMnemonic(Keys keys)
        {
            if ((keys & Keys.Modifiers) != Keys.Alt)
                return false;
            Keys key = keys & Keys.KeyCode;
            return key == Keys.F || key == Keys.E || key == Keys.V || key == Keys.I || key == Keys.B
                || key == Keys.K || key == Keys.T || key == Keys.W || key == Keys.H;
        }

        static readonly HashSet<Keys> GeneXusShortcuts = new HashSet<Keys>(
            new[]
            {
                "Ctrl+A", "Ctrl+B", "Ctrl+C", "Ctrl+D", "Ctrl+E", "Ctrl+F", "Ctrl+G", "Ctrl+H", "Ctrl+I", "Ctrl+J",
                "Ctrl+K", "Ctrl+L", "Ctrl+M", "Ctrl+N", "Ctrl+O", "Ctrl+P", "Ctrl+Q", "Ctrl+R", "Ctrl+S", "Ctrl+T",
                "Ctrl+U", "Ctrl+V", "Ctrl+X", "Ctrl+Y", "Ctrl+Z",
                "Ctrl+Shift+A", "Ctrl+Shift+B", "Ctrl+Shift+C", "Ctrl+Shift+D", "Ctrl+Shift+E", "Ctrl+Shift+F",
                "Ctrl+Shift+G", "Ctrl+Shift+H", "Ctrl+Shift+I", "Ctrl+Shift+J", "Ctrl+Shift+K", "Ctrl+Shift+L",
                "Ctrl+Shift+M", "Ctrl+Shift+N", "Ctrl+Shift+O", "Ctrl+Shift+Q", "Ctrl+Shift+R", "Ctrl+Shift+S",
                "Ctrl+Shift+U", "Ctrl+Shift+V",
                "F1", "F2", "F3", "F4", "F5", "F6", "F8", "F9", "F10", "F12",
                "Shift+F2", "Shift+F3", "Shift+F11", "Shift+F12",
                "Ctrl+F1", "Ctrl+F3", "Ctrl+F4", "Ctrl+F5", "Ctrl+F12",
                "Ctrl+Shift+F3", "Ctrl+Shift+F4", "Ctrl+Shift+F8", "Ctrl+Shift+F11",
            }
            .Select(text => { TryParseRaw(text, out Keys k); return k; }));

        static void TryParseRaw(string text, out Keys keys)
        {
            keys = Keys.None;
            foreach (string part in text.Split('+'))
            {
                if (part == "Ctrl") keys |= Keys.Control;
                else if (part == "Shift") keys |= Keys.Shift;
                else if (part == "Alt") keys |= Keys.Alt;
                else keys |= (Keys)Enum.Parse(typeof(Keys), part);
            }
        }
    }
}
