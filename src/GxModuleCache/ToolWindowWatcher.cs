using System;
using System.Windows.Forms;
using Artech.Architecture.UI.Framework.Controls;
using Artech.Architecture.UI.Framework.Services;

namespace GxModuleCache
{
    /// <summary>
    /// Detecta la pantalla Manage Module References: GeneXus no avisa cuando abre un tool window, así que en cada
    /// <see cref="Application.Idle"/> se busca por su Guid (una consulta a un diccionario) y, si es una instancia
    /// nueva, se le agrega el botón de <see cref="ModuleCacheButton"/>.
    /// </summary>
    sealed class ToolWindowWatcher : IDisposable
    {
        // Guid de ManageModuleReferencesToolWindow (Artech.Packages.GenexusUI, clase internal)
        static readonly Guid ManageModuleReferencesId = new Guid("8E0BA1B6-1CB7-4E2C-A175-5C4A47AA3B71");

        Control m_Attached;

        public ToolWindowWatcher()
        {
            Application.Idle += OnIdle;
        }

        void OnIdle(object sender, EventArgs e)
        {
            if (!Settings.Enabled || !UIServices.IsToolWindowsAvailable)
                return;
            try
            {
                if (!UIServices.ToolWindows.TryGet(ManageModuleReferencesId, out IToolWindow window) ||
                    !(window is Control control) || control.IsDisposed || control == m_Attached)
                    return;
                m_Attached = control;
                ModuleCacheButton.Attach(control);
            }
            catch (Exception ex)
            {
                Log.Write("Error al detectar Manage Module References: " + ex);
            }
        }

        public void Dispose()
        {
            Application.Idle -= OnIdle;
        }
    }
}
