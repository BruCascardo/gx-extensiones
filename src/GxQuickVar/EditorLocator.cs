using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Artech.Common.Framework.Commands;
using Artech.Genexus.UI.Common.Editor;

namespace GxQuickVar
{
    /// <summary>Encuentra el editor de código (Events, Source, Rules...) sobre el que actuar.</summary>
    static class EditorLocator
    {
        [DllImport("user32.dll")]
        static extern IntPtr GetFocus();

        public static GxTextEditor Find(CommandData data)
        {
            // Los editores de texto se pasan a sí mismos como contexto (ISelectionContainer)
            if (data?.Context is GxTextEditor fromContext)
                return fromContext;
            return FromFocus();
        }

        public static GxTextEditor FromFocus() => From(FocusedControl());

        /// <summary>El editor de código que contiene al control, o null.</summary>
        public static GxTextEditor From(Control control)
        {
            for (Control c = control; c != null; c = c.Parent)
            {
                if (c is GxTextEditor editor)
                    return editor;
            }
            return null;
        }

        /// <summary>El control de WinForms que tiene el foco (o el que contiene a la ventana que lo tiene), o null.</summary>
        public static Control FocusedControl()
        {
            IntPtr handle = GetFocus();
            return handle == IntPtr.Zero ? null : Control.FromChildHandle(handle);
        }

        public static ActiproSoftware.SyntaxEditor.SyntaxEditor FindSyntaxEditor(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is ActiproSoftware.SyntaxEditor.SyntaxEditor se)
                    return se;
                var nested = FindSyntaxEditor(child);
                if (nested != null)
                    return nested;
            }
            return null;
        }
    }
}
