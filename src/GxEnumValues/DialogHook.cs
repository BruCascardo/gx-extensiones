using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Artech.Packages.Genexus.UI.TypeEditors;

namespace GxEnumValues
{
    /// <summary>
    /// Detecta cuando se abre el diálogo Enum Values: un hook WH_CBT del hilo de la interfaz avisa cada vez que
    /// se activa una ventana, y si es un <see cref="EnumValuesEditorControl"/> se le agrega
    /// <see cref="EnumValuesEnhancer"/>. GeneXus no tiene un evento para esto y el diálogo lo crea su UITypeEditor.
    /// </summary>
    sealed class DialogHook : IDisposable
    {
        const int WH_CBT = 5;
        const int HCBT_ACTIVATE = 5;

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        // El delegado tiene que vivir mientras esté el hook
        readonly HookProc m_Proc;
        IntPtr m_Hook;

        public DialogHook()
        {
            m_Proc = Callback;
            m_Hook = SetWindowsHookEx(WH_CBT, m_Proc, IntPtr.Zero, GetCurrentThreadId());
            if (m_Hook == IntPtr.Zero)
                throw new InvalidOperationException("SetWindowsHookEx falló (error " + Marshal.GetLastWin32Error() + ")");
        }

        IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code == HCBT_ACTIVATE && Settings.Enabled)
            {
                try
                {
                    // Se activa también al volver de un mensaje: Attach no lo agrega dos veces
                    if (Control.FromHandle(wParam) is EnumValuesEditorControl dialog && !dialog.IsDisposed)
                        dialog.BeginInvoke((Action)(() => EnumValuesEnhancer.Attach(dialog)));
                }
                catch (Exception ex)
                {
                    Log.Write("Error al detectar el diálogo Enum Values: " + ex);
                }
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

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();
    }
}
