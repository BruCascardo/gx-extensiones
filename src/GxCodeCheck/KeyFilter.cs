using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GxCodeCheck
{
    /// <summary>
    /// El atajo de las correcciones (Ctrl+. por defecto, como en VS Code): en un editor de código, con el
    /// cursor sobre algo marcado, abre su cartel con el foco para elegir la corrección con el teclado. Si
    /// ahí no hay nada marcado, la tecla sigue su camino.
    /// </summary>
    sealed class KeyFilter : IMessageFilter
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;
        const int WM_CHAR = 0x0102;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSKEYUP = 0x0105;
        const int WM_SYSCHAR = 0x0106;

        readonly Func<Control, EditorSession> m_SessionFor;
        // Después de consumir el atajo hay que tragarse el WM_CHAR (si no, se escribe el carácter), el
        // key-up de la tecla y el de Alt si el atajo lo usaba (si no, se activa la barra de menús)
        Keys m_SwallowKey;
        bool m_SwallowAlt;

        public KeyFilter(Func<Control, EditorSession> sessionFor)
        {
            m_SessionFor = sessionFor;
        }

        public bool PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_KEYDOWN:
                case WM_SYSKEYDOWN:
                    return OnKeyDown(KeyOf(m));
                case WM_CHAR:
                case WM_SYSCHAR:
                    return m_SwallowKey != Keys.None;
                case WM_KEYUP:
                case WM_SYSKEYUP:
                    Keys key = KeyOf(m);
                    if (m_SwallowKey != Keys.None && key == m_SwallowKey)
                    {
                        m_SwallowKey = Keys.None;
                        return true;
                    }
                    if (m_SwallowAlt && (key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu))
                    {
                        m_SwallowAlt = false;
                        return true;
                    }
                    break;
            }
            return false;
        }

        bool OnKeyDown(Keys key)
        {
            if (Shortcuts.IsModifierKey(key) || !Settings.Enabled)
                return false;
            Keys bound = Settings.FixKey;
            if (bound == Keys.None || (key | Control.ModifierKeys) != bound)
                return false;
            try
            {
                IntPtr focus = GetFocus();
                Control focused = focus == IntPtr.Zero ? null : Control.FromChildHandle(focus);
                EditorSession session = focused == null ? null : m_SessionFor(focused);
                int marked = session?.MarkedAtCaret() ?? 0;
                Log.Write($"Atajo de las correcciones en {focused?.GetType().Name ?? "(nada)"}: "
                          + (session == null ? "no es un editor revisado" : marked == 0 ? "no hay nada marcado en el cursor" : $"{marked} marcado(s), se abre el cartel"));
                if (marked == 0)
                    return false;
                // Después de atender la tecla: abierto en el medio, el editor recupera el foco y el cartel se cierra
                Control top = focused.TopLevelControl ?? focused;
                top.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        session.ShowAtCaret();
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Error al abrir las correcciones: " + ex);
                    }
                }));
            }
            catch (Exception ex)
            {
                // Un error acá no puede romper el teclado de GeneXus: la tecla sigue su camino
                Log.Write("Error en el atajo de las correcciones: " + ex.Message);
                return false;
            }
            m_SwallowKey = key;
            m_SwallowAlt = (bound & Keys.Alt) != 0;
            return true;
        }

        static Keys KeyOf(Message m) => (Keys)((int)(long)m.WParam & 0xFFFF);

        [DllImport("user32.dll")]
        static extern IntPtr GetFocus();
    }

    /// <summary>Texto ⇄ combinación de teclas, independiente del idioma de Windows ("Ctrl+.", "Alt+Enter").</summary>
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
            // "Ctrl+." : el punto es la tecla, no un separador vacío
            string[] parts = text.EndsWith("+.") ? (text.Substring(0, text.Length - 2) + "+OemPeriod").Split('+') : text.Split('+');
            foreach (string raw in parts)
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

        /// <summary>Un atajo usable dentro del editor: con Ctrl o Alt, o una tecla de función.</summary>
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
            if (key == Keys.OemPeriod)
                return ".";
            if (key == Keys.Return)
                return "Enter";
            return key.ToString();
        }
    }
}
