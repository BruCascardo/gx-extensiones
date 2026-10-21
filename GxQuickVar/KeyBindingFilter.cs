using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace GxQuickVar
{
    /// <summary>
    /// Atajos configurables dentro de los editores de código. No se declaran en el manifiesto
    /// porque ahí son fijos (y el enum Shortcut de WinForms no admite Alt+letra): se interceptan
    /// los mensajes de teclado antes de que lleguen al editor o al menú, solo si el foco está en
    /// un editor de código editable.
    /// </summary>
    sealed class KeyBindingFilter : IMessageFilter
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;
        const int WM_CHAR = 0x0102;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSKEYUP = 0x0105;
        const int WM_SYSCHAR = 0x0106;

        sealed class Binding
        {
            public Func<Keys> Keys;
            public Action Action;
        }

        readonly List<Binding> m_Bindings = new List<Binding>();

        // Después de consumir un atajo hay que tragarse el resto de la secuencia: el WM_CHAR/WM_SYSCHAR
        // (si no, se escribe un carácter o suena el beep), el key-up de la tecla, y el de Alt si el
        // atajo lo usaba (si no, al soltar Alt se activa la barra de menús).
        Keys m_SwallowKey;
        bool m_SwallowAlt;

        public void Add(Func<Keys> keys, Action action)
        {
            m_Bindings.Add(new Binding { Keys = keys, Action = action });
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
            if (Shortcuts.IsModifierKey(key))
                return false;
            Keys combo = key | Control.ModifierKeys;

            foreach (Binding binding in m_Bindings)
            {
                Keys bound = binding.Keys();
                if (bound == Keys.None || bound != combo)
                    continue;

                var editor = EditorLocator.FromFocus();
                if (editor == null || editor.ReadOnly)
                    return false;

                m_SwallowKey = key;
                m_SwallowAlt = (combo & Keys.Alt) != 0;
                // Diferido: que termine el procesamiento de la tecla antes de abrir el popup
                editor.BeginInvoke(binding.Action);
                return true;
            }
            return false;
        }

        static Keys KeyOf(Message m) => (Keys)((int)(long)m.WParam & 0xFFFF);
    }
}
