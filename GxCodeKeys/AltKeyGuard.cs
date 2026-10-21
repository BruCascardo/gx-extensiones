using System;
using System.Runtime.InteropServices;

namespace GxCodeKeys
{
    /// <summary>
    /// Evita que, después de un Alt+Click o de un atajo con Alt, al soltar Alt se active la barra de
    /// menús de GeneXus (y las letras siguientes abran menús: la L abría Layout, la V abría View).
    ///
    /// La barra (Infragistics UltraToolbarsManager) marca "Alt pendiente" en cada WM_SYSKEYDOWN de
    /// Alt, también en las repeticiones automáticas mientras se lo mantiene apretado, y lo desmarca con
    /// cualquier otra tecla. Al soltar Alt, si quedó marcado, entra en modo menú. Un click no lo
    /// desmarca, y la flecha de nuestros atajos no le llega.
    ///
    /// Durante un gesto con Alt:
    /// - se manda una tecla sin asignar (0xE8), que desmarca el Alt pendiente;
    /// - se descartan las repeticiones automáticas de Alt, que lo volverían a marcar. Se hace con un
    ///   hook de teclado del hilo, que corre antes que cualquier filtro de mensajes (el orden entre
    ///   filtros depende de quién se registró primero).
    /// El Alt soltado no se toca: la barra tiene que verlo para no quedar con Alt "pegado".
    /// </summary>
    sealed class AltKeyGuard : IDisposable
    {
        const int WH_KEYBOARD = 2;
        const int HC_ACTION = 0;
        const int VK_MENU = 0x12;
        // Tecla virtual sin asignar: no escribe nada ni dispara ningún comando (la "menu mask key" de AutoHotkey)
        const ushort VK_MASK = 0xE8;
        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint Type;
            public InputUnion Data;
        }

        // La unión tiene que medir lo que MOUSEINPUT (la más grande) para que SendInput acepte el tamaño
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT Mouse;
            [FieldOffset(0)] public KEYBDINPUT Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int Dx, Dy;
            public uint MouseData, Flags, Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort Vk, Scan;
            public uint Flags, Time;
            public IntPtr ExtraInfo;
        }

        // El delegado tiene que vivir lo que el hook: si lo junta el GC, el hook llama a basura
        readonly HookProc m_Proc;
        IntPtr m_Hook;
        // Hay un gesto con Alt en curso: se descartan las repeticiones de Alt hasta que se suelte
        bool m_Active;

        public AltKeyGuard()
        {
            m_Proc = Proc;
            try
            {
                m_Hook = SetWindowsHookEx(WH_KEYBOARD, m_Proc, IntPtr.Zero, GetCurrentThreadId());
                if (m_Hook == IntPtr.Zero)
                    Log.Error("Alt", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            }
            catch (Exception ex)
            {
                Log.Error("Alt", ex);
            }
        }

        /// <summary>Se usó Alt para algo nuestro (con Alt todavía apretado): que soltarlo no abra el menú.</summary>
        public void BeginGesture()
        {
            m_Active = true;
            try
            {
                var inputs = new[]
                {
                    new INPUT { Type = INPUT_KEYBOARD, Data = new InputUnion { Keyboard = new KEYBDINPUT { Vk = VK_MASK } } },
                    new INPUT { Type = INPUT_KEYBOARD, Data = new InputUnion { Keyboard = new KEYBDINPUT { Vk = VK_MASK, Flags = KEYEVENTF_KEYUP } } },
                };
                if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) != inputs.Length)
                    Log.Error("Alt", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            }
            catch (Exception ex)
            {
                Log.Error("Alt", ex);
            }
        }

        IntPtr Proc(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code == HC_ACTION && (int)(long)wParam == VK_MENU)
                {
                    long flags = lParam.ToInt64() & 0xFFFFFFFFL;
                    bool released = (flags & 0x80000000L) != 0;
                    bool repeat = (flags & 0x40000000L) != 0;
                    if (released || !repeat)
                        m_Active = false;      // Alt soltado, o un Alt nuevo: termina el gesto
                    else if (m_Active)
                        return (IntPtr)1;      // repetición automática durante el gesto: se descarta
                }
            }
            catch (Exception)
            {
                // Nunca cortar la cadena de hooks por un error nuestro
            }
            return CallNextHookEx(m_Hook, code, wParam, lParam);
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
