using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;

namespace GxTabScroll
{
    /// <summary>
    /// Ctrl+W con el foco dentro de una pestaña de objeto la cierra, como en Chrome o VS Code. Ejecuta el
    /// comando Close de GeneXus (el de Ctrl+F4), así que pregunta si guardar igual que siempre. Con el foco
    /// en una ventana acoplada (KB Explorer, Properties...) o en un diálogo no hace nada.
    /// </summary>
    sealed class KeyFilter : IMessageFilter
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_CHAR = 0x0102;
        const string CorePackageGuid = "98121D96-A7D8-468b-9310-B1F468F812AE";

        static readonly CommandKey CloseCommand = new CommandKey(new Guid(CorePackageGuid), "Close");

        // Después de consumir el atajo hay que tragarse su WM_CHAR (Ctrl+W = 0x17): si no, llega a la
        // ventana que queda con el foco
        bool m_SwallowChar;

        public bool PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_KEYDOWN:
                    return OnKeyDown((Keys)((int)(long)m.WParam & 0xFFFF));

                case WM_CHAR:
                    if (!m_SwallowChar)
                        return false;
                    m_SwallowChar = false;
                    return true;
            }
            return false;
        }

        bool OnKeyDown(Keys key)
        {
            m_SwallowChar = false;
            if (key != Keys.W || Control.ModifierKeys != Keys.Control || !Settings.CloseWithCtrlW)
                return false;

            Form tab = FocusedTab();
            if (tab == null)
                return false;

            m_SwallowChar = true;
            // Se cierra después de atender el mensaje, no adentro del filtro
            tab.BeginInvoke((Action)(() =>
            {
                try
                {
                    UIServices.CommandDispatcher.Dispatch(CloseCommand);
                }
                catch (Exception ex)
                {
                    Log.Write("Error al cerrar la pestaña: " + ex);
                }
            }));
            return true;
        }

        /// <summary>Formulario de la pestaña (hijo MDI) que contiene el foco, o null.</summary>
        static Form FocusedTab()
        {
            // Un editor puede ser a su vez un Form embebido: se sube hasta el hijo MDI. Los diálogos y las
            // ventanas acopladas no tienen uno arriba.
            for (Control control = Control.FromChildHandle(GetFocus()); control != null; control = control.Parent)
            {
                if (control is Form form && form.IsMdiChild)
                    return form;
            }
            return null;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetFocus();
    }
}
