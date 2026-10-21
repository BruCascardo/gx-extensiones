using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GxQuickOpen
{
    /// <summary>
    /// El atajo de la búsqueda (Ctrl+O por defecto, el mismo del diálogo de GeneXus).
    ///
    /// No alcanza con un IMessageFilter: la barra de menús de GeneXus (Infragistics) procesa sus
    /// atajos antes, y Ctrl+O nunca llegaba. Un hook de teclado del hilo de la interfaz ve la tecla
    /// antes que cualquier filtro, menú o control; si es el atajo configurado y corresponde, la
    /// descarta (GeneXus no se entera) y abre la búsqueda. Si el atajo configurado es otro, Ctrl+O
    /// pasa de largo y GeneXus abre su diálogo de siempre.
    /// </summary>
    sealed class KeyboardHook : IDisposable
    {
        const int WH_KEYBOARD = 2;
                const long KeyUpFlag = 0x80000000L;     // bit 31 de lParam: se está soltando
        const long RepeatFlag = 0x40000000L;    // bit 30: ya estaba apretada (auto-repetición)

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        readonly Func<Keys> m_Keys;
        /// <summary>null si el atajo tiene que abrir la búsqueda; si no, por qué no (para el log).</summary>
        readonly Func<string> m_WhyNot;
        readonly Action m_Action;
        // Hay que guardar la referencia: si el GC junta el delegado, el hook llama a basura
        readonly HookProc m_Proc;
        IntPtr m_Hook;

        // Tecla del atajo que se está tragando hasta que se suelte (y Alt si el atajo lo usaba:
        // si no, al soltarlo se activa la barra de menús)
        Keys m_SwallowKey;
        bool m_SwallowAlt;
        int m_TriggeredAt;

        public KeyboardHook(Func<Keys> keys, Func<string> whyNot, Action action)
        {
            m_Keys = keys;
            m_WhyNot = whyNot;
            m_Action = action;
            m_Proc = Proc;
            m_Hook = SetWindowsHookEx(WH_KEYBOARD, m_Proc, IntPtr.Zero, GetCurrentThreadId());
            if (m_Hook == IntPtr.Zero)
                throw new InvalidOperationException("SetWindowsHookEx falló (error " + Marshal.GetLastWin32Error() + ")");
            Diag.Log("hook instalado en el hilo " + GetCurrentThreadId());
        }

        public void Dispose()
        {
            if (m_Hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(m_Hook);
                m_Hook = IntPtr.Zero;
            }
        }

        IntPtr Proc(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                // Con cualquier código (HC_ACTION o HC_NOREMOVE): el bucle de WinForms primero espía la
                // cola con PeekMessage(PM_NOREMOVE) y Windows llama al hook una sola vez por tecla, en
                // ese momento. Esperar al HC_ACTION hace que nunca se vea el atajo; y descartar la tecla
                // ahí (devolver 1) la saca de la cola, así que GeneXus no abre su diálogo.
                if (code >= 0 && Handle(code, (Keys)((int)(long)wParam & 0xFFFF), lParam.ToInt64() & 0xFFFFFFFFL))
                    return (IntPtr)1;   // descartada: no llega a GeneXus
            }
            catch (Exception ex)
            {
                Diag.Log("hook: " + ex);   // nunca romper el teclado del IDE
            }
            return CallNextHookEx(m_Hook, code, wParam, lParam);
        }

        /// <summary>Si hay que descartar la tecla (y, si es el atajo, abrir la búsqueda).</summary>
        bool Handle(int code, Keys key, long flags)
        {
            bool up = (flags & KeyUpFlag) != 0;
            if (up)
            {
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
                return false;
            }

            if (Shortcuts.IsModifierKey(key))
                return false;
            // La misma pulsación vista otra vez (auto-repetición, o el HC_ACTION después de un
            // HC_NOREMOVE si Windows lo llegara a mandar): tragarla sin volver a abrir
            if (m_SwallowKey != Keys.None && key == m_SwallowKey
                && ((flags & RepeatFlag) != 0 || unchecked(Environment.TickCount - m_TriggeredAt) < 1000))
                return true;

            Keys bound = m_Keys();
            Keys combo = key | Control.ModifierKeys;
            if (bound == Keys.None || bound != combo)
                return false;
            string why = m_WhyNot();
            if (why != null)
            {
                Diag.Log($"atajo {Shortcuts.Format(combo)} (código {code}): sigue a GeneXus porque {why}");
                return false;
            }

            Diag.Log($"atajo {Shortcuts.Format(combo)} (código {code}): abre la búsqueda");
            m_SwallowKey = key;
            m_SwallowAlt = (bound & Keys.Alt) != 0;
            m_TriggeredAt = Environment.TickCount;
            m_Action();   // tiene que diferir el trabajo: no abrir una ventana modal adentro del hook
            return true;
        }
    }
}
