using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ActiproSoftware.SyntaxEditor;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.UI.Framework.Objects;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;
using Artech.Genexus.Common.Parts;
using Artech.Genexus.UI.Common.Editor;

namespace GxCodeCheck
{
    /// <summary>
    /// Los errores de un editor de código (Source, Events, Rules...): subraya los que GeneXus informó para su
    /// parte (al guardar y en la especificación) más los chequeos propios del texto, y al dejar el mouse sobre
    /// un subrayado muestra <see cref="ErrorPopup"/>.
    /// </summary>
    /// <remarks>
    /// No usa el parser de GeneXus (tarda segundos en un objeto grande y congelaría el editor). Al guardar
    /// corre el chequeo de tipos propio (<see cref="TypeChecker"/>, milisegundos), que vale hasta que termina la
    /// especificación. Se redibuja solo cuando cambian los errores del objeto (al guardar y con la
    /// especificación); entre medio los subrayados se corren solos con el texto que se escribe.
    /// </remarks>
    sealed class EditorSession : IDisposable
    {
        readonly GxTextEditor m_Editor;
        readonly SyntaxEditor m_Syntax;
        readonly Timer m_ApplyTimer = new Timer { Interval = 100 };
        ErrorPopup m_Popup;
        Diagnostic m_StatusDiagnostic;
        bool m_Saved;
        // Lo que encontraron el chequeo de tipos y el del estándar en el último guardado (corren una vez por guardado), o null
        List<Diagnostic> m_Types;
        bool m_TypesPending;
        // Si ya se comparó el chequeo de tipos de este guardado con la especificación (para el log)
        bool m_Compared;
        TypeWarmer m_Warmer;
        // Los errores ya corregidos desde el cartel: no vuelven a marcarse hasta el próximo guardado
        readonly HashSet<string> m_Fixed = new HashSet<string>();
        bool m_Disposed;

        public event EventHandler Closed;

        public EditorSession(GxTextEditor editor, SyntaxEditor syntax)
        {
            m_Editor = editor;
            m_Syntax = syntax;

            m_ApplyTimer.Tick += (s, e) => { m_ApplyTimer.Stop(); Apply(); };
            m_Syntax.DocumentTextChanged += OnTextChanged;
            m_Syntax.ViewMouseHover += OnMouseHover;
            m_Syntax.SelectionChanged += OnSelectionChanged;
            m_Syntax.ViewVerticalScroll += OnScroll;
            m_Syntax.ViewHorizontalScroll += OnScroll;
            m_Syntax.Disposed += OnDisposed;
            SpecStore.Changed += OnErrorsChanged;
            CallerCheck.Changed += OnErrorsChanged;
            KbCheck.Changed += OnErrorsChanged;

            // Lee de antemano lo que va a necesitar el chequeo de tipos, de a poco: el primer guardado ya es rápido
            KBObjectPart opened = Part;
            // Cómo es el parm() al abrirlo, para saber al guardar si cambió (después de abrir el editor)
            if (Settings.Enabled && opened?.KBObject is Artech.Genexus.Common.Objects.Procedure)
                m_Syntax.BeginInvoke(new Action(() => Safe(() => CallerCheck.Remember(opened.KBObject))));
            if (Settings.Enabled && Settings.QuickTypes && (opened is ProcedurePart || opened is EventsPart))
                m_Warmer = new TypeWarmer(opened.KBObject, m_Syntax.Document.Text);

            // Lo que GeneXus ya informó del objeto (por ejemplo, el último build)
            string name = ObjectName;
            if (name != null && SpecStore.For(name).Count > 0)
                Apply();
        }

        /// <summary>El módulo del objeto del editor (vacío el raíz), o null.</summary>
        public string ModuleName
        {
            get
            {
                try
                {
                    Module module = Part?.KBObject?.Module;
                    return module == null ? null : module.IsRoot ? "" : module.QualifiedName.ToString();
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>Nombre calificado del objeto del editor, o null.</summary>
        public string ObjectName => Part?.KBObject?.QualifiedName.ToString();

        KBObjectPart Part
        {
            get
            {
                try
                {
                    return m_Editor.GetPart();
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>Se está guardando el objeto: a partir de ahora también se revisa el texto.</summary>
        public void Saving()
        {
            m_Saved = true;
            m_Types = null;
            m_Fixed.Clear();
            ScheduleApply();
        }

        /// <summary>GeneXus guardó el objeto (las variables autodefinidas ya están): el chequeo de tipos.</summary>
        public void Saved()
        {
            m_TypesPending = true;
            m_Compared = false;
            ScheduleApply();
        }

        /// <summary>Vuelve a dibujar todo, por ejemplo después de cambiar las opciones.</summary>
        public void Refresh() => ScheduleApply();

        // Los mensajes llegan de a uno: se dibujan todos juntos
        void ScheduleApply()
        {
            m_ApplyTimer.Stop();
            m_ApplyTimer.Start();
        }

        void Apply()
        {
            if (m_Disposed)
                return;
            KBObjectPart part = Part;
            string name = ObjectName;
            // Solo las partes con código (Source, Events, Rules, Conditions...): no otros editores de texto
            if (!Settings.Enabled || !(part is ISource) || name == null)
            {
                Squiggles.Clear(m_Syntax);
                return;
            }

            string text = m_Syntax.Document.Text;
            var source = new SourceText(text);
            var parser = new List<RawMessage>();
            var diagnostics = new List<Diagnostic>();
            foreach (SpecStore.Entry entry in SpecStore.For(name))
            {
                if (entry.Part == Guid.Empty)
                    diagnostics.AddRange(Analyzer.FromUndefinedVariable(entry.Message, source));
                else if (!IsThisPart(entry, part))
                    continue;
                else if (entry.Message.Code.StartsWith("spc"))
                    diagnostics.Add(Analyzer.FromSpecifier(entry.Message, source));
                else
                    parser.Add(entry.Message);
            }
            // Los chequeos del texto, desde que se guarda (aunque GeneXus no haya encontrado nada)
            if (m_Saved || parser.Count > 0)
                diagnostics.AddRange(Analyzer.FromParser(text, parser, blocks: part is ProcedurePart || part is EventsPart));
            // Lo que quedó mal al cambiar el parm() de un procedimiento que llama
            diagnostics.AddRange(CallerCheck.For(name, part.Type));
            // Lo de la última revisión del código (Tools), hasta que se guarde el objeto
            foreach (Diagnostic found in KbCheck.For(name, part.Type))
            {
                if (!diagnostics.Any(d => d != null && d.Code == found.Code && d.Line == found.Line && d.Column == found.Column))
                    diagnostics.Add(found);
            }
            diagnostics.RemoveAll(d => d == null);

            // Hasta que termine la especificación de lo guardado, que es la que vale
            bool saveFailed = parser.Any(m => m.Severity == Severity.Error);
            if (m_TypesPending)
            {
                m_TypesPending = false;
                m_Types = (Settings.QuickTypes || Settings.Standards) && !saveFailed && (part is ProcedurePart || part is EventsPart)
                    ? CheckTypes(part, name, source) : null;
            }
            if (m_Types != null && Settings.QuickTypes && !(Settings.ShowSpecifier && SpecStore.IsSpecified(name)))
            {
                // Lo que la especificación ya informó en esa línea
                diagnostics.AddRange(m_Types.Where(t => t.Origin == Origin.Types && !diagnostics.Any(d => d.Origin == Origin.Specifier && d.Line == t.Line)));
            }
            // El estándar no lo revisa la especificación: queda hasta el próximo guardado
            if (m_Types != null)
                diagnostics.AddRange(m_Types.Where(t => t.Origin == Origin.Standard && Settings.IsStandardOn(t.Code)));

            diagnostics.RemoveAll(d => m_Fixed.Contains(FixedKey(d)));
            Squiggles.Apply(m_Syntax, diagnostics, Settings.ShowWarnings);
            m_StatusDiagnostic = null;

            if (m_Types != null && !m_Compared && Settings.ShowSpecifier && SpecStore.IsSpecified(name))
            {
                m_Compared = true;
                CompareWithSpecification(name, diagnostics.Where(d => d.Origin == Origin.Specifier).ToList(), m_Types.Where(t => t.Origin == Origin.Types).ToList());
            }

            // Al guardar con errores, cuántos son y dónde está el primero
            if (parser.Count > 0)
            {
                var errors = diagnostics.Where(d => d.Severity == Severity.Error).OrderBy(d => d.Line).ToList();
                if (errors.Count > 0)
                    CodeCheckPackage.Status($"GxCodeCheck: no se pudo guardar, {errors.Count} error(es). Línea {errors[0].Line + 1}: {errors[0].Message}");
            }
        }

        /// <summary>
        /// En el log, lo que encontró el chequeo rápido contra lo que encontró la especificación (la que vale), por
        /// línea: lo que solo dice la especificación es lo que se le escapa, y lo que solo dice el chequeo, un
        /// error que no existe.
        /// </summary>
        static void CompareWithSpecification(string name, List<Diagnostic> specification, List<Diagnostic> quick)
        {
            static string Item(Diagnostic d) => $"L{d.Line + 1} {d.Code}";
            var specLines = new HashSet<int>(specification.Where(d => d.Severity != Severity.Info).Select(d => d.Line));
            var quickLines = new HashSet<int>(quick.Where(d => d.Severity != Severity.Info).Select(d => d.Line));
            var onlySpec = specification.Where(d => d.Severity != Severity.Info && !quickLines.Contains(d.Line)).Select(Item).ToList();
            var onlyQuick = quick.Where(d => d.Severity != Severity.Info && !specLines.Contains(d.Line)).Select(d => $"{Item(d)} {d.Message}").ToList();
            int both = specLines.Count(quickLines.Contains);
            Log.Write($"Comparación de {name}: especificación {specLines.Count} línea(s), chequeo rápido {quickLines.Count}, coinciden {both}"
                      + (onlySpec.Count > 0 ? "; solo la especificación: " + string.Join(", ", onlySpec.Take(10)) : "")
                      + (onlyQuick.Count > 0 ? "; solo el chequeo rápido: " + string.Join(" | ", onlyQuick.Take(10)) : ""));
        }

        static List<Diagnostic> CheckTypes(KBObjectPart part, string name, SourceText source)
        {
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var context = new KbTypeContext(part.KBObject);
                List<Diagnostic> found = TypeChecker.Run(source, context, out List<string> unresolved);
                if (part is ProcedurePart && KbCheck.IsCommitOnExit(part.KBObject))
                    found.Add(TypeChecker.CommitOnExitWarning(source));
                int standards = found.Count(d => d.Origin == Origin.Standard);
                Log.Write($"Chequeo de tipos de {name}: {found.Count - standards} error(es), {standards} del estándar, en {watch.ElapsedMilliseconds} ms ({context.Timings})"
                          + (unresolved.Count > 0 ? "; sin leer: " + string.Join(", ", unresolved.Take(10)) : ""));
                if (!Settings.QuickTypes)
                    found.RemoveAll(d => d.Origin == Origin.Types);
                var errors = found.Where(d => d.Severity == Severity.Error).OrderBy(d => d.Line).ToList();
                if (errors.Count > 0)
                    CodeCheckPackage.Status($"GxCodeCheck: {errors.Count} error(es) de tipos. Línea {errors[0].Line + 1}: {errors[0].Message}");
                return found;
            }
            catch (Exception ex)
            {
                Log.Write($"Error en el chequeo de tipos de {name}: {ex}");
                return new List<Diagnostic>();
            }
        }

        /// <summary>
        /// Si el mensaje es de esta parte: por su tipo o por su nombre. Al guardar un procedimiento que modifica
        /// WorkWithPlus GeneXus informa "Source (by WorkWithPlus)" con el tipo de otra parte del objeto (la de
        /// WorkWithPlus), así que el nombre va antes que el tipo de las otras partes. Si no es de ninguna, va al
        /// código principal: la especificación de un Web Panel informa los Events como "Source", con el tipo
        /// de parte de un Procedure.
        /// </summary>
        static bool IsThisPart(SpecStore.Entry entry, KBObjectPart part)
        {
            if (entry.Part == part.Type || IsNamed(part, entry.PartName))
                return true;
            var parts = part.KBObject.Parts.Cast<KBObjectPart>().ToList();
            if (parts.Any(p => p.Type == entry.Part || IsNamed(p, entry.PartName)))
                return false;
            return part is ProcedurePart || part is EventsPart;
        }

        static bool IsNamed(KBObjectPart part, string name) =>
            !string.IsNullOrEmpty(name)
            && (string.Equals(name, part.TypeDescriptor?.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, part.TypeDescriptor?.Description, StringComparison.OrdinalIgnoreCase)
                || (part is ProcedurePart && string.Equals(name, "Source", StringComparison.OrdinalIgnoreCase)));

        void OnTextChanged(object sender, DocumentModificationEventArgs e) => ClosePopup();

        void OnMouseHover(object sender, EditorViewMouseEventArgs e)
        {
            try
            {
                SyntaxEditorHitTestResult hit = e.HitTestResult;
                if (!Settings.Enabled || hit == null || hit.Target != SyntaxEditorHitTestTarget.TextArea)
                    return;
                List<Diagnostic> found = Squiggles.At(m_Syntax, hit.Offset, includeEnd: false);
                if (found.Count == 0)
                    return;
                // En lugar del tooltip de GeneXus (tipo de la variable, descripción del atributo...)
                e.ToolTipText = null;
                m_Syntax.IntelliPrompt.QuickInfo.Hide();
                if (m_Popup != null && m_Popup.Shows(found))
                    return;
                ShowPopup(found, hit.Offset);
            }
            catch (Exception ex)
            {
                Log.Write("Error al mostrar el error: " + ex);
            }
        }

        void ShowPopup(List<Diagnostic> diagnostics, int offset, bool focus = false)
        {
            ClosePopup();
            Rectangle anchor = m_Syntax.RectangleToScreen(m_Syntax.SelectedView.GetCharacterBounds(offset));
            var fixes = diagnostics.SelectMany(d => d.Fixes.Where(CanApply).Select(f => (d, f))).ToList();
            var popup = new ErrorPopup(diagnostics, PartTitle(), m_Syntax.BackColor, anchor, fixes, (d, f) => Safe(() => ApplyFix(d, f)));
            popup.FormClosed += (s, e) =>
            {
                if (m_Popup == popup)
                    m_Popup = null;
            };
            m_Popup = popup;
            if (focus)
                popup.ShowFocused(m_Syntax.FindForm());
            else
                popup.Show(m_Syntax.FindForm());
        }

        /// <summary>Cuántos errores hay marcados donde está el cursor (para el atajo de las correcciones).</summary>
        public int MarkedAtCaret() =>
            m_Disposed || !Settings.Enabled ? 0 : Squiggles.At(m_Syntax, m_Syntax.Caret.Offset, includeEnd: true).Count;

        /// <summary>
        /// Con el atajo de las correcciones: el cartel de lo marcado donde está el cursor, con el foco para
        /// elegir con el teclado.
        /// </summary>
        public void ShowAtCaret()
        {
            if (m_Disposed || !Settings.Enabled)
                return;
            int caret = m_Syntax.Caret.Offset;
            List<Diagnostic> found = Squiggles.At(m_Syntax, caret, includeEnd: true);
            if (found.Count > 0)
                ShowPopup(found, caret, focus: true);
        }

        /// <summary>Si es el editor de ese control.</summary>
        public bool Contains(Control control)
        {
            for (Control c = control; c != null; c = c.Parent)
            {
                if (c == m_Syntax)
                    return true;
            }
            return false;
        }

        // El comando de GxQuickVar que declara la variable bajo el cursor (si está instalado)
        static readonly CommandKey QuickVarDeclare = new CommandKey(new Guid("3E8F6A42-9C1D-4B7E-A5F0-6D2C81B4E937"), "GxQuickVarDeclare");
        static bool? s_QuickVar;

        static bool QuickVarInstalled => s_QuickVar ?? (s_QuickVar = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "GxQuickVar")).Value;

        bool CanApply(Fix fix)
        {
            if (m_Editor.ReadOnly)
                return false;
            switch (fix.Kind)
            {
                case FixKind.DeclareVariable:
                    return QuickVarInstalled;
                case FixKind.CommitOnExitNo:
                    return Part?.KBObject is Artech.Genexus.Common.Objects.Procedure;
                default:
                    return true;
            }
        }

        static string FixedKey(Diagnostic d) => $"{d.Code}|{d.Line}|{d.Column}|{d.Message}";

        void ApplyFix(Diagnostic d, Fix fix)
        {
            if (m_Disposed)
                return;
            if (fix.Kind == FixKind.CommitOnExitNo)
            {
                KBObject obj = Part?.KBObject;
                if (obj == null)
                    return;
                obj.SetPropertyValue(StandardProperty.CommitOnExit, StandardProperty.No);
                if (UIServices.DocumentManager.IsOpenDocument(obj, out IGxDocument document))
                    document.Dirty = true;
                Fixed(d, "Commit on exit quedó en No; se aplica al guardar el objeto");
                return;
            }

            int? anchor = Squiggles.Anchor(m_Syntax, d);
            Document doc = m_Syntax.Document;
            // Con "\n" solo: las posiciones del documento cuentan así cada salto de línea (Document.Text trae "\r\n")
            string text = doc.GetText(LineTerminator.Newline);
            if (anchor == null || !fix.TryLocate(text, d, anchor.Value, out int offset, out string replacement))
            {
                Log.Write($"Corrección no aplicada en {ObjectName}: {fix} ({d.Code}, línea {d.Line + 1}); "
                          + (anchor == null ? "el subrayado ya no está" : $"en {anchor} hay '{Around(text, anchor.Value)}'"));
                CodeCheckPackage.Status("GxCodeCheck: el código cambió en ese lugar desde que se revisó; guardá para volver a revisarlo");
                return;
            }

            if (fix.Kind == FixKind.DeclareVariable)
            {
                // GxQuickVar declara la variable que está bajo el cursor
                m_Syntax.SelectedView.Selection.SelectRange(offset + 1, 0);
                m_Syntax.Focus();
                m_Syntax.BeginInvoke(new Action(() => Safe(() => UIServices.CommandDispatcher.Dispatch(QuickVarDeclare))));
                return;
            }

            doc.UndoRedo.StartGroup(DocumentModificationType.Custom);
            try
            {
                const DocumentModificationOptions options = DocumentModificationOptions.CheckReadOnly;
                if (fix.Expected.Length == 0)
                    doc.InsertText(DocumentModificationType.Custom, offset, replacement, options);
                else if (replacement.Length == 0)
                    doc.DeleteText(DocumentModificationType.Custom, offset, fix.Expected.Length, options);
                else
                    doc.ReplaceText(DocumentModificationType.Custom, offset, fix.Expected.Length, replacement, options);
            }
            finally
            {
                doc.UndoRedo.EndGroup();
            }
            m_Syntax.SelectedView.Selection.SelectRange(offset + replacement.Length, 0);
            m_Syntax.Focus();
            Fixed(d, fix.Title);
        }

        static string Around(string text, int offset)
        {
            int start = Math.Max(0, Math.Min(offset, text.Length));
            return text.Substring(start, Math.Min(30, text.Length - start)).Replace("\n", "\\n");
        }

        /// <summary>Saca el subrayado del error corregido (hasta el próximo guardado, que lo vuelve a revisar).</summary>
        void Fixed(Diagnostic d, string what)
        {
            m_Fixed.Add(FixedKey(d));
            Squiggles.Remove(m_Syntax, d);
            m_StatusDiagnostic = null;
            CodeCheckPackage.Status("GxCodeCheck: " + what);
            Log.Write($"Corrección en {ObjectName}: {what} ({d.Code}, línea {d.Line + 1})");
        }

        void ClosePopup()
        {
            ErrorPopup popup = m_Popup;
            m_Popup = null;
            if (popup != null && !popup.IsDisposed)
                popup.Close();
        }

        /// <summary>"Procedure 'Modulo.Objeto' Source", para el texto que se copia.</summary>
        string PartTitle()
        {
            KBObjectPart part = Part;
            if (part?.KBObject == null)
                return null;
            return $"{part.KBObject.TypeDescriptor?.Name} '{part.KBObject.QualifiedName}' {part.TypeDescriptor?.Name}".Trim();
        }

        void OnSelectionChanged(object sender, SelectionEventArgs e)
        {
            // En la barra de estado, el error donde quedó el cursor (útil sin mouse)
            try
            {
                if (!Settings.Enabled)
                    return;
                Diagnostic d = Squiggles.At(m_Syntax, m_Syntax.Caret.Offset, includeEnd: true).FirstOrDefault();
                if (d == m_StatusDiagnostic)
                    return;
                m_StatusDiagnostic = d;
                CodeCheckPackage.Status(d == null ? "" : $"GxCodeCheck: {d.Message}");
            }
            catch (Exception ex)
            {
                Log.Write("Error al mostrar el error en la barra de estado: " + ex.Message);
            }
        }

        void OnScroll(object sender, EditorViewEventArgs e) => ClosePopup();

        void OnErrorsChanged(string objectName)
        {
            if (string.Equals(objectName, ObjectName, StringComparison.OrdinalIgnoreCase))
                ScheduleApply();
        }

        void OnDisposed(object sender, EventArgs e) => Dispose();

        static void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Write("GxCodeCheck: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;
            SpecStore.Changed -= OnErrorsChanged;
            CallerCheck.Changed -= OnErrorsChanged;
            KbCheck.Changed -= OnErrorsChanged;
            m_Syntax.DocumentTextChanged -= OnTextChanged;
            m_Syntax.ViewMouseHover -= OnMouseHover;
            m_Syntax.SelectionChanged -= OnSelectionChanged;
            m_Syntax.ViewVerticalScroll -= OnScroll;
            m_Syntax.ViewHorizontalScroll -= OnScroll;
            m_Syntax.Disposed -= OnDisposed;
            m_ApplyTimer.Dispose();
            m_Warmer?.Dispose();
            ClosePopup();
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
