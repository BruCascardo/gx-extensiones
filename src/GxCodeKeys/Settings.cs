using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace GxCodeKeys
{
    enum CodeCommand { MoveLineUp, MoveLineDown, DuplicateLineUp, DuplicateLineDown, CursorAbove, CursorBelow }

    sealed class CommandInfo
    {
        public CodeCommand Command;
        /// <summary>Clave en settings.txt.</summary>
        public string Key;
        public string Caption;
        public Keys DefaultKeys;
    }

    /// <summary>
    /// Configuración por usuario (%APPDATA%\GxCodeKeys\settings.txt). Se edita desde
    /// Tools &gt; Options &gt; GxCodeKeys.
    /// </summary>
    static class Settings
    {
        // Los mismos que VS Code en Windows
        public static readonly IReadOnlyList<CommandInfo> Commands = new[]
        {
            new CommandInfo { Command = CodeCommand.MoveLineUp, Key = "MoveLineUp", Caption = "Mover línea arriba:", DefaultKeys = Keys.Alt | Keys.Up },
            new CommandInfo { Command = CodeCommand.MoveLineDown, Key = "MoveLineDown", Caption = "Mover línea abajo:", DefaultKeys = Keys.Alt | Keys.Down },
            new CommandInfo { Command = CodeCommand.DuplicateLineUp, Key = "DuplicateLineUp", Caption = "Duplicar línea arriba:", DefaultKeys = Keys.Alt | Keys.Shift | Keys.Up },
            new CommandInfo { Command = CodeCommand.DuplicateLineDown, Key = "DuplicateLineDown", Caption = "Duplicar línea abajo:", DefaultKeys = Keys.Alt | Keys.Shift | Keys.Down },
            new CommandInfo { Command = CodeCommand.CursorAbove, Key = "CursorAbove", Caption = "Agregar cursor arriba:", DefaultKeys = Keys.Control | Keys.Alt | Keys.Up },
            new CommandInfo { Command = CodeCommand.CursorBelow, Key = "CursorBelow", Caption = "Agregar cursor abajo:", DefaultKeys = Keys.Control | Keys.Alt | Keys.Down },
        };

        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GxCodeKeys", "settings.txt");

        static bool s_Loaded;
        static readonly Dictionary<CodeCommand, Keys> s_Keys = Commands.ToDictionary(c => c.Command, c => c.DefaultKeys);
        static bool s_AltClickCursors = true;

        /// <summary>Atajo de un comando (Keys.None = sin atajo).</summary>
        public static Keys GetKeys(CodeCommand command)
        {
            Load();
            return s_Keys[command];
        }

        public static void SetKeys(CodeCommand command, Keys keys) => s_Keys[command] = keys;

        /// <summary>Alt+Click agrega cursores (si no, Alt+Click queda como en GeneXus: selección en bloque).</summary>
        public static bool AltClickCursors { get { Load(); return s_AltClickCursors; } set { s_AltClickCursors = value; } }

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
                    CommandInfo command = Commands.FirstOrDefault(c => c.Key == key);
                    if (command != null)
                    {
                        if (Shortcuts.TryParse(value, out Keys keys))
                            s_Keys[command.Command] = keys;
                    }
                    else if (key == "AltClickCursors")
                    {
                        s_AltClickCursors = value != "false";
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
                var lines = Commands.Select(c => c.Key + "=" + Shortcuts.Format(s_Keys[c.Command])).ToList();
                lines.Add("AltClickCursors=" + (s_AltClickCursors ? "true" : "false"));
                File.WriteAllLines(FilePath, lines, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Error("guardar la configuración", ex);
            }
        }
    }

    /// <summary>Texto ⇄ combinación de teclas, independiente del idioma de Windows ("Ctrl+Alt+Up", "Alt+Shift+Down").</summary>
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
        /// (una flecha sola, Shift+letra) rompería la edición normal.
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
            switch (key)
            {
                case Keys.Prior: return "PageUp";
                case Keys.Next: return "PageDown";
                case Keys.Return: return "Enter";
            }
            return key.ToString();
        }

        /// <summary>
        /// Con qué choca un atajo dentro del editor de código, o null si está libre. Dentro del
        /// editor gana GxCodeKeys (intercepta la tecla antes), así que el otro deja de funcionar ahí.
        /// </summary>
        public static string ConflictOf(Keys keys)
        {
            if ((keys & Keys.KeyCode) == Keys.None)
                return null;
            if (EditorShortcuts.TryGetValue(keys, out string editorCommand))
                return "el editor de GeneXus (" + editorCommand + ")";
            if (GeneXusShortcuts.Contains(keys))
                return "un comando de los menús de GeneXus";
            if (IsAltMenuMnemonic(keys))
                return "la apertura de un menú de GeneXus";
            string quickVar = GxQuickVarCommandFor(keys);
            if (quickVar != null)
                return "GxQuickVar (" + quickVar + ")";
            return null;
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

        /// <summary>
        /// Atajos propios del editor de código (ActiproSoftware SyntaxEditor 4.0, el control que usa
        /// GeneXus), relevados de su tabla de teclas por defecto. Solo los que tienen Ctrl o Alt.
        /// </summary>
        static readonly Dictionary<Keys, string> EditorShortcuts = new Dictionary<Keys, string>
        {
            [Keys.Alt | Keys.Shift | Keys.Up] = "selección en bloque hacia arriba",
            [Keys.Alt | Keys.Shift | Keys.Down] = "selección en bloque hacia abajo",
            [Keys.Alt | Keys.Shift | Keys.Left] = "selección en bloque a la izquierda",
            [Keys.Alt | Keys.Shift | Keys.Right] = "selección en bloque a la derecha",
            [Keys.Control | Keys.Alt | Keys.Shift | Keys.Left] = "selección en bloque por palabra",
            [Keys.Control | Keys.Alt | Keys.Shift | Keys.Right] = "selección en bloque por palabra",
            [Keys.Control | Keys.Alt | Keys.Shift | Keys.T] = "transponer líneas",
            [Keys.Control | Keys.Up] = "desplazar la vista hacia arriba",
            [Keys.Control | Keys.Down] = "desplazar la vista hacia abajo",
            [Keys.Control | Keys.Left] = "palabra anterior",
            [Keys.Control | Keys.Right] = "palabra siguiente",
            [Keys.Control | Keys.Shift | Keys.Left] = "seleccionar hasta la palabra anterior",
            [Keys.Control | Keys.Shift | Keys.Right] = "seleccionar hasta la palabra siguiente",
            [Keys.Control | Keys.Home] = "inicio del documento",
            [Keys.Control | Keys.End] = "fin del documento",
            [Keys.Control | Keys.Shift | Keys.Home] = "seleccionar hasta el inicio",
            [Keys.Control | Keys.Shift | Keys.End] = "seleccionar hasta el fin",
            [Keys.Control | Keys.Prior] = "primera línea visible",
            [Keys.Control | Keys.Next] = "última línea visible",
            [Keys.Control | Keys.Return] = "abrir línea arriba",
            [Keys.Control | Keys.Shift | Keys.Return] = "abrir línea abajo",
            [Keys.Control | Keys.Back] = "borrar la palabra anterior",
            [Keys.Control | Keys.Delete] = "borrar la palabra siguiente",
            [Keys.Control | Keys.Space] = "completar palabra",
            [Keys.Control | Keys.Shift | Keys.Space] = "información de parámetros",
            [Keys.Control | Keys.Insert] = "copiar",
            [Keys.Control | Keys.Add] = "expandir selección",
            [Keys.Control | Keys.Subtract] = "contraer selección",
            [Keys.Control | Keys.OemCloseBrackets] = "ir a la llave correspondiente",
            [Keys.Control | Keys.Shift | Keys.OemCloseBrackets] = "seleccionar hasta la llave correspondiente",
            [Keys.Control | Keys.Shift | Keys.W] = "seleccionar palabra",
        };

        /// <summary>Atajos que ya usa GeneXus en sus menús (relevados de los manifiestos de sus packages).</summary>
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
            .Select(text => { TryParse(text, out Keys k); return k; }));

        /// <summary>
        /// Los atajos de GxQuickVar, leídos de su configuración (si no la cambiaste, Alt+V y Alt+Shift+V).
        /// </summary>
        static string GxQuickVarCommandFor(Keys keys)
        {
            Keys declare = Keys.Alt | Keys.V;
            Keys declareAll = Keys.Alt | Keys.Shift | Keys.V;
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GxQuickVar", "settings.txt");
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0)
                            continue;
                        string name = line.Substring(0, eq).Trim();
                        string value = line.Substring(eq + 1).Trim();
                        if (name == "DeclareShortcut" && TryParse(value, out Keys d)) declare = d;
                        else if (name == "DeclareAllShortcut" && TryParse(value, out Keys a)) declareAll = a;
                    }
                }
            }
            catch (Exception)
            {
                // Si no se puede leer, se comparan los valores por defecto
            }
            if (keys == declare)
                return "declarar variable";
            if (keys == declareAll)
                return "declarar todas las variables";
            return null;
        }
    }
}
