using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ActiproSoftware.SyntaxEditor;
using Artech.Genexus.UI.Common.Editor;

namespace GxCodeCheck
{
    /// <summary>
    /// Detecta los editores de código: GeneXus no avisa cuando abre uno, así que en cada
    /// <see cref="Application.Idle"/> se mira si el foco está en un <see cref="GxTextEditor"/> nuevo y se le
    /// crea su <see cref="EditorSession"/>. Cada sesión termina sola cuando se cierra su editor.
    /// </summary>
    sealed class EditorWatcher : IDisposable
    {
        readonly Dictionary<SyntaxEditor, EditorSession> m_Sessions = new Dictionary<SyntaxEditor, EditorSession>();
        IntPtr m_LastFocus;

        public EditorWatcher()
        {
            Application.Idle += OnIdle;
        }

        public IEnumerable<EditorSession> Sessions => m_Sessions.Values.ToList();

        /// <summary>La sesión del editor que contiene al control, o null.</summary>
        public EditorSession SessionFor(Control control) => m_Sessions.Values.FirstOrDefault(s => s.Contains(control));

        void OnIdle(object sender, EventArgs e)
        {
            IntPtr focus = GetFocus();
            if (focus == m_LastFocus || !Settings.Enabled)
                return;
            m_LastFocus = focus;
            try
            {
                GxTextEditor editor = FindEditor(Control.FromChildHandle(focus));
                if (editor == null)
                    return;
                SyntaxEditor syntax = FindSyntaxEditor(editor);
                if (syntax == null || m_Sessions.ContainsKey(syntax))
                    return;
                var session = new EditorSession(editor, syntax);
                session.Closed += (s, a) => m_Sessions.Remove(syntax);
                m_Sessions[syntax] = session;
            }
            catch (Exception ex)
            {
                Log.Write("Error al detectar el editor: " + ex);
            }
        }

        static GxTextEditor FindEditor(Control control)
        {
            for (Control c = control; c != null; c = c.Parent)
            {
                if (c is GxTextEditor editor)
                    return editor;
            }
            return null;
        }

        static SyntaxEditor FindSyntaxEditor(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is SyntaxEditor syntax)
                    return syntax;
                SyntaxEditor nested = FindSyntaxEditor(child);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        public void Dispose()
        {
            Application.Idle -= OnIdle;
            foreach (EditorSession session in Sessions)
                session.Dispose();
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetFocus();
    }
}
