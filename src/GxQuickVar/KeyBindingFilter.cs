using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace GxQuickVar
{
    /// <summary>
    /// Atajos configurables. No se declaran en el manifiesto porque ahí son fijos (y el enum Shortcut
    /// de WinForms no admite Alt+letra): se interceptan los mensajes de teclado antes de que lleguen
    /// al control o al menú, solo si el atajo tiene algo que hacer donde está el foco (un editor de
    /// código editable, la grilla de variables...); si no, la tecla sigue su camino.
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
            /// <summary>Qué hacer con el foco en ese control, o null si ahí el atajo no aplica.</summary>
            public Func<Control, Action> Resolve;
        }

        readonly List<Binding> m_Bindings = new List<Binding>();

        // Después de consumir un atajo hay que tragarse el resto de la secuencia: el WM_CHAR/WM_SYSCHAR
        // (si no, se escribe un carácter o suena el beep), el key-up de la tecla, y el de Alt si el
        // atajo lo usaba (si no, al soltar Alt se activa la barra de menús).
        Keys m_SwallowKey;
        bool m_SwallowAlt;

        public void Add(Func<Keys> keys, Func<Control, Action> resolve)
        {
            m_Bindings.Add(new Binding { Keys = keys, Resolve = resolve });
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

                Control focused = EditorLocator.FocusedControl();
                Action action = focused != null ? Resolve(binding, focused) : null;
                if (action == null)
                    continue;

                m_SwallowKey = key;
                m_SwallowAlt = (combo & Keys.Alt) != 0;
                // Diferido: que termine el procesamiento de la tecla antes de abrir el popup. En la ventana
                // principal, que sigue viva aunque el control con el foco se cierre en el medio.
                (focused.TopLevelControl ?? focused).BeginInvoke(action);
                return true;
            }
            return false;
        }

        /// <summary>Un error al decidir no puede romper el teclado de GeneXus: la tecla sigue su camino.</summary>
        static Action Resolve(Binding binding, Control focused)
        {
            try
            {
                return binding.Resolve(focused);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static Keys KeyOf(Message m) => (Keys)((int)(long)m.WParam & 0xFFFF);
    }
}
