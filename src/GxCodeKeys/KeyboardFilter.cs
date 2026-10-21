using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ActiproSoftware.SyntaxEditor;

namespace GxCodeKeys
{
    /// <summary>
    /// Intercepta teclado y mouse antes de que lleguen al editor, solo si el destino es un editor
    /// de código de GeneXus (EditorLocator):
    /// - Los atajos configurables (mover/duplicar líneas, agregar cursores). No se declaran en el
    ///   manifiesto porque ahí son fijos y el enum Shortcut de WinForms no admite Ctrl+Alt+flecha.
    /// - Con cursores extra activos, las teclas de edición y los caracteres escritos van a MultiCursor.
    /// - Alt+Click agrega un cursor; Alt+arrastrar sigue haciendo la selección en bloque de siempre.
    /// - Supr, Ctrl+Supr, Ctrl+X, Ctrl+C y Ctrl+V con cursores extra se toman con un hook de teclado
    ///   (KeyboardHook): GeneXus los atiende antes que este filtro y sus comandos Delete, Cut, Copy y
    ///   Paste actúan solo en el cursor real.
    ///
    /// Cualquier error se informa en Output y la tecla sigue su curso normal: nunca rompe el editor.
    /// </summary>
    sealed class KeyboardFilter : IMessageFilter, IDisposable
    {
        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;
        const int WM_CHAR = 0x0102;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSKEYUP = 0x0105;
        const int WM_SYSCHAR = 0x0106;
        const int WM_MOUSEMOVE = 0x0200;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_LBUTTONUP = 0x0202;
        const int WM_LBUTTONDBLCLK = 0x0203;

        sealed class Binding
        {
            public Func<Keys> Keys;
            public Action<SyntaxEditor> Action;
            public string Name;
        }

        /// <summary>Un Alt+Click en curso: si el mouse se mueve es una selección en bloque, si no agrega un cursor.</summary>
        sealed class AltClick
        {
            public SyntaxEditor Editor;
            public Point Start;
            public EditPosition StartPosition;
            public int Offset;
            public bool Dragging;
            public bool Ignore;   // segundo click de un doble click
        }

        readonly List<Binding> m_Bindings = new List<Binding>();
        readonly AltKeyGuard m_AltGuard = new AltKeyGuard();
        readonly KeyboardHook m_Hook;
        // Editores a los que ya se les agregó el manejador de ViewMouseDown
        readonly HashSet<SyntaxEditor> m_HookedEditors = new HashSet<SyntaxEditor>();

        // Después de consumir una tecla hay que tragarse el resto de la secuencia: el WM_CHAR (si no,
        // se escribe un carácter o suena el beep) y el key-up de la tecla. El key-up de Alt no se
        // traga nunca: la barra de menús de GeneXus tiene que verlo (ver AltKeyGuard).
        Keys m_SwallowKey;
        AltClick m_AltClick;

        public void Add(string name, Func<Keys> keys, Action<SyntaxEditor> action)
        {
            m_Bindings.Add(new Binding { Name = name, Keys = keys, Action = action });
        }

        public KeyboardFilter()
        {
            m_Hook = new KeyboardHook(OnHookKeyDown);
        }

        public void Dispose()
        {
            m_Hook.Dispose();
            m_AltGuard.Dispose();
        }

        public bool PreFilterMessage(ref Message m)
        {
            try
            {
                switch (m.Msg)
                {
                    case WM_KEYDOWN:
                    case WM_SYSKEYDOWN:
                        return OnKeyDown(KeyOf(m));

                    case WM_CHAR:
                    case WM_SYSCHAR:
                        char c = (char)(long)m.WParam;
                        // Solo el carácter de control de la tecla consumida: si se escribe rápido, la
                        // letra siguiente puede llegar antes de soltar la tecla anterior y no se pierde
                        if (m_SwallowKey != Keys.None && (c < ' ' || m.Msg == WM_SYSCHAR))
                            return true;
                        return m.Msg == WM_CHAR && OnChar(c);

                    case WM_KEYUP:
                    case WM_SYSKEYUP:
                        return OnKeyUp(KeyOf(m));

                    case WM_LBUTTONDOWN:
                    case WM_LBUTTONDBLCLK:
                        return OnMouseDown(m.HWnd, PointOf(m), m.Msg == WM_LBUTTONDBLCLK);

                    case WM_MOUSEMOVE:
                        return m_AltClick != null && OnMouseMove(m.HWnd, PointOf(m));

                    case WM_LBUTTONUP:
                        return m_AltClick != null && OnMouseUp(m.HWnd);
                }
            }
            catch (Exception ex)
            {
                EndAltClick();
                Log.Error("teclado", ex);
            }
            return false;
        }

        // ---- Teclado ----

        bool OnKeyDown(Keys key)
        {
            if (Shortcuts.IsModifierKey(key))
                return false;
            Keys combo = key | Control.ModifierKeys;

            foreach (Binding binding in m_Bindings)
            {
                Keys bound = binding.Keys();
                if (bound == Keys.None || bound != combo)
                    continue;

                SyntaxEditor editor = EditorLocator.FromFocus();
                if (!EditorLocator.IsWritable(editor))
                    return false;   // fuera del editor de código (o de solo lectura) la tecla sigue su curso

                m_SwallowKey = key;
                if ((combo & Keys.Alt) != 0)
                    m_AltGuard.BeginGesture();   // la flecha no le llega a la barra de menús: para ella, Alt fue solo
                Run(binding.Name, binding.Action, editor);
                return true;
            }

            MultiCursor session = MultiCursor.For(EditorLocator.FromFocus());
            if (session != null && session.HandleKey(combo))
            {
                m_SwallowKey = key;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Desde el hook, con cursores extra: Supr, Ctrl+Supr, Cortar, Copiar y Pegar (Ctrl+X/C/V,
        /// Shift+Supr, Ctrl+Insert, Shift+Insert). Devuelve true si la tomó.
        /// </summary>
        bool OnHookKeyDown(Keys key)
        {
            // Descarte rápido: el hook ve todas las teclas del hilo (también cada letra que se escribe)
            if (key != Keys.Delete && key != Keys.Insert && key != Keys.C && key != Keys.X && key != Keys.V)
                return false;
            Keys combo = key | Control.ModifierKeys;
            if ((key == Keys.C || key == Keys.X || key == Keys.V) && combo != (key | Keys.Control))
                return false;
            foreach (Binding binding in m_Bindings)
            {
                if (binding.Keys() == combo)
                    return false;   // un atajo configurado con esta tecla: lo atiende OnKeyDown
            }
            SyntaxEditor editor = EditorLocator.FromFocus();
            MultiCursor session = MultiCursor.For(editor);
            if (session == null || !session.WantsHookKey(combo))
                return false;

            // Fuera del hook (corre adentro de PeekMessage). Lo encolado con BeginInvoke se procesa
            // antes que las teclas siguientes: los mensajes posteados van antes que los de entrada.
            editor.BeginInvoke(new Action(() =>
            {
                if (!editor.IsDisposed)
                    Run(combo.ToString(), e => MultiCursor.For(e)?.HandleKey(combo), editor);
            }));
            return true;
        }

        bool OnChar(char c)
        {
            MultiCursor session = MultiCursor.For(EditorLocator.FromFocus());
            return session != null && session.HandleChar(c);
        }

        bool OnKeyUp(Keys key)
        {
            if (m_SwallowKey != Keys.None && key == m_SwallowKey)
            {
                m_SwallowKey = Keys.None;
                return true;
            }
            return false;
        }

        // ---- Mouse ----

        bool OnMouseDown(IntPtr hwnd, Point point, bool doubleClick)
        {
            EndAltClick();
            SyntaxEditor editor = EditorLocator.FromHandle(hwnd);
            if (editor == null)
                return false;

            if (Control.ModifierKeys != Keys.Alt || !Settings.AltClickCursors || !EditorLocator.IsWritable(editor))
            {
                // Un click normal vuelve al modo de un solo cursor (y el editor lo procesa como siempre)
                MultiCursor.Clear(editor);
                return false;
            }

            EditorView view = editor.SelectedView;
            SyntaxEditorHitTestResult hit = editor.HitTest(point);
            if (view == null || hit == null || hit.Target != SyntaxEditorHitTestTarget.TextArea || hit.View != view)
                return false;   // márgenes, barras de desplazamiento, otra vista dividida: lo de siempre

            if (!editor.Focused)
                editor.Focus();
            HookViewMouseDown(editor);
            m_AltClick = new AltClick
            {
                Editor = editor,
                Start = point,
                StartPosition = view.LocationToPosition(point, LocationToPositionAlgorithm.Block),
                // BestFit, como el editor al hacer click: en una línea vacía o a la derecha del texto
                // da el final de esa línea (el offset del hit-test es -1 cuando no hay un carácter debajo)
                Offset = view.LocationToOffset(point, LocationToPositionAlgorithm.BestFit),
                Ignore = doubleClick,
            };
            editor.Capture = true;
            m_AltGuard.BeginGesture();

            // El click sigue su curso: la barra de menús de GeneXus tiene que verlo para salir del modo
            // Alt. El editor no lo procesa porque OnViewMouseDown lo cancela. El doble click sí se traga:
            // GeneXus atiende el doble click sobre una variable antes de que se pueda cancelar.
            return doubleClick;
        }

        void HookViewMouseDown(SyntaxEditor editor)
        {
            if (!m_HookedEditors.Add(editor))
                return;
            editor.ViewMouseDown += (s, e) =>
            {
                if (m_AltClick != null && ReferenceEquals(m_AltClick.Editor, editor) && e.Button == MouseButtons.Left)
                    e.Cancel = true;   // ni mover el cursor ni empezar una selección: lo maneja Alt+Click
            };
            editor.Disposed += (s, e) => m_HookedEditors.Remove(editor);
        }

        bool OnMouseMove(IntPtr hwnd, Point point)
        {
            AltClick click = m_AltClick;
            if (hwnd != click.Editor.Handle)
                return false;
            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                EndAltClick();   // se soltó el botón fuera de la ventana
                return false;
            }

            if (!click.Dragging)
            {
                Size drag = SystemInformation.DragSize;
                if (Math.Abs(point.X - click.Start.X) <= drag.Width / 2 && Math.Abs(point.Y - click.Start.Y) <= drag.Height / 2)
                    return true;
                if (click.Ignore)
                    return true;
                click.Dragging = true;
                MultiCursor.Clear(click.Editor);
            }

            // Alt+arrastrar: la selección en bloque (columna) nativa del editor
            EditorView view = click.Editor.SelectedView;
            EditPosition current = view.LocationToPosition(point, LocationToPositionAlgorithm.Block);
            view.Selection.SelectRange(click.StartPosition, current, SelectionModes.Block);
            return true;
        }

        bool OnMouseUp(IntPtr hwnd)
        {
            AltClick click = m_AltClick;
            if (hwnd != click.Editor.Handle)
                return false;
            EndAltClick();
            if (!click.Dragging && !click.Ignore)
                Run("Alt+Click", editor => MultiCursor.Toggle(editor, click.Offset), click.Editor);
            // El editor también lo recibe: cierra el ciclo del click (con el mouse-down cancelado, al
            // soltar solo vuelve a su estado de reposo y no mueve el cursor ni la selección)
            return false;
        }

        void EndAltClick()
        {
            SyntaxEditor editor = m_AltClick?.Editor;
            m_AltClick = null;
            if (editor != null && !editor.IsDisposed)
                editor.Capture = false;
        }

        // ---- Utilidades ----

        static void Run(string name, Action<SyntaxEditor> action, SyntaxEditor editor)
        {
            try
            {
                action(editor);
            }
            catch (Exception ex)
            {
                // El documento queda como estaba o con la operación completa: cada una es una sola
                // modificación (o un grupo de deshacer), así que Ctrl+Z la revierte igual
                MultiCursor.Clear(editor);
                Log.Error(name, ex);
            }
        }

        static Keys KeyOf(Message m) => (Keys)((int)(long)m.WParam & 0xFFFF);

        static Point PointOf(Message m)
        {
            int lParam = (int)(long)m.LParam;
            return new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
        }
    }
}
