using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Artech.Core.UI.Dialogs;

namespace GxKbRefs
{
    /// <summary>
    /// Detecta las ventanas References: GeneXus abre una nueva por objeto (CallBrowserToolWindow, con un Guid
    /// distinto cada vez) sin avisar, y le da el foco. En cada <see cref="Application.Idle"/> se sube desde el
    /// control con el foco hasta encontrar su <see cref="CallBrowserControl"/> y, si es nuevo, se le agrega
    /// <see cref="ReferencesExtender"/>.
    /// </summary>
    sealed class ReferencesWatcher : IDisposable
    {
        readonly HashSet<CallBrowserControl> m_Attached = new HashSet<CallBrowserControl>();
        IntPtr m_LastFocus;

        public ReferencesWatcher()
        {
            Application.Idle += OnIdle;
        }

        void OnIdle(object sender, EventArgs e)
        {
            IntPtr focus = GetFocus();
            if (focus == m_LastFocus || !Settings.Enabled)
                return;
            m_LastFocus = focus;
            try
            {
                for (Control control = Control.FromChildHandle(focus); control != null; control = control.Parent)
                {
                    if (control is CallBrowserControl references)
                    {
                        if (m_Attached.Add(references))
                        {
                            references.Disposed += (s, a) => m_Attached.Remove(references);
                            ReferencesExtender.Attach(references);
                        }
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Error al detectar la ventana References: " + ex);
            }
        }

        public void Dispose()
        {
            Application.Idle -= OnIdle;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetFocus();
    }
}
