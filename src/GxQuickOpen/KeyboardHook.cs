using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    ///
    /// El hook no ve las teclas de los controles web (el Output es un Monaco en CefSharp): Chromium
    /// las recibe en su propio hilo y GeneXus le reenvía los atajos a la interfaz con un mensaje
    /// armado que pasa por Application.FilterMessage, sin entrar a la cola. Para esos también es un
    /// filtro de mensajes, puesto primero en la lista para que lo vea antes que el de los menús.
    /// </summary>
    sealed class KeyboardHook : IMessageFilter, IDisposable
    {
        const int WH_KEYBOARD = 2;
        const int WM_KEYDOWN = 0x100;
        const int WM_KEYUP = 0x101;
        const int WM_SYSKEYDOWN = 0x104;
        const int WM_SYSKEYUP = 0x105;
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
        bool m_Filtering;

        // Tecla del atajo que se está tragando hasta que se suelte (y Alt si el atajo lo usaba:
        // si no, al soltarlo se activa la barra de menús)
        Keys m_SwallowKey;
        bool m_SwallowAlt;
        // Hasta cuándo (TickCount) se traga otra vez la misma pulsación aunque no sea auto-repetición
        int m_SwallowUntil;

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
            AddFilterFirst();
        }

        public void Dispose()
        {
            if (m_Hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(m_Hook);
                m_Hook = IntPtr.Zero;
            }
            if (m_Filtering)
            {
                Application.RemoveMessageFilter(this);
                m_Filtering = false;
            }
        }

        /// <summary>
        /// Agrega el filtro adelante de los que ya están: WinForms no tiene cómo hacerlo, así que se
        /// sacan los anteriores (la lista es privada: se lee por reflection) y se vuelven a agregar
        /// después. Si no se puede leer la lista, queda al final y el Output sigue como antes.
        /// </summary>
        void AddFilterFirst()
        {
            var previous = new List<IMessageFilter>();
            try
            {
                Type contextType = typeof(Application).GetNestedType("ThreadContext", BindingFlags.NonPublic);
                object context = contextType?.GetMethod("FromCurrent", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
                if (contextType?.GetField("messageFilters", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(context) is IList filters)
                {
                    foreach (object filter in filters)
                    {
                        if (filter is IMessageFilter messageFilter)
                            previous.Add(messageFilter);
                    }
                }
                else
                {
                    Diag.Log("filtro: no se encontró la lista de filtros de WinForms, queda al final");
                }
            }
            catch (Exception ex)
            {
                Diag.Log("filtro: no se pudo leer la lista de filtros (" + ex.Message + "), queda al final");
                previous.Clear();
            }

            foreach (IMessageFilter filter in previous)
                Application.RemoveMessageFilter(filter);
            Application.AddMessageFilter(this);
            foreach (IMessageFilter filter in previous)
                Application.AddMessageFilter(filter);
            m_Filtering = true;
            Diag.Log("filtro instalado antes de otros " + previous.Count);
        }

        IntPtr Proc(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                // Con cualquier código (HC_ACTION o HC_NOREMOVE): el bucle de WinForms primero espía la
                // cola con PeekMessage(PM_NOREMOVE) y Windows llama al hook una sola vez por tecla, en
                // ese momento. Esperar al HC_ACTION hace que nunca se vea el atajo; y descartar la tecla
                // ahí (devolver 1) la saca de la cola, así que GeneXus no abre su diálogo.
                long flags = lParam.ToInt64() & 0xFFFFFFFFL;
                if (code >= 0 && Handle("código " + code, (Keys)((int)(long)wParam & 0xFFFF),
                                        (flags & KeyUpFlag) != 0, (flags & RepeatFlag) != 0, true))
                    return (IntPtr)1;   // descartada: no llega a GeneXus
            }
            catch (Exception ex)
            {
                Diag.Log("hook: " + ex);   // nunca romper el teclado del IDE
            }
            return CallNextHookEx(m_Hook, code, wParam, lParam);
        }

        public bool PreFilterMessage(ref Message m)
        {
            // Las teclas reales ya pasaron por el hook: si llegan acá es que siguen a GeneXus, y el
            // filtro decide lo mismo. Lo nuevo son las que reenvía Chromium (lParam trae el original).
            if (m.Msg != WM_KEYDOWN && m.Msg != WM_SYSKEYDOWN && m.Msg != WM_KEYUP && m.Msg != WM_SYSKEYUP)
                return false;
            try
            {
                bool up = m.Msg == WM_KEYUP || m.Msg == WM_SYSKEYUP;
                return Handle("filtro", (Keys)((int)(long)m.WParam & 0xFFFF), up,
                              ((m.LParam.ToInt64() & 0xFFFFFFFFL) & RepeatFlag) != 0, false);
            }
            catch (Exception ex)
            {
                Diag.Log("filtro: " + ex);   // nunca romper el teclado del IDE
                return false;
            }
        }

        /// <summary>
        /// Si hay que descartar la tecla (y, si es el atajo, abrir la búsqueda). fromHook: si viene
        /// de la cola, donde la misma pulsación se puede ver dos veces (HC_NOREMOVE y HC_ACTION).
        /// </summary>
        bool Handle(string source, Keys key, bool up, bool repeat, bool fromHook)
        {
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
            if (m_SwallowKey != Keys.None && key == m_SwallowKey)
            {
                if (repeat || unchecked(m_SwallowUntil - Environment.TickCount) > 0)
                    return true;
                // Una pulsación nueva: el soltar de la anterior se perdió (Chromium no lo reenvía si
                // ya no hay Ctrl), no hay nada más que tragar
                m_SwallowKey = Keys.None;
                m_SwallowAlt = false;
            }

            Keys bound = m_Keys();
            Keys combo = key | Control.ModifierKeys;
            if (bound == Keys.None || bound != combo)
                return false;
            string why = m_WhyNot();
            if (why != null)
            {
                Diag.Log($"atajo {Shortcuts.Format(combo)} ({source}): sigue a GeneXus porque {why}");
                return false;
            }

            Diag.Log($"atajo {Shortcuts.Format(combo)} ({source}): abre la búsqueda");
            m_SwallowKey = key;
            m_SwallowAlt = (bound & Keys.Alt) != 0;
            m_SwallowUntil = Environment.TickCount + (fromHook ? 1000 : 0);
            m_Action();   // tiene que diferir el trabajo: no abrir una ventana modal adentro del hook
            return true;
        }
    }
}
