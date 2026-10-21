using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GxCodeKeys
{
    /// <summary>
    /// Hook de teclado del hilo de la interfaz, para las teclas que GeneXus atiende antes que
    /// cualquier IMessageFilter (como Supr o Ctrl+V, que van a sus comandos Delete y Paste y actúan solo
    /// en el cursor real).
    /// Ve la tecla antes que cualquier filtro, menú o control; si <c>onKeyDown</c> la toma, la descarta
    /// (GeneXus no se entera) y también descarta cuando se suelta.
    /// </summary>
    sealed class KeyboardHook : IDisposable
    {
        const int WH_KEYBOARD = 2;
        const long KeyUpFlag = 0x80000000L;     // bit 31 de lParam: se está soltando

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        readonly Func<Keys, bool> m_OnKeyDown;
        // El delegado tiene que vivir lo que el hook: si lo junta el GC, el hook llama a basura
        readonly HookProc m_Proc;
        IntPtr m_Hook;
        // Tecla tomada que se descarta hasta que se suelte
        Keys m_SwallowKey;

        /// <param name="onKeyDown">Recibe la tecla apretada (también las repeticiones automáticas);
        /// devuelve true si la tomó. Corre adentro del hook: tiene que diferir el trabajo.</param>
        public KeyboardHook(Func<Keys, bool> onKeyDown)
        {
            m_OnKeyDown = onKeyDown;
            m_Proc = Proc;
            try
            {
                m_Hook = SetWindowsHookEx(WH_KEYBOARD, m_Proc, IntPtr.Zero, GetCurrentThreadId());
                if (m_Hook == IntPtr.Zero)
                    Log.Error("teclado", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            }
            catch (Exception ex)
            {
                Log.Error("teclado", ex);
            }
        }

        IntPtr Proc(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                // Con cualquier código (HC_ACTION o HC_NOREMOVE): el bucle de WinForms primero espía la
                // cola con PeekMessage(PM_NOREMOVE) y Windows llama al hook una sola vez por tecla, en
                // ese momento. Descartarla ahí (devolver 1) la saca de la cola.
                if (code >= 0 && Handle((Keys)((int)(long)wParam & 0xFFFF), lParam.ToInt64() & 0xFFFFFFFFL))
                    return (IntPtr)1;
            }
            catch (Exception ex)
            {
                Log.Error("teclado", ex);   // nunca cortar la cadena de hooks por un error nuestro
            }
            return CallNextHookEx(m_Hook, code, wParam, lParam);
        }

        bool Handle(Keys key, long flags)
        {
            if ((flags & KeyUpFlag) != 0)
            {
                if (m_SwallowKey == Keys.None || key != m_SwallowKey)
                    return false;
                m_SwallowKey = Keys.None;
                return true;
            }
            if (!m_OnKeyDown(key))
                return false;
            m_SwallowKey = key;
            return true;
        }

        public void Dispose()
        {
            if (m_Hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(m_Hook);
                m_Hook = IntPtr.Zero;
            }
        }
    }
}
