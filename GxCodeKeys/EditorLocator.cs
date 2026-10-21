using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ActiproSoftware.SyntaxEditor;
using Artech.Common.Framework.Commands;
using Artech.Genexus.UI.Common.Editor;

namespace GxCodeKeys
{
    /// <summary>
    /// Encuentra el editor de código de GeneXus (Events, Source, Rules, Conditions...) sobre el que
    /// actuar. Solo cuenta un SyntaxEditor que esté dentro de un GxTextEditor: el resto de los
    /// controles (otros editores de texto, grillas, el form) quedan afuera.
    /// </summary>
    static class EditorLocator
    {
        [DllImport("user32.dll")]
        static extern IntPtr GetFocus();

        public static SyntaxEditor Find(CommandData data)
        {
            // Los editores de texto se pasan a sí mismos como contexto (ISelectionContainer)
            if (data?.Context is GxTextEditor fromContext)
                return FindSyntaxEditor(fromContext);
            return FromFocus();
        }

        public static SyntaxEditor FromFocus()
        {
            IntPtr handle = GetFocus();
            return handle == IntPtr.Zero ? null : FromControl(Control.FromChildHandle(handle));
        }

        /// <summary>El editor de código dueño de una ventana (para los mensajes de mouse).</summary>
        public static SyntaxEditor FromHandle(IntPtr handle)
        {
            return handle == IntPtr.Zero ? null : FromControl(Control.FromHandle(handle));
        }

        /// <summary>Si se puede modificar el texto (el editor de GeneXus y el documento no son de solo lectura).</summary>
        public static bool IsWritable(SyntaxEditor syntaxEditor)
        {
            if (syntaxEditor == null || syntaxEditor.IsDisposed || syntaxEditor.Document == null || syntaxEditor.Document.ReadOnly)
                return false;
            GxTextEditor editor = OwnerOf(syntaxEditor);
            return editor != null && !editor.ReadOnly;
        }

        static SyntaxEditor FromControl(Control control)
        {
            SyntaxEditor syntaxEditor = null;
            for (Control c = control; c != null; c = c.Parent)
            {
                if (syntaxEditor == null && c is SyntaxEditor se)
                    syntaxEditor = se;
                if (c is GxTextEditor editor)
                    return syntaxEditor ?? FindSyntaxEditor(editor);
            }
            return null;
        }

        static GxTextEditor OwnerOf(SyntaxEditor syntaxEditor)
        {
            for (Control c = syntaxEditor.Parent; c != null; c = c.Parent)
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
                if (child is SyntaxEditor se)
                    return se;
                var nested = FindSyntaxEditor(child);
                if (nested != null)
                    return nested;
            }
            return null;
        }
    }
}
