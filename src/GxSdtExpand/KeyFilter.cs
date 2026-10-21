using System.Windows.Forms;

namespace GxSdtExpand
{
    /// <summary>
    /// Atiende el atajo configurable dentro de los editores de código. El atajo no va en el
    /// manifiesto porque ahí es fijo (y el enum Shortcut de WinForms no admite Alt+letra).
    /// </summary>
    sealed class KeyFilter : IMessageFilter
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;
        const int WM_CHAR = 0x0102;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSKEYUP = 0x0105;
        const int WM_SYSCHAR = 0x0106;

        // Después de consumir el atajo hay que tragarse el resto de la secuencia: el WM_CHAR/WM_SYSCHAR
        // (si no, se escribe un carácter o suena el beep), el key-up de la tecla, y el de Alt si el
        // atajo lo usaba (si no, al soltar Alt se activa la barra de menús).
        Keys m_SwallowKey;
        bool m_SwallowAlt;

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
            if (Shortcuts.IsModifierKey(key))
                return false;
            Keys combo = key | Control.ModifierKeys;
            bool full;
            if (Settings.ExpandKey != Keys.None && combo == Settings.ExpandKey)
                full = false;
            else if (Settings.ExpandAllKey != Keys.None && combo == Settings.ExpandAllKey)
                full = true;
            else
                return false;

            var editor = EditorLocator.FromFocus();
            if (editor == null || editor.ReadOnly)
                return false;

            m_SwallowKey = key;
            m_SwallowAlt = (combo & Keys.Alt) != 0;
            editor.BeginInvoke((System.Action)(() => SdtExpander.Run(null, full)));
            return true;
        }

        static Keys KeyOf(Message m) => (Keys)((int)(long)m.WParam & 0xFFFF);
    }
}
