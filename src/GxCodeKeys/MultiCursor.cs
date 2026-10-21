using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ActiproSoftware.SyntaxEditor;

namespace GxCodeKeys
{
    /// <summary>
    /// Múltiples cursores sobre un SyntaxEditor, que no los soporta de forma nativa.
    ///
    /// El cursor "principal" es el cursor real del editor (con su selección, si la hay). Los extra
    /// son posiciones con su propia selección (ancla y cursor) que se guardan acá, se dibujan encima
    /// del editor y se mantienen al día con cada cambio del texto. Mientras haya cursores extra,
    /// KeyboardFilter manda las teclas de edición y de movimiento a esta clase, que replica cada
    /// edición en todas las posiciones como una sola acción de deshacer.
    ///
    /// El autocompletado (la lista de variables, de miembros, Ctrl+Espacio) es del cursor principal,
    /// como en VS Code: mientras la lista está abierta sus teclas le llegan al editor como siempre y,
    /// al cerrarse, lo que cambió en el principal se replica en los demás (ver BeginMirror).
    ///
    /// Hay una sesión por editor y solo existe mientras haya cursores extra.
    /// </summary>
    sealed class MultiCursor : NativeWindow
    {
        const int WM_PAINT = 0x000F;
        const int NULLREGION = 1;
        // Más que esto no tiene sentido y cada tecla sería lenta en archivos grandes
        const int MaxCursors = 5000;
        // Si hay que repintar más cursores que esto, se repinta toda el área de texto de una vez
        const int MaxRectsToInvalidate = 64;

        [DllImport("user32.dll")]
        static extern int GetUpdateRgn(IntPtr hWnd, IntPtr hRgn, bool erase);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr handle);

        [DllImport("user32.dll")]
        static extern bool IsClipboardFormatAvailable(uint format);

        const uint CF_TEXT = 1;
        const uint CF_UNICODETEXT = 13;

        // Lo que hace el editor cuando un carácter tipeado activa un trigger del lenguaje (no es público)
        static readonly MethodInfo s_EditorTriggerActivated = typeof(SyntaxEditor).GetMethod(
            "OnTriggerActivated", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(TriggerEventArgs) }, null);
        static readonly MethodInfo s_LanguageTriggerActivated = typeof(SyntaxLanguage).GetMethod(
            "OnSyntaxEditorTriggerActivated", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(SyntaxEditor), typeof(TriggerEventArgs) }, null);

        // Para escribir en los cursores extra sin cerrar la lista de autocompletado (ver ApplyKeepingCompletion)
        static readonly FieldInfo[] s_EditorArrayLists = typeof(SyntaxEditor)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(ArrayList))
            .ToArray();
        static readonly FieldInfo s_ListStart = IntField(typeof(IntelliPromptMemberList), "l");
        static readonly FieldInfo s_ListLength = IntField(typeof(IntelliPromptMemberList), "h");

        static FieldInfo IntField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.FieldType == typeof(int) ? field : null;
        }

        static readonly Dictionary<SyntaxEditor, MultiCursor> s_Sessions = new Dictionary<SyntaxEditor, MultiCursor>();
        static IWordBreakFinder s_DefaultWordBreakFinder;

        readonly SyntaxEditor m_Editor;
        // Cursores extra, ordenados, sin superponerse y sin el principal
        List<Cursor> m_Extras = new List<Cursor>();
        // Columna visual con la que se agregan cursores arriba/abajo (como en VS Code, no se "achica" en líneas cortas)
        int m_VerticalColumn = -1;
        bool m_Applying;
        bool m_CaretsVisible = true;
        readonly Timer m_Blink = new Timer();
        // Primera mitad de un carácter fuera del plano básico (emoji): llega en dos WM_CHAR
        char m_PendingHighSurrogate;
        // Edición del autocompletado en el cursor principal, pendiente de replicar en los demás
        Mirror m_Mirror;

        MultiCursor(SyntaxEditor editor)
        {
            m_Editor = editor;
            int blink = SystemInformation.CaretBlinkTime;
            m_Blink.Interval = blink > 0 ? blink : 530;
            m_Blink.Enabled = blink > 0;
            m_Blink.Tick += (s, e) => Safe(() =>
            {
                // Sin foco no titilan (como el cursor principal, que desaparece)
                m_CaretsVisible = !m_CaretsVisible || !m_Editor.ContainsFocus;
                InvalidateCarets(m_Extras);
            });

            AssignHandle(editor.Handle);
            editor.DocumentTextChanging += OnDocumentTextChanging;
            editor.DocumentTextChanged += OnDocumentTextChanged;
            editor.IntelliPromptMemberListClosed += OnMemberListClosed;
            editor.ViewVerticalScroll += OnViewScroll;
            editor.ViewHorizontalScroll += OnViewScroll;
            editor.SelectedViewChanged += OnSelectedViewChanged;
            editor.HandleDestroyed += OnHandleDestroyed;
            s_Sessions[editor] = this;
        }

        /// <summary>La sesión del editor, si tiene cursores extra.</summary>
        public static MultiCursor For(SyntaxEditor editor)
        {
            return editor != null && s_Sessions.TryGetValue(editor, out MultiCursor session) ? session : null;
        }

        /// <summary>Saca los cursores extra del editor (vuelve al modo de un solo cursor).</summary>
        public static void Clear(SyntaxEditor editor) => For(editor)?.End();

        public static void ClearAll()
        {
            foreach (MultiCursor session in s_Sessions.Values.ToList())
                session.End();
        }

        /// <summary>Un cursor y su selección: Anchor es donde empezó y Caret donde está el cursor (iguales si no hay selección).</summary>
        struct Cursor
        {
            public int Anchor;
            public int Caret;

            public Cursor(int caret) : this(caret, caret) { }

            public Cursor(int anchor, int caret)
            {
                Anchor = anchor;
                Caret = caret;
            }

            public int Start => Math.Min(Anchor, Caret);
            public int End => Math.Max(Anchor, Caret);
            public bool HasSelection => Anchor != Caret;
        }

        /// <summary>El cursor real del editor, con su selección.</summary>
        static Cursor PrimaryOf(SyntaxEditor editor)
        {
            Document document = editor.Document;
            Selection selection = editor.SelectedView.Selection;
            return new Cursor(DocumentText.Clamp(document, selection.StartOffset), DocumentText.Clamp(document, selection.EndOffset));
        }

        // =====================================================================================
        // Agregar y quitar cursores
        // =====================================================================================

        /// <summary>Alt+Click: agrega un cursor en <paramref name="offset"/>, o lo quita si ya había uno ahí.</summary>
        public static void Toggle(SyntaxEditor editor, int offset)
        {
            offset = DocumentText.Clamp(editor.Document, offset);
            Cursor primary = PrimaryOf(editor);
            MultiCursor session = For(editor);

            if (session != null && session.m_Extras.Any(c => c.Caret == offset))
            {
                session.SetCursors(primary, session.m_Extras.Where(c => c.Caret != offset));
                return;
            }
            if (offset == primary.Caret)
                return;   // ya hay un cursor ahí: el principal

            session = session ?? new MultiCursor(editor);
            session.m_VerticalColumn = -1;
            session.SetCursors(primary, session.m_Extras.Concat(new[] { new Cursor(offset) }));
        }

        /// <summary>
        /// Ctrl+Alt+↑/↓: agrega un cursor en la línea de arriba del cursor más alto (o abajo del más
        /// bajo), en la misma columna. El nuevo pasa a ser el principal, así la vista lo sigue.
        /// </summary>
        public static void AddVertical(SyntaxEditor editor, bool up)
        {
            Document document = editor.Document;
            int tabSize = DocumentText.TabSize(document);
            Cursor primary = PrimaryOf(editor);
            MultiCursor session = For(editor);

            List<Cursor> all = session?.m_Extras.ToList() ?? new List<Cursor>();
            all.Add(primary);
            if (all.Count >= MaxCursors)
                return;

            int column = session?.m_VerticalColumn ?? -1;
            if (column < 0)
            {
                DocumentLine line = DocumentText.LineOf(document, primary.Caret);
                column = DocumentText.VisualColumn(DocumentText.LineText(document, line), primary.Caret - line.StartOffset, tabSize);
            }

            int edge = up ? all.Min(c => c.Caret) : all.Max(c => c.Caret);
            int lineIndex = DocumentText.LineIndexOf(document, edge) + (up ? -1 : 1);
            if (lineIndex < 0 || lineIndex >= document.Lines.Count)
                return;

            DocumentLine target = document.Lines[lineIndex];
            int offset = target.StartOffset + DocumentText.CharIndexAtVisualColumn(DocumentText.LineText(document, target), column, tabSize);

            session = session ?? new MultiCursor(editor);
            session.m_VerticalColumn = column;
            // El principal anterior queda como cursor extra, con su selección
            session.SetCursors(new Cursor(offset), all);
        }

        // =====================================================================================
        // Teclado (lo llama KeyboardFilter solo si este editor tiene el foco)
        // =====================================================================================

        /// <summary>Procesa una tecla. Devuelve true si la consumió.</summary>
        public bool HandleKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            Keys modifiers = keyData & Keys.Modifiers;

            if (CompletionVisible)
            {
                // La lista es del cursor principal: sus teclas le llegan como siempre y lo que confirme
                // se replica en los demás cursores cuando se cierre
                if (IsCompletionKey(key, modifiers))
                {
                    BeginMirror();
                    return false;
                }
                if (!IsTypingKey(key, modifiers))
                    CloseCompletion();   // ←, Fin, Ctrl+V...: se cierra la lista y la tecla va a todos los cursores
            }
            else
            {
                FlushMirror();   // un autocompletado que terminó y todavía no se replicó
                if (modifiers == Keys.Control && key == Keys.Space)
                {
                    // Ctrl+Espacio: la lista (o el completado directo) en el principal, replicado igual
                    BeginMirror();
                    return false;
                }
            }

            if (modifiers == Keys.None || (modifiers == Keys.Shift && (key == Keys.Back || key == Keys.Return)))
            {
                switch (key)
                {
                    case Keys.Escape: End(); return true;
                    case Keys.Back: Backspace(); return true;
                    case Keys.Delete: Delete(); return true;
                    case Keys.Return: NewLine(); return true;
                    case Keys.Tab: Tab(); return true;
                    case Keys.Left:
                    case Keys.Right:
                    case Keys.Up:
                    case Keys.Down:
                    case Keys.Home:
                    case Keys.End:
                        Move(key, extend: false, byWord: false);
                        return true;
                    case Keys.Prior:
                    case Keys.Next:
                    case Keys.Insert:
                        End();          // navegación de un solo cursor: sigue la tecla nativa
                        return false;
                }
                return false;           // letras, números, F1..F12: el carácter llega por WM_CHAR
            }

            // Shift+flechas, Shift+Inicio/Fin: cada cursor extiende su selección
            if (modifiers == Keys.Shift && IsMoveKey(key))
            {
                Move(key, extend: true, byWord: false);
                return true;
            }
            // Ctrl+←/→ por palabra; con Shift, seleccionando
            if ((modifiers == Keys.Control || modifiers == (Keys.Control | Keys.Shift)) && (key == Keys.Left || key == Keys.Right))
            {
                Move(key, extend: (modifiers & Keys.Shift) != 0, byWord: true);
                return true;
            }
            if (modifiers == Keys.Control && (key == Keys.Back || key == Keys.Delete))
            {
                DeleteWord(backward: key == Keys.Back);
                return true;
            }

            if ((modifiers == Keys.Control && key == Keys.V) || (modifiers == Keys.Shift && key == Keys.Insert))
            {
                Paste();
                return true;
            }
            if (modifiers == Keys.Control && (key == Keys.C || key == Keys.Insert))
                return Copy();          // sin selección en ningún cursor, el Ctrl+C del editor
            if ((modifiers == Keys.Control && key == Keys.X) || (modifiers == Keys.Shift && key == Keys.Delete))
                return Cut();

            if ((modifiers & Keys.Alt) != 0)
                return false;           // AltGr (Ctrl+Alt) escribe caracteres, y Alt+letra son atajos de otros packages

            if ((modifiers & Keys.Control) != 0 || IsNavigationKey(key))
                End();                  // Ctrl+Z, Ctrl+Inicio, Shift+RePág...: un solo cursor
            return false;
        }

        static bool IsMoveKey(Keys key) =>
            key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End;

        static bool IsNavigationKey(Keys key) =>
            IsMoveKey(key) || key == Keys.Prior || key == Keys.Next || key == Keys.Tab || key == Keys.Delete;

        /// <summary>Las teclas que maneja la lista de autocompletado abierta: navegar, confirmar, cerrar, borrar lo escrito.</summary>
        static bool IsCompletionKey(Keys key, Keys modifiers)
        {
            if (modifiers == Keys.Control)
                return key == Keys.Space;
            return modifiers == Keys.None && (key == Keys.Up || key == Keys.Down || key == Keys.Prior || key == Keys.Next
                || key == Keys.Return || key == Keys.Tab || key == Keys.Escape || key == Keys.Back);
        }

        /// <summary>Una tecla que escribe un carácter: el carácter llega después por WM_CHAR (HandleChar).</summary>
        static bool IsTypingKey(Keys key, Keys modifiers) =>
            (modifiers == Keys.None || modifiers == Keys.Shift) && !IsNavigationKey(key)
            && key != Keys.Return && key != Keys.Escape && key != Keys.Back && key != Keys.Insert;

        /// <summary>
        /// Teclas que GeneXus atiende antes que cualquier IMessageFilter y que con varios cursores tienen
        /// que venir acá: Supr y los comandos Cortar, Copiar y Pegar del menú Edit (que actúan solo en el
        /// cursor real). KeyboardFilter las toma con un hook si esto devuelve true y después llama a
        /// HandleKey. Corre adentro del hook: solo lee el estado, no cambia nada.
        /// </summary>
        public bool WantsHookKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Delete:
                case Keys.Control | Keys.Delete:
                    return true;
                case Keys.Control | Keys.C:
                case Keys.Control | Keys.Insert:
                case Keys.Control | Keys.X:
                case Keys.Shift | Keys.Delete:
                    // Sin selección en ningún cursor, lo de siempre del editor (Ctrl+X corta la línea)
                    return PrimaryOf(m_Editor).HasSelection || m_Extras.Any(c => c.HasSelection);
                case Keys.Control | Keys.V:
                case Keys.Shift | Keys.Insert:
                    // Sin texto (por ejemplo, variables copiadas del árbol), el Pegar de GeneXus
                    return IsClipboardFormatAvailable(CF_UNICODETEXT) || IsClipboardFormatAvailable(CF_TEXT);
            }
            return false;
        }

        /// <summary>Un carácter escrito (WM_CHAR). Devuelve true si lo consumió.</summary>
        public bool HandleChar(char c)
        {
            if (CompletionVisible)
            {
                // Filtra la lista (o la confirma, con un carácter que no es de un nombre) en el principal
                BeginMirror();
                return false;
            }
            FlushMirror();

            if (char.IsHighSurrogate(c))
            {
                m_PendingHighSurrogate = c;
                return true;
            }
            if (char.IsLowSurrogate(c))
            {
                if (m_PendingHighSurrogate != '\0')
                    Type(new string(new[] { m_PendingHighSurrogate, c }));
                m_PendingHighSurrogate = '\0';
                return true;
            }
            m_PendingHighSurrogate = '\0';
            if (c < ' ' || c == '\u007F')
                return true;            // caracteres de control de teclas ya procesadas (Backspace, Enter, Ctrl+letra)
            if ((Control.ModifierKeys & (Keys.Control | Keys.Alt)) == Keys.Control)
                return false;           // el espacio de Ctrl+Espacio: es del editor (AltGr sí escribe)
            Type(c.ToString());
            FireTriggers(c);
            return true;
        }

        /// <summary>
        /// Los triggers del lenguaje para el carácter recién escrito, como hace el editor al tipear con
        /// un solo cursor: así "&amp;" abre la lista de variables, "." la de miembros, etc. Se activan en el
        /// cursor principal y lo que se elija se replica en los demás.
        /// </summary>
        void FireTriggers(char c)
        {
            if (!s_Sessions.ContainsKey(m_Editor) || !m_Editor.KeyPressTriggersEnabled || s_EditorTriggerActivated == null)
                return;
            Document document = m_Editor.Document;
            int offset = PrimaryOf(m_Editor).Caret - 1;
            if (offset < 0 || CharAt(document, offset) != c)
                return;
            int tokenIndex = document.Tokens.IndexOf(offset);
            if (tokenIndex < 0)
                return;
            ILexicalState state = document.Tokens[tokenIndex].LexicalState;
            SyntaxLanguage language = m_Editor.SelectedView.GetCurrentLanguageForContext();

            BeginMirror();   // si el trigger además cambia el texto, también se replica
            foreach (Trigger trigger in language.Triggers)
            {
                if (trigger is KeyPressTrigger keyPress && keyPress.Character == c && keyPress.ValidLexicalStates.Contains(state)
                    && (trigger.AllowInReadOnlyMode || !document.IsTextRangeReadOnly(new TextRange(offset))))
                {
                    var args = new TriggerEventArgs(trigger);
                    s_LanguageTriggerActivated?.Invoke(language, new object[] { m_Editor, args });
                    s_EditorTriggerActivated.Invoke(m_Editor, new object[] { args });
                }
            }
        }

        // =====================================================================================
        // Ediciones
        // =====================================================================================

        /// <summary>Reemplazar [Offset, Offset+Length) por Text; el cursor queda en Offset+CaretInText.</summary>
        struct Edit
        {
            public int Offset;
            public int Length;
            public string Text;
            public int CaretInText;
            public bool IsPrimary;

            public static Edit Insert(Cursor cursor, string text) =>
                new Edit { Offset = cursor.Start, Length = cursor.End - cursor.Start, Text = text, CaretInText = text.Length };

            public static Edit Delete(int start, int end) => new Edit { Offset = start, Length = end - start, Text = string.Empty };

            /// <summary>Nada: el cursor queda donde está.</summary>
            public static Edit None(Cursor cursor) => Delete(cursor.Caret, cursor.Caret);
        }

        void Type(string text) => Apply(cursor => Edit.Insert(cursor, text));

        void Backspace()
        {
            Document document = m_Editor.Document;
            Apply(cursor =>
            {
                if (cursor.HasSelection)
                    return Edit.Delete(cursor.Start, cursor.End);
                int caret = cursor.Caret;
                if (caret == 0)
                    return Edit.Delete(0, 0);
                DocumentLine line = DocumentText.LineOf(document, caret);
                if (caret == line.StartOffset)   // al principio de la línea: se une con la anterior
                    return Edit.Delete(document.Lines[line.Index - 1].EndOffset, caret);
                int length = caret >= 2 && char.IsLowSurrogate(CharAt(document, caret - 1)) && char.IsHighSurrogate(CharAt(document, caret - 2)) ? 2 : 1;
                return Edit.Delete(caret - length, caret);
            });
        }

        void Delete()
        {
            Document document = m_Editor.Document;
            Apply(cursor =>
            {
                if (cursor.HasSelection)
                    return Edit.Delete(cursor.Start, cursor.End);
                int caret = cursor.Caret;
                if (caret >= document.Length)
                    return Edit.Delete(caret, caret);
                DocumentLine line = DocumentText.LineOf(document, caret);
                if (caret == line.EndOffset)     // al final de la línea: se une con la siguiente
                    return Edit.Delete(caret, document.Lines[line.Index + 1].StartOffset);
                int length = caret + 1 < document.Length && char.IsHighSurrogate(CharAt(document, caret)) && char.IsLowSurrogate(CharAt(document, caret + 1)) ? 2 : 1;
                return Edit.Delete(caret, caret + length);
            });
        }

        /// <summary>Ctrl+Retroceso / Ctrl+Supr: borra hasta el comienzo de la palabra anterior o siguiente, como el editor.</summary>
        void DeleteWord(bool backward)
        {
            Document document = m_Editor.Document;
            IWordBreakFinder words = WordBreakFinder(document);
            Apply(cursor =>
            {
                if (cursor.HasSelection)
                    return Edit.Delete(cursor.Start, cursor.End);
                int caret = cursor.Caret;
                int other = DocumentText.Clamp(document, backward ? words.FindPreviousWordStart(document, caret) : words.FindNextWordStart(document, caret));
                return Edit.Delete(Math.Min(caret, other), Math.Max(caret, other));
            });
        }

        void NewLine()
        {
            Document document = m_Editor.Document;
            Apply(cursor =>
            {
                // Misma indentación que la línea actual (sin pasar del cursor)
                DocumentLine line = DocumentText.LineOf(document, cursor.Start);
                string indent = DocumentText.LeadingWhitespace(DocumentText.Get(document, line.StartOffset, cursor.Start));
                return Edit.Insert(cursor, DocumentText.NewLine + indent);
            });
        }

        void Tab()
        {
            Document document = m_Editor.Document;
            int tabSize = DocumentText.TabSize(document);
            Apply(cursor =>
            {
                if (!document.AutoConvertTabsToSpaces)
                    return Edit.Insert(cursor, "\t");
                DocumentLine line = DocumentText.LineOf(document, cursor.Start);
                int column = DocumentText.VisualColumn(DocumentText.LineText(document, line), cursor.Start - line.StartOffset, tabSize);
                return Edit.Insert(cursor, new string(' ', tabSize - column % tabSize));
            });
        }

        void Paste()
        {
            string text;
            try
            {
                text = Clipboard.GetText();
            }
            catch (Exception)
            {
                return;   // portapapeles ocupado por otra aplicación
            }
            if (string.IsNullOrEmpty(text))
                return;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');

            // Como VS Code: si el portapapeles tiene tantas líneas como cursores, va una línea a cada uno
            string[] lines = text.TrimEnd('\n').Split('\n');
            int cursorCount = AllCursors().Count;
            if (lines.Length == cursorCount)
            {
                int index = 0;
                Apply(cursor => Edit.Insert(cursor, lines[index++]));
            }
            else
            {
                Apply(cursor => Edit.Insert(cursor, text));
            }
        }

        /// <summary>
        /// Ctrl+C: como VS Code, el texto seleccionado de todos los cursores, una selección por línea
        /// (así Ctrl+V con la misma cantidad de cursores pega cada una en su lugar).
        /// </summary>
        bool Copy()
        {
            string text = SelectedText();
            if (text == null)
                return false;
            SetClipboard(text);
            return true;
        }

        /// <summary>Ctrl+X: copia y borra las selecciones de todos los cursores.</summary>
        bool Cut()
        {
            string text = SelectedText();
            if (text == null)
            {
                End();   // sin selección: el Ctrl+X del editor (corta la línea), con un solo cursor
                return false;
            }
            if (SetClipboard(text))
                Apply(cursor => Edit.Delete(cursor.Start, cursor.End));
            return true;
        }

        /// <summary>Las selecciones de todos los cursores en orden, separadas por saltos de línea; null si ninguno tiene selección.</summary>
        string SelectedText()
        {
            Document document = m_Editor.Document;
            List<string> parts = AllCursors()
                .Where(c => c.HasSelection)
                .Select(c => DocumentText.Get(document, c.Start, c.End).Replace("\n", "\r\n"))
                .ToList();
            return parts.Count == 0 ? null : string.Join("\r\n", parts);
        }

        static bool SetClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception)
            {
                return false;   // portapapeles ocupado por otra aplicación
            }
        }

        /// <summary>
        /// Aplica una edición por cursor (en orden de offset, que es como Apply recorre los cursores)
        /// como una sola acción de deshacer, y reubica todos los cursores.
        /// </summary>
        void Apply(Func<Cursor, Edit> editFor) => Apply((cursor, isPrimary) => editFor(cursor));

        void Apply(Func<Cursor, bool, Edit> editFor)
        {
            if (m_Mirror != null)
                CloseCompletion();   // primero se replica lo que se completó en el principal

            Document document = m_Editor.Document;
            List<(Cursor cursor, bool isPrimary)> cursors = Normalize(document, PrimaryOf(m_Editor), m_Extras);

            var edits = new List<Edit>(cursors.Count);
            foreach (var (cursor, isPrimary) in cursors)
            {
                Edit edit = editFor(cursor, isPrimary);
                edit.IsPrimary = isPrimary;
                edits.Add(edit);
            }
            edits.Sort((a, b) => a.Offset.CompareTo(b.Offset));

            // Dos cursores pegados (Backspace en offsets 5 y 6): los rangos no se pueden pisar
            int previousEnd = 0;
            for (int i = 0; i < edits.Count; i++)
            {
                Edit edit = edits[i];
                if (edit.Offset < previousEnd)
                {
                    int end = Math.Max(edit.Offset + edit.Length, previousEnd);
                    edit.Offset = previousEnd;
                    edit.Length = end - previousEnd;
                    edits[i] = edit;
                }
                previousEnd = Math.Max(previousEnd, edit.Offset + edit.Length);
            }

            bool ok = true;
            m_Applying = true;
            try
            {
                // De atrás para adelante: cada edición no corre los offsets de las anteriores
                DocumentText.AsSingleUndo(document, () =>
                {
                    for (int i = edits.Count - 1; i >= 0; i--)
                        ok &= DocumentText.Replace(document, edits[i].Offset, edits[i].Length, edits[i].Text);
                });
            }
            finally
            {
                m_Applying = false;
            }
            if (!ok)
            {
                End();   // algún rango era de solo lectura: las posiciones ya no son confiables
                return;
            }

            int shift = 0;
            int newPrimary = 0;
            var newExtras = new List<Cursor>(edits.Count);
            foreach (Edit edit in edits)
            {
                int caret = edit.Offset + shift + edit.CaretInText;
                if (edit.IsPrimary)
                    newPrimary = caret;
                else
                    newExtras.Add(new Cursor(caret));
                shift += edit.Text.Length - edit.Length;
            }
            SetCursors(new Cursor(newPrimary), newExtras);
        }

        /// <summary>
        /// Flechas, Inicio y Fin en todos los cursores. Con <paramref name="extend"/> (Shift) cada uno
        /// extiende su selección; con <paramref name="byWord"/> (Ctrl) ← y → van por palabra, como el editor.
        /// </summary>
        void Move(Keys key, bool extend, bool byWord)
        {
            if (m_Mirror != null)
                CloseCompletion();

            Document document = m_Editor.Document;
            int tabSize = DocumentText.TabSize(document);
            IWordBreakFinder words = byWord ? WordBreakFinder(document) : null;

            int MoveOne(int offset)
            {
                offset = DocumentText.Clamp(document, offset);
                DocumentLine line = DocumentText.LineOf(document, offset);
                switch (key)
                {
                    case Keys.Left when byWord:
                        return words.FindPreviousWordStart(document, offset);
                    case Keys.Right when byWord:
                        return words.FindNextWordStart(document, offset);
                    case Keys.Left:
                        return offset == line.StartOffset
                            ? (line.Index > 0 ? document.Lines[line.Index - 1].EndOffset : offset)
                            : offset - 1;
                    case Keys.Right:
                        return offset == line.EndOffset
                            ? (line.Index < document.Lines.Count - 1 ? document.Lines[line.Index + 1].StartOffset : offset)
                            : offset + 1;
                    case Keys.Home:
                    {
                        // Como VS Code: primero al comienzo del código, después a la columna 0
                        string text = DocumentText.LineText(document, line);
                        int firstNonBlank = line.StartOffset + DocumentText.LeadingWhitespace(text).Length;
                        return offset == firstNonBlank ? line.StartOffset : firstNonBlank;
                    }
                    case Keys.End:
                        return line.EndOffset;
                    case Keys.Up:
                    case Keys.Down:
                    {
                        int target = line.Index + (key == Keys.Up ? -1 : 1);
                        if (target < 0)
                            return line.StartOffset;
                        if (target >= document.Lines.Count)
                            return line.EndOffset;
                        int column = DocumentText.VisualColumn(DocumentText.LineText(document, line), offset - line.StartOffset, tabSize);
                        DocumentLine targetLine = document.Lines[target];
                        return targetLine.StartOffset + DocumentText.CharIndexAtVisualColumn(DocumentText.LineText(document, targetLine), column, tabSize);
                    }
                }
                return offset;
            }

            Cursor MoveCursor(Cursor cursor)
            {
                // Sin Shift, ← y → con selección solo la colapsan
                if (!extend && !byWord && cursor.HasSelection && key == Keys.Left)
                    return new Cursor(cursor.Start);
                if (!extend && !byWord && cursor.HasSelection && key == Keys.Right)
                    return new Cursor(cursor.End);
                int caret = DocumentText.Clamp(document, MoveOne(cursor.Caret));
                return extend ? new Cursor(cursor.Anchor, caret) : new Cursor(caret);
            }

            SetCursors(MoveCursor(PrimaryOf(m_Editor)), m_Extras.Select(MoveCursor).ToList());
        }

        static IWordBreakFinder WordBreakFinder(Document document) =>
            document.Language?.WordBreakFinder ?? (s_DefaultWordBreakFinder ?? (s_DefaultWordBreakFinder = new DefaultWordBreakFinder()));

        static char CharAt(Document document, int offset)
        {
            string text = DocumentText.Get(document, offset, offset + 1);
            return text.Length == 1 ? text[0] : '\0';
        }

        // =====================================================================================
        // Autocompletado
        // =====================================================================================

        /// <summary>
        /// Lo que el editor cambió por su cuenta en el cursor principal durante un autocompletado (lo que
        /// se escribe con la lista abierta, el ítem elegido, lo que agregue un trigger). Anchor es dónde
        /// estaba el principal al empezar; [Start, OriginalEnd) es el tramo que cambió medido en el texto
        /// de ese momento, que ahora ocupa [Start, End). Antes de Start el texto no cambió.
        /// </summary>
        sealed class Mirror
        {
            public int Anchor;
            public bool Changed;
            public bool Valid = true;
            public bool DeletedLineBreak;
            public int Start;
            public int OriginalEnd;
            public int End;
        }

        bool CompletionVisible => m_Editor.IntelliPrompt.MemberList.Visible;

        /// <summary>
        /// Empieza a seguir lo que el editor cambia en el cursor principal, para replicarlo en los demás
        /// cuando termine de procesar la tecla (FlushMirror).
        /// </summary>
        void BeginMirror()
        {
            if (m_Mirror == null)
                m_Mirror = new Mirror { Anchor = PrimaryOf(m_Editor).Caret };
            ScheduleFlush();
        }

        /// <summary>Replica cuando el editor termine de procesar la tecla.</summary>
        void ScheduleFlush()
        {
            if (m_Mirror != null && !m_Editor.IsDisposed && m_Editor.IsHandleCreated)
                m_Editor.BeginInvoke(new Action(() => Safe(FlushMirror)));
        }

        void CloseCompletion()
        {
            if (CompletionVisible)
                m_Editor.IntelliPrompt.MemberList.Abort();
            FlushMirror();
        }

        /// <summary>
        /// Hace en cada cursor extra lo mismo que cambió en el principal, en la misma posición relativa
        /// (como VS Code: elegir "&amp;Cliente" en la lista lo escribe en todos los cursores). Con la lista
        /// abierta, lo que se escribe para filtrarla también aparece en todos a medida que se escribe.
        /// </summary>
        void FlushMirror()
        {
            Mirror mirror = m_Mirror;
            if (mirror == null)
                return;
            Document document = m_Editor.Document;
            bool listOpen = CompletionVisible;
            if (listOpen)
            {
                // Lo que se escribió con la lista abierta, ya, sin cerrarla. Si algo no cuadra se sigue
                // anotando y se replica todo junto cuando se cierre.
                if (!mirror.Changed || !mirror.Valid || mirror.End > document.Length)
                    return;
                m_Mirror = null;
                if (!ApplyKeepingCompletion(MirrorEdits(document, mirror)))
                {
                    m_Mirror = mirror;
                    return;
                }
                if (s_Sessions.ContainsKey(m_Editor))
                    m_Mirror = new Mirror { Anchor = PrimaryOf(m_Editor).Caret };   // sigue la misma lista
                return;
            }

            m_Mirror = null;
            if (!mirror.Changed || !mirror.Valid || !s_Sessions.ContainsKey(m_Editor) || mirror.End > document.Length)
                return;
            Apply(MirrorEdits(document, mirror));
        }

        /// <summary>Las ediciones de los cursores extra que repiten <paramref name="mirror"/>; el principal no cambia.</summary>
        Func<Cursor, bool, Edit> MirrorEdits(Document document, Mirror mirror)
        {
            string text = DocumentText.Get(document, mirror.Start, mirror.End);
            int caretInText = Math.Max(0, Math.Min(text.Length, PrimaryOf(m_Editor).Caret - mirror.Start));
            return (cursor, isPrimary) =>
            {
                if (isPrimary)
                    return Edit.None(cursor);   // ya lo hizo el editor
                // Sin pasar a otra línea, salvo que en el principal se haya borrado un salto de línea
                int min = 0;
                int max = document.Length;
                if (!mirror.DeletedLineBreak)
                {
                    DocumentLine line = DocumentText.LineOf(document, cursor.Caret);
                    min = line.StartOffset;
                    max = line.EndOffset;
                }
                int start = Math.Max(min, Math.Min(cursor.Caret + mirror.Start - mirror.Anchor, max));
                int end = Math.Max(start, Math.Min(cursor.Caret + mirror.OriginalEnd - mirror.Anchor, max));
                return new Edit { Offset = start, Length = end - start, Text = text, CaretInText = caretInText };
            };
        }

        /// <summary>
        /// Apply con la lista de autocompletado abierta, sin que se cierre. La lista se entera de cada
        /// cambio del texto porque está anotada en una lista privada de popups del editor, y se cierra si
        /// el cambio cae fuera del tramo que completa (sus campos privados l y h: inicio y largo). Mientras
        /// se edita en los cursores extra se la saca de esa lista, y después se le corre el inicio lo que
        /// se haya corrido el principal. Devuelve false, sin tocar nada, si no se encuentra lo que se
        /// espera (otra versión del editor).
        /// </summary>
        bool ApplyKeepingCompletion(Func<Cursor, bool, Edit> editFor)
        {
            IntelliPromptMemberList list = m_Editor.IntelliPrompt.MemberList;
            if (s_ListStart == null || s_ListLength == null)
                return false;
            ArrayList popups = s_EditorArrayLists
                .Select(f => f.GetValue(m_Editor) as ArrayList)
                .FirstOrDefault(a => a != null && a.Contains(list));
            if (popups == null)
                return false;
            int start = (int)s_ListStart.GetValue(list);
            int length = (int)s_ListLength.GetValue(list);
            int caret = PrimaryOf(m_Editor).Caret;
            if (caret < start || caret > start + length)
                return false;   // no son los campos que se esperaba

            int index = popups.IndexOf(list);
            popups.RemoveAt(index);
            try
            {
                Apply(editFor);
            }
            finally
            {
                // El principal no se edita: solo lo corren las ediciones de los cursores de antes
                s_ListStart.SetValue(list, start + PrimaryOf(m_Editor).Caret - caret);
                popups.Insert(Math.Min(index, popups.Count), list);
            }
            return true;
        }

        void Record(DocumentModification modification)
        {
            Mirror mirror = m_Mirror;
            int start = modification.StartOffset;
            int deletedEnd = modification.DeletionEndOffset;

            // Solo cambios pegados a lo que se está completando: otra cosa (otro package, un deshacer
            // desde el menú) no se replica, los cursores extra solo se corren con el texto
            int from = mirror.Changed ? mirror.Start : mirror.Anchor;
            int to = mirror.Changed ? mirror.End : mirror.Anchor;
            if (start > to || deletedEnd < from)
            {
                mirror.Valid = false;
                return;
            }
            if (modification.DeletedText.IndexOf('\n') >= 0)
                mirror.DeletedLineBreak = true;

            if (!mirror.Changed)
            {
                mirror.Changed = true;
                mirror.Start = start;
                mirror.OriginalEnd = deletedEnd;
                mirror.End = modification.InsertionEndOffset;
                return;
            }
            mirror.Start = Math.Min(mirror.Start, start);
            if (deletedEnd > mirror.End)
            {
                mirror.OriginalEnd += deletedEnd - mirror.End;
                mirror.End = deletedEnd;
            }
            mirror.End += modification.LengthDelta;
        }

        /// <summary>Un ítem confirmado sin que la tecla pasara por acá (doble click en la lista, Ctrl+Espacio que GeneXus atendió antes).</summary>
        void OnDocumentTextChanging(object sender, DocumentModificationEventArgs e) => Safe(() =>
        {
            if (!m_Applying && m_Mirror == null && e.Modification.Type == DocumentModificationType.AutoComplete)
                BeginMirror();
        });

        void OnMemberListClosed(object sender, CancelEventArgs e) => Safe(ScheduleFlush);

        // =====================================================================================
        // Estado y dibujo
        // =====================================================================================

        /// <summary>El principal y los extra, ordenados y sin superponerse.</summary>
        List<Cursor> AllCursors() => Normalize(m_Editor.Document, PrimaryOf(m_Editor), m_Extras).Select(c => c.cursor).ToList();

        static List<(Cursor cursor, bool isPrimary)> Normalize(Document document, Cursor primary, IEnumerable<Cursor> extras)
        {
            return Normalize(document, new[] { (primary, true) }.Concat(extras.Select(c => (c, false))));
        }

        /// <summary>
        /// Ordena los cursores y, como VS Code, funde los que se pisan: dos selecciones que se superponen
        /// (Shift+Fin con dos cursores en la misma línea) o un cursor dentro de una selección quedan en uno
        /// solo. Si uno de los fundidos era el principal, el resultado es el principal.
        /// </summary>
        static List<(Cursor cursor, bool isPrimary)> Normalize(Document document, IEnumerable<(Cursor cursor, bool isPrimary)> cursors)
        {
            List<(Cursor cursor, bool isPrimary)> sorted = cursors
                .Select(c => (new Cursor(DocumentText.Clamp(document, c.cursor.Anchor), DocumentText.Clamp(document, c.cursor.Caret)), c.isPrimary))
                .OrderBy(c => c.Item1.Start)
                .ThenBy(c => c.Item1.End)
                .ToList();

            var merged = new List<(Cursor cursor, bool isPrimary)>(sorted.Count);
            foreach (var (cursor, isPrimary) in sorted)
            {
                if (merged.Count > 0)
                {
                    var (last, lastIsPrimary) = merged[merged.Count - 1];
                    if (cursor.Start == last.Start || cursor.Start < last.End || (cursor.Start == last.End && !cursor.HasSelection))
                    {
                        merged[merged.Count - 1] = (Merge(last, cursor), lastIsPrimary || isPrimary);
                        continue;
                    }
                }
                merged.Add((cursor, isPrimary));
            }
            return merged;
        }

        static Cursor Merge(Cursor a, Cursor b)
        {
            int start = Math.Min(a.Start, b.Start);
            int end = Math.Max(a.End, b.End);
            // Mantiene el sentido de la selección (de qué lado queda el cursor)
            Cursor withSelection = a.HasSelection ? a : b;
            return withSelection.Caret < withSelection.Anchor ? new Cursor(end, start) : new Cursor(start, end);
        }

        void SetCursors(Cursor primary, IEnumerable<Cursor> extras)
        {
            List<(Cursor cursor, bool isPrimary)> all = Normalize(m_Editor.Document, primary, extras);
            primary = all.First(c => c.isPrimary).cursor;
            List<Cursor> old = m_Extras;
            List<Cursor> updated = all.Where(c => !c.isPrimary).Select(c => c.cursor).Take(MaxCursors - 1).ToList();

            EditorView view = m_Editor.SelectedView;
            Selection selection = view.Selection;
            if (selection.StartOffset != primary.Anchor || selection.EndOffset != primary.Caret || selection.Mode != SelectionModes.ContinuousStream)
                selection.SelectRange(new TextRange(primary.Anchor, primary.Caret), SelectionModes.ContinuousStream);

            Invalidate(old);
            m_Extras = updated;
            if (m_Extras.Count == 0)
            {
                End();
                return;
            }

            m_CaretsVisible = true;
            if (m_Blink.Enabled)
            {
                m_Blink.Stop();   // reinicia el parpadeo: después de escribir los cursores se ven
                m_Blink.Start();
            }
            Invalidate(m_Extras);
            view.ScrollToCaret();
        }

        /// <summary>Termina la sesión: un solo cursor otra vez.</summary>
        void End()
        {
            if (!s_Sessions.Remove(m_Editor))
                return;
            m_Blink.Stop();
            m_Blink.Dispose();
            m_Mirror = null;
            m_Editor.DocumentTextChanging -= OnDocumentTextChanging;
            m_Editor.DocumentTextChanged -= OnDocumentTextChanged;
            m_Editor.IntelliPromptMemberListClosed -= OnMemberListClosed;
            m_Editor.ViewVerticalScroll -= OnViewScroll;
            m_Editor.ViewHorizontalScroll -= OnViewScroll;
            m_Editor.SelectedViewChanged -= OnSelectedViewChanged;
            m_Editor.HandleDestroyed -= OnHandleDestroyed;
            List<Cursor> old = m_Extras;
            m_Extras = new List<Cursor>();
            if (!m_Editor.IsDisposed && m_Editor.IsHandleCreated)
                Safe(() => Invalidate(old));
            ReleaseHandle();
        }

        /// <summary>
        /// Cambios del texto que no hicimos nosotros (autocompletado, deshacer desde el menú, otro
        /// package): los cursores extra se corren igual que el texto. Los del autocompletado además se
        /// anotan para replicarlos en los cursores extra.
        /// </summary>
        void OnDocumentTextChanged(object sender, DocumentModificationEventArgs e) => Safe(() =>
        {
            if (m_Applying)
                return;
            if (e.IsProgrammaticTextReplacement)
            {
                End();   // se reemplazó todo el texto (recarga del objeto)
                return;
            }
            DocumentModification modification = e.Modification;
            if (m_Mirror != null)
                Record(modification);
            int start = modification.StartOffset;
            int deletedEnd = modification.DeletionEndOffset;
            int delta = modification.LengthDelta;
            int Follow(int o) => o >= deletedEnd ? o + delta : (o > start ? start : o);
            var moved = m_Extras.Select(c => (new Cursor(Follow(c.Anchor), Follow(c.Caret)), false));
            Invalidate(m_Extras);
            m_Extras = Normalize(m_Editor.Document, moved).Select(c => c.cursor).ToList();
            Invalidate(m_Extras);
        });

        void OnViewScroll(object sender, EditorViewEventArgs e) => Safe(() => m_Editor.Invalidate());

        void OnSelectedViewChanged(object sender, EventArgs e) => Safe(() => m_Editor.Invalidate());

        void OnHandleDestroyed(object sender, EventArgs e) => Safe(End);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg != WM_PAINT || m_Extras.Count == 0)
            {
                base.WndProc(ref m);
                return;
            }

            // Lo que el editor va a repintar: las selecciones son semitransparentes y pintarlas dos veces
            // sobre lo mismo las oscurecería, así que solo se dibuja ahí
            Region update = null;
            Safe(() => update = UpdateRegion());
            try
            {
                base.WndProc(ref m);
                if (update != null && m_Extras.Count > 0)
                    Safe(() => Paint(update));
            }
            finally
            {
                update?.Dispose();
            }
        }

        Region UpdateRegion()
        {
            IntPtr region = CreateRectRgn(0, 0, 0, 0);
            try
            {
                return GetUpdateRgn(Handle, region, false) > NULLREGION ? Region.FromHrgn(region) : null;
            }
            finally
            {
                DeleteObject(region);
            }
        }

        /// <summary>
        /// Dibuja las selecciones y los cursores extra después de que el editor se pintó. Solo los
        /// visibles y recortados al área de texto (no se dibujan sobre los números de línea).
        /// </summary>
        void Paint(Region update)
        {
            EditorView view = m_Editor.SelectedView;
            if (view == null)
                return;
            Rectangle area = view.TextAreaBounds;
            if (area.Width <= 0 || area.Height <= 0)
                return;

            using (Graphics g = Graphics.FromHwnd(Handle))
            {
                g.SetClip(area);
                g.IntersectClip(update);
                if (m_Extras.Any(c => c.HasSelection))
                    PaintSelections(g, view, area);
                if (m_CaretsVisible)
                    PaintCarets(g, view, area);
            }
        }

        void PaintSelections(Graphics g, EditorView view, Rectangle area)
        {
            Document document = m_Editor.Document;
            using (var brush = new SolidBrush(SelectionColor()))
            {
                foreach (Cursor cursor in m_Extras)
                {
                    if (!cursor.HasSelection || cursor.End > document.Length)
                        continue;
                    int lastLine = DocumentText.LineIndexOf(document, cursor.End);
                    for (int i = DocumentText.LineIndexOf(document, cursor.Start); i <= lastLine; i++)
                    {
                        DocumentLine line = document.Lines[i];
                        Rectangle r;
                        try
                        {
                            Rectangle first = view.GetCharacterBounds(Math.Max(cursor.Start, line.StartOffset));
                            if (first.Top > area.Bottom)
                                return;   // ordenados: el resto está más abajo
                            if (first.Bottom < area.Top)
                                continue;
                            int right = view.GetCharacterBounds(Math.Min(cursor.End, line.EndOffset)).Left;
                            // El salto de línea seleccionado se ve como un espacio al final, como en el editor
                            if (cursor.End > line.EndOffset)
                                right += Math.Max(4, first.Height / 2);
                            r = new Rectangle(first.Left, first.Top, right - first.Left, first.Height);
                        }
                        catch (Exception)
                        {
                            continue;   // línea dentro de una región colapsada u otra rareza: esa no se dibuja
                        }
                        if (r.Width > 0 && r.IntersectsWith(area))
                            g.FillRectangle(brush, r);
                    }
                }
            }
        }

        void PaintCarets(Graphics g, EditorView view, Rectangle area)
        {
            using (var brush = new SolidBrush(CaretColor()))
            {
                int width = CaretWidth(g);
                int documentLength = m_Editor.Document.Length;
                foreach (Cursor cursor in m_Extras)
                {
                    if (cursor.Caret > documentLength)
                        continue;
                    Rectangle r;
                    try
                    {
                        r = CaretBounds(view, cursor.Caret, width);
                    }
                    catch (Exception)
                    {
                        continue;   // posición dentro de una región colapsada u otra rareza: ese no se dibuja
                    }
                    if (r.Top > area.Bottom)
                        break;   // ordenados: el resto está más abajo
                    if (r.IntersectsWith(area))
                        g.FillRectangle(brush, r);
                }
            }
        }

        /// <summary>Repinta donde están (o estaban) estos cursores: toda el área de texto si alguno tiene selección.</summary>
        void Invalidate(List<Cursor> cursors)
        {
            if (cursors.Any(c => c.HasSelection))
            {
                EditorView view = m_Editor.IsDisposed || !m_Editor.IsHandleCreated ? null : m_Editor.SelectedView;
                if (view != null)
                    m_Editor.Invalidate(view.TextAreaBounds);
                return;
            }
            InvalidateCarets(cursors);
        }

        void InvalidateCarets(List<Cursor> cursors)
        {
            if (cursors.Count == 0 || m_Editor.IsDisposed || !m_Editor.IsHandleCreated)
                return;
            EditorView view = m_Editor.SelectedView;
            if (view == null)
                return;
            Rectangle area = view.TextAreaBounds;
            if (cursors.Count > MaxRectsToInvalidate)
            {
                m_Editor.Invalidate(area);
                return;
            }
            int documentLength = m_Editor.Document.Length;
            foreach (Cursor cursor in cursors)
            {
                if (cursor.Caret > documentLength)
                    continue;
                Rectangle r;
                try
                {
                    r = CaretBounds(view, cursor.Caret, 12);   // de sobra para el ancho del cursor en cualquier DPI
                }
                catch (Exception)
                {
                    // Posición que el editor todavía no ubicó (por ejemplo, en medio de un cambio de
                    // texto): se repinta toda el área, que es más lento pero nunca deja rastros
                    m_Editor.Invalidate(area);
                    return;
                }
                r.Offset(-3, 0);
                if (r.IntersectsWith(area))
                    m_Editor.Invalidate(r);
            }
        }

        static Rectangle CaretBounds(EditorView view, int offset, int width)
        {
            Rectangle character = view.GetCharacterBounds(offset);
            return new Rectangle(character.Left, character.Top, width, character.Height);
        }

        static int CaretWidth(Graphics g) => Math.Max(2, (int)Math.Round(2 * g.DpiX / 96f));

        Color CaretColor() => m_Editor.BackColor.GetBrightness() < 0.5f ? Color.FromArgb(0xAE, 0xAF, 0xAD) : Color.Black;

        /// <summary>El color de selección del editor, semitransparente porque se pinta encima del texto ya dibujado.</summary>
        Color SelectionColor()
        {
            ISyntaxEditorRenderer renderer = m_Editor.RendererResolved;
            Color color = m_Editor.ContainsFocus ? renderer.SelectionFocusedBackColor : renderer.SelectionUnfocusedBackColor;
            if (color.A == 0)
                color = SystemColors.Highlight;
            return Color.FromArgb(color.A < 255 ? color.A : 96, color.R, color.G, color.B);
        }

        /// <summary>Un error acá no puede romper el editor: se informa en Output y se sigue.</summary>
        static void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error("múltiples cursores", ex);
            }
        }
    }
}
