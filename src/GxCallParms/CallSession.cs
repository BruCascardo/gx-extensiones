using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ActiproSoftware.SyntaxEditor;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using Artech.Genexus.Common.Parts.Variables;
using Artech.Genexus.Common.Types;

namespace GxCallParms
{
    static partial class CallFiller
    {
        /// <summary>La llamada a completar: dónde está, a qué objeto y qué va en cada lugar.</summary>
        sealed class Plan
        {
            public Context Context;
            public CallSite Site;
            public KBObject Target;
            public int ParameterCount;
            /// <summary>En el orden de los paréntesis; el valor devuelto, si va, es el último.</summary>
            public List<Slot> Slots;
            /// <summary>La salida que devuelve el udp, si la hay.</summary>
            public Parameter Returned;
            /// <summary>"&amp;X = Objeto.udp(" ya escrito: X.</summary>
            public string AssignedName;
            /// <summary>"Cliente_Get.udp", como está escrito.</summary>
            public string CallText;
        }

        /// <summary>
        /// Completa una llamada como el IntelliSense, mientras se escribe en el editor: la ayuda de parámetros
        /// de GeneXus (la de Actipro) muestra la firma con el lugar actual en negrita, y en cada lugar vacío se
        /// abre la lista de autocompletado con lo que se puede pasar. Enter / Tab eligen de la lista o, sin
        /// elegir, aceptan lo escrito y pasan al siguiente lugar; en el último salen del ")". Al terminar se
        /// declaran las &amp;variables nuevas con el tipo de su parámetro.
        /// </summary>
        sealed class Session
        {
            // El trigger de GeneXus que abre la lista de todas las variables al escribir "&"
            const string VariablesTrigger = "VariablesListTrigger";

            static readonly Regex VariableRegex = new Regex(@"^&(?<name>[A-Za-z_]\w*)$", RegexOptions.CultureInvariant);
            static readonly Regex VariablePathRegex = new Regex(@"^&[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*$", RegexOptions.CultureInvariant);

            static readonly Dictionary<SyntaxEditor, Session> s_Active = new Dictionary<SyntaxEditor, Session>();

            readonly Plan m_Plan;
            readonly SyntaxEditor m_Editor;
            // El "(" de la llamada; -1 si se borró
            int m_Open;
            // Se editó antes del "(": los offsets del nombre y del método ya no valen
            bool m_Moved;
            // Se llegó a un lugar escribiendo "," o pasando al siguiente: abrir su lista o poner lo fijo
            bool m_Enter;
            // Se eligió de la lista: pasar al siguiente lugar cuando termine de escribirse
            bool m_Committed;
            // Cerrar la lista nosotros (para mostrarla de nuevo o terminar) no es elegir
            bool m_Aborting;
            bool m_RefreshQueued;
            bool m_Ended;
            List<Candidate> m_Variables;
            List<Candidate> m_Members;
            readonly Dictionary<int, List<ArgumentOption>> m_Options = new Dictionary<int, List<ArgumentOption>>();

            Session(Plan plan)
            {
                m_Plan = plan;
                m_Editor = plan.Context.SyntaxEditor;
                m_Open = plan.Site.ArgsOffset - 1;
            }

            /// <summary>Si se está completando una llamada en el editor.</summary>
            public static bool IsActive(SyntaxEditor editor) => s_Active.ContainsKey(editor);

            public static void Start(Plan plan)
            {
                if (s_Active.TryGetValue(plan.Context.SyntaxEditor, out Session previous))
                    previous.End();
                new Session(plan).Begin();
            }

            IntelliPromptMemberList List => m_Editor.IntelliPrompt.MemberList;

            bool OurList => List.Visible && List.Context == this;

            int Caret => m_Editor.SelectedView.Selection.EndOffset;

            void SetCaret(int offset) => m_Editor.SelectedView.Selection.SelectRange(offset, 0);

            void Begin()
            {
                // Queda "Objeto.call()" con el cursor entre los paréntesis
                CallSite site = m_Plan.Site;
                Edit(() => Replace(m_Editor.Document, site.ArgsOffset, site.ArgsLength, site.HasClose ? string.Empty : ")"));
                SetCaret(site.ArgsOffset);

                s_Active[m_Editor] = this;
                m_Editor.KeyTyping += OnKeyTyping;
                m_Editor.TriggerActivated += OnTriggerActivated;
                m_Editor.SelectionChanged += OnSelectionChanged;
                m_Editor.DocumentTextChanged += OnTextChanged;
                m_Editor.IntelliPromptMemberListClosing += OnListClosing;
                m_Editor.IntelliPromptMemberListClosed += OnListClosed;
                m_Editor.LostFocus += OnLostFocus;
                m_Editor.Disposed += OnDisposed;

                Log.Write($"sugerencias para {m_Plan.Target.Name}: {m_Plan.Slots.Count} lugar(es)");
                m_Enter = true;
                Refresh();
                // Recorrer los SDT / BC es lo lento (carga sus estructuras): después de mostrar la primera lista
                Defer(LoadMembers);
            }

            /// <summary>Termina: cierra la ayuda y la lista, y después declara las variables y acomoda la salida.</summary>
            void End()
            {
                if (m_Ended)
                    return;
                m_Ended = true;
                if (s_Active.TryGetValue(m_Editor, out Session current) && current == this)
                    s_Active.Remove(m_Editor);
                m_Editor.KeyTyping -= OnKeyTyping;
                m_Editor.TriggerActivated -= OnTriggerActivated;
                m_Editor.SelectionChanged -= OnSelectionChanged;
                m_Editor.DocumentTextChanged -= OnTextChanged;
                m_Editor.IntelliPromptMemberListClosing -= OnListClosing;
                m_Editor.IntelliPromptMemberListClosed -= OnListClosed;
                m_Editor.LostFocus -= OnLostFocus;
                m_Editor.Disposed -= OnDisposed;
                if (m_Editor.IsDisposed)
                    return;

                IntelliPromptParameterInfo info = m_Editor.IntelliPrompt.ParameterInfo;
                if (info.Context == this)
                {
                    info.Hide();
                    info.Context = null;
                }
                AbortList();
                // Puede venir de una tecla a medio procesar: el texto se toca cuando el editor termine
                Defer(Finish);
            }

            // =====================================================================================
            // Eventos del editor
            // =====================================================================================

            void OnKeyTyping(object sender, KeyTypingEventArgs e) => Guard(() => KeyTyping(e));

            /// <summary>Antes que la lista y la ayuda de parámetros: decide qué hacen Enter, Tab, Esc, "," y ")".</summary>
            void KeyTyping(KeyTypingEventArgs e)
            {
                if (m_Ended)
                    return;
                Layout layout = Read();
                if (layout.Index < 0)
                    return;   // fuera de los paréntesis: lo termina Refresh
                if (e.KeyData == (Keys.Control | Keys.Return))
                {
                    e.Cancel = true;
                    AbortList();
                    FillRest(layout);
                    return;
                }
                if (layout.Nested)
                    return;   // dentro de otros paréntesis o de un string: como siempre

                IntelliPromptMemberList list = List;
                bool ours = OurList;
                switch (e.KeyData)
                {
                    case Keys.Escape:
                        if (!list.Visible)
                            End();   // si hay una lista, Esc solo la cierra
                        return;

                    case Keys.Return:
                    case Keys.Tab:
                        if (list.Visible && !ours)
                            return;   // la lista de GeneXus completa como siempre
                        if (ours && !list.IsSelectionVirtual && list.SelectedItem != null)
                            return;   // completa la lista y después se pasa al siguiente (OnListClosed)
                        // Sin elegir nada: queda lo escrito y se pasa al siguiente lugar
                        e.Cancel = true;
                        AbortList();
                        Next(layout);
                        return;
                }

                char c = e.KeyChar;
                if (c == '\0')
                    return;
                // Solo Enter / Tab eligen de la lista: con otro carácter (",", " ", "&", ".") lo escrito queda como está
                if (ours && !char.IsLetterOrDigit(c) && c != '_')
                    AbortList();
                if (c == ',')
                {
                    m_Enter = true;
                }
                else if (c == ')' && layout.Close >= 0 && Caret == layout.Close)
                {
                    // Sobre el ")" de la llamada: se sale de ella
                    e.Cancel = true;
                    SetCaret(layout.Close + 1);
                    End();
                }
            }

            void OnTriggerActivated(object sender, TriggerEventArgs e) => Guard(() =>
            {
                if (m_Ended || e.Trigger?.Key != VariablesTrigger)
                    return;
                TakeOverVariables();
                Defer(TakeOverVariables);   // por si GeneXus la abre después
            });

            void OnSelectionChanged(object sender, SelectionEventArgs e) => Guard(QueueRefresh);

            void OnTextChanged(object sender, DocumentModificationEventArgs e) => Guard(() =>
            {
                if (m_Ended || s_Applying || e.Modification == null)
                    return;
                DocumentModification modification = e.Modification;
                if (modification.StartOffset <= m_Open)
                {
                    if (modification.DeletionEndOffset <= m_Open)
                    {
                        m_Open += modification.LengthDelta;   // algo antes del "(": se corre
                        m_Moved = true;
                    }
                    else
                    {
                        m_Open = -1;   // se borró el "("
                    }
                }
                QueueRefresh();
            });

            void OnListClosing(object sender, CancelEventArgs e) => Guard(() =>
            {
                if (!m_Ended && !m_Aborting && !e.Cancel && List.Context == this)
                    m_Committed = true;
            });

            void OnListClosed(object sender, CancelEventArgs e) => Guard(() =>
            {
                if (!m_Committed)
                    return;
                m_Committed = false;
                Defer(AfterCommit);
            });

            void OnLostFocus(object sender, EventArgs e) => Defer(() =>
            {
                if (!m_Ended && !m_Editor.ContainsFocus && !List.Visible)
                    End();
            });

            void OnDisposed(object sender, EventArgs e) => End();

            // =====================================================================================
            // Lugares
            // =====================================================================================

            void QueueRefresh()
            {
                if (m_RefreshQueued || m_Ended)
                    return;
                m_RefreshQueued = true;
                Defer(Refresh);
            }

            /// <summary>Con el cursor fuera de los paréntesis termina; si no, actualiza la ayuda y entra al lugar al que se llegó.</summary>
            void Refresh()
            {
                m_RefreshQueued = false;
                if (m_Ended)
                    return;
                Layout layout = Read();
                if (layout.Index < 0)
                {
                    End();
                    return;
                }
                if (layout.Nested)
                    return;   // dentro de otra llamada: no se le discute la ayuda a GeneXus

                ShowInfo(Math.Min(layout.Index, m_Plan.Slots.Count - 1), layout);
                if (m_Enter)
                {
                    m_Enter = false;
                    if (layout.Index < m_Plan.Slots.Count)
                        Enter(layout);
                }
            }

            /// <summary>Un lugar vacío: lo fijo de los valores predefinidos va solo; si no, se abre la lista.</summary>
            void Enter(Layout layout)
            {
                int index = layout.Index;
                if (ArgumentText(layout, index).Trim().Length > 0)
                    return;
                Slot slot = m_Plan.Slots[index];
                if (slot.Fixed && slot.Preset.Values[0].Length > 0)
                {
                    Insert(Caret, slot.Preset.Values[0]);
                    Next(Read());
                    return;
                }
                ShowList(index, Caret, 0);
            }

            /// <summary>Se eligió de la lista: si el cursor quedó al final del argumento, al siguiente lugar.</summary>
            void AfterCommit()
            {
                if (m_Ended)
                    return;
                Layout layout = Read();
                if (layout.Index < 0 || layout.Nested)
                    return;
                int caret = Caret;
                if (Text(caret, layout.Ends[layout.Index] - caret).Trim().Length > 0)
                {
                    QueueRefresh();
                    return;
                }
                Next(layout);
            }

            /// <summary>Al siguiente lugar (agregando ", " si hace falta); en el último sale del ")" y termina.</summary>
            void Next(Layout layout)
            {
                int index = layout.Index;
                if (index < 0)
                    return;
                if (index + 1 < layout.Starts.Count)
                {
                    SetCaret(ArgumentStart(layout, index + 1));
                }
                else if (index + 1 < m_Plan.Slots.Count)
                {
                    Insert(ArgumentEnd(layout, index), ", ");
                }
                else
                {
                    if (layout.Close >= 0)
                        SetCaret(layout.Close + 1);
                    End();
                    return;
                }
                m_Enter = true;
                QueueRefresh();
            }

            /// <summary>Ctrl+Enter: los lugares vacíos desde el actual van con lo predefinido o las variables de la regla parm.</summary>
            void FillRest(Layout layout)
            {
                if (layout.Close < 0)
                {
                    End();
                    return;
                }
                var arguments = new List<string>();
                int count = Math.Max(m_Plan.Slots.Count, layout.Starts.Count);
                for (int i = 0; i < count; i++)
                {
                    string text = i < layout.Starts.Count ? ArgumentText(layout, i).Trim() : string.Empty;
                    if (text.Length == 0 && i >= layout.Index && i < m_Plan.Slots.Count)
                        text = m_Plan.Slots[i].Automatic;
                    arguments.Add(text);
                }
                string inside = string.Join(", ", arguments);
                Edit(() => Replace(m_Editor.Document, m_Open + 1, layout.Close - m_Open - 1, inside));
                SetCaret(m_Open + 1 + inside.Length + 1);
                End();
            }

            // =====================================================================================
            // Lista de sugerencias y ayuda de parámetros
            // =====================================================================================

            List<ArgumentOption> OptionsFor(int index)
            {
                if (!m_Options.TryGetValue(index, out List<ArgumentOption> options))
                {
                    var watch = Stopwatch.StartNew();
                    m_Variables = m_Variables ?? VariableCandidates(m_Plan.Context);
                    Slot slot = m_Plan.Slots[index];
                    options = SuggestionsFor(m_Plan.Context, m_Plan.Target, slot, m_Variables, m_Members);
                    m_Options[index] = options;
                    Log.Write($"parámetro {slot.Default}: {options.Count} sugerencia(s) en {watch.ElapsedMilliseconds} ms");
                }
                return options;
            }

            /// <summary>
            /// Muestra la lista del lugar, completando el texto de <paramref name="start"/> a <paramref name="start"/>
            /// + <paramref name="length"/>. Recién llegado, queda elegido lo predefinido (si no, nada: Enter no
            /// escribe una sugerencia que no se eligió).
            /// </summary>
            void ShowList(int index, int start, int length)
            {
                List<ArgumentOption> options = OptionsFor(index);
                if (options.Count == 0)
                    return;
                IntelliPromptMemberList list = List;
                if (list.Visible)
                {
                    m_Aborting = true;
                    try
                    {
                        list.Abort();
                    }
                    finally
                    {
                        m_Aborting = false;
                    }
                }

                list.Clear();
                int firstIcon = Icons.Register(list.ImageList);
                string proposed = m_Plan.Slots[index].Proposed;
                IntelliPromptMemberListItem selected = null;
                foreach (ArgumentOption option in options)
                {
                    var item = new IntelliPromptMemberListItem(option.Text, firstIcon < 0 ? -1 : firstIcon + (int)option.Kind, Description(option));
                    list.Add(item);
                    if (length == 0 && option.Text == proposed)
                        selected = item;
                }
                list.Context = this;
                list.Show(start, length);
                if (selected != null)
                    list.SelectedItem = selected;
            }

            void AbortList()
            {
                if (!OurList)
                    return;
                m_Aborting = true;
                try
                {
                    List.Abort();
                }
                finally
                {
                    m_Aborting = false;
                }
            }

            static string Description(ArgumentOption option) =>
                "<b>" + Escape(option.Text) + "</b>" + (string.IsNullOrEmpty(option.Detail) ? string.Empty : "<br/>" + Escape(option.Detail));

            /// <summary>
            /// Al escribir "&amp;" al principio de un lugar GeneXus abre la lista de todas las variables: se cambia por
            /// las sugerencias del lugar (si tiene variables para sugerir).
            /// </summary>
            void TakeOverVariables()
            {
                if (m_Ended || OurList)
                    return;
                Layout layout = Read();
                if (layout.Index < 0 || layout.Nested || layout.Index >= m_Plan.Slots.Count)
                    return;
                int start = ArgumentStart(layout, layout.Index);
                if (Caret != start + 1 || Text(start, 1) != "&")
                    return;
                if (!OptionsFor(layout.Index).Any(o => o.Text.StartsWith("&", StringComparison.Ordinal)))
                    return;
                ShowList(layout.Index, start, 1);
            }

            void LoadMembers()
            {
                if (m_Ended || m_Members != null)
                    return;
                var watch = Stopwatch.StartNew();
                m_Members = MemberCandidates(m_Plan.Context);
                Log.Write($"miembros de SDT / BC: {m_Members.Count} en {watch.ElapsedMilliseconds} ms");
                m_Options.Clear();   // se rearman con los miembros

                // La lista abierta se vuelve a mostrar con ellos
                if (!OurList)
                    return;
                Layout layout = Read();
                if (layout.Index < 0 || layout.Nested || layout.Index >= m_Plan.Slots.Count)
                    return;
                int start = ArgumentStart(layout, layout.Index);
                ShowList(layout.Index, start, Math.Max(0, Caret - start));
            }

            /// <summary>La firma con el lugar actual en negrita y, abajo, su tipo.</summary>
            void ShowInfo(int index, Layout layout)
            {
                IntelliPromptParameterInfo info = m_Editor.IntelliPrompt.ParameterInfo;
                string markup = InfoMarkup(index);
                if (info.Visible && info.Context == this)
                {
                    if (info.Info.Count != 1 || info.Info[0] != markup)
                    {
                        info.Info.Clear();
                        info.Info.Add(markup);
                        info.MeasureAndResize(m_Editor.PointToScreen(System.Drawing.Point.Empty));
                    }
                    return;
                }
                if (layout.Close < 0)
                    return;

                info.Hide();
                info.Info.Clear();
                info.Info.Add(markup);
                info.SelectedIndex = 0;
                info.Context = this;
                // Desde adentro del "(" hasta el ")": con el cursor fuera Actipro la cierra
                info.ValidTextRange = new TextRange(m_Open + 1, layout.Close + 1);
                CallSite site = m_Plan.Site;
                info.Show(m_Open - (site.ArgsOffset - 1 - site.NameOffset));
            }

            string InfoMarkup(int index)
            {
                List<Slot> slots = m_Plan.Slots;
                var text = new StringBuilder();
                if (m_Plan.AssignedName != null)
                    text.Append(Escape("&" + m_Plan.AssignedName + " = "));
                text.Append(Escape(m_Plan.CallText + "("));
                for (int i = 0; i < slots.Count; i++)
                {
                    if (i > 0)
                        text.Append(", ");
                    string label = Escape((slots[i].IsReturn ? "out" : AccessText(slots[i].Parameter.Accessor)) + ":" + slots[i].Default);
                    text.Append(i == index ? "<b>" + label + "</b>" : label);
                }
                text.Append(")");

                Slot slot = slots[index];
                string type = slot.Parameter.Object is ITypedObject typed ? TypeText(typed) : string.Empty;
                string detail = slot.Default.TrimStart('&') + (type.Length > 0 ? ": " + type : string.Empty);
                if (slot.IsReturn)
                    detail += " · con una &variable queda &variable = " + m_Plan.Site.Name + ".udp(...)";
                return text.Append("<br/>").Append(Escape(detail)).ToString();
            }

            static string Escape(string text) => IntelliPrompt.EscapeMarkupText(text);

            // =====================================================================================
            // Al terminar
            // =====================================================================================

            /// <summary>
            /// Declara las &amp;variables escritas (y la de "&amp;X = " ya escrita) con el tipo de su parámetro. El
            /// valor devuelto escrito como último argumento sale de los paréntesis: &amp;X = Objeto.udp(...); vacío se saca.
            /// </summary>
            void Finish()
            {
                if (m_Open < 0 || m_Editor.IsDisposed)
                {
                    Log.Write($"sugerencias para {m_Plan.Target.Name}: se perdió la llamada");
                    return;
                }
                Layout layout = Read();
                Context context = m_Plan.Context;
                List<Slot> slots = m_Plan.Slots;
                int count = Math.Min(slots.Count, layout.Starts.Count);
                List<string> arguments = Enumerable.Range(0, count).Select(i => ArgumentText(layout, i).Trim()).ToList();

                var declarations = new Declarations();
                for (int i = 0; i < count; i++)
                {
                    Match variable = VariableRegex.Match(arguments[i]);
                    if (variable.Success)
                        declarations.Track(variable.Groups["name"].Value, Declare(context.Part, variable.Groups["name"].Value, slots[i].Parameter.Object));
                }
                if (m_Plan.AssignedName != null)
                    declarations.Track(m_Plan.AssignedName, Declare(context.Part, m_Plan.AssignedName, m_Plan.Returned.Object));

                string output = "-";
                if (slots[slots.Count - 1].IsReturn && count == slots.Count && layout.Close >= 0 && !m_Moved)
                {
                    string value = arguments[count - 1];
                    if (value.Length == 0 || VariablePathRegex.IsMatch(value))
                    {
                        MoveReturn(layout, count - 1, value);
                        output = value.Length > 0 ? value : "vacía";
                    }
                }
                if (declarations.Declared.Count > 0)
                    MarkModified(context.Part);

                Log.Write($"completado {m_Plan.Target.Name}: ({string.Join(", ", arguments)}) salida={output}");
                Status(Summary(m_Plan.Target, m_Plan.ParameterCount, declarations));
            }

            /// <summary>Saca el último argumento (y su coma) y, si es una variable, la pone como "&amp;X = " (call pasa a udp).</summary>
            void MoveReturn(Layout layout, int index, string value)
            {
                CallSite site = m_Plan.Site;
                Document document = m_Editor.Document;
                int from = index > 0 ? layout.Ends[index - 1] : layout.Starts[index];
                int to = layout.Ends[index];
                s_Applying = true;
                document.UndoRedo.StartGroup(DocumentModificationType.Custom);
                try
                {
                    // De derecha a izquierda, así los offsets de lo anterior siguen valiendo
                    Replace(document, from, to - from, string.Empty);
                    if (value.Length > 0)
                    {
                        if (site.Kind == CallKind.Call)
                            Replace(document, site.MethodOffset, site.MethodLength, UdpMethod);
                        Replace(document, site.NameOffset, 0, value + " = ");
                    }
                }
                finally
                {
                    document.UndoRedo.EndGroup();
                    s_Applying = false;
                }
            }

            // =====================================================================================
            // Texto de la llamada
            // =====================================================================================

            /// <summary>Los argumentos de la llamada, separados por las comas de afuera (no las de otros paréntesis ni strings).</summary>
            sealed class Layout
            {
                /// <summary>El ")" que cierra la llamada; -1 si no está en la línea.</summary>
                public int Close = -1;
                public readonly List<int> Starts = new List<int>();
                public readonly List<int> Ends = new List<int>();
                /// <summary>El argumento donde está el cursor; -1 si está fuera de los paréntesis.</summary>
                public int Index = -1;
                /// <summary>El cursor está dentro de otros paréntesis o de un string.</summary>
                public bool Nested;
            }

            Layout Read()
            {
                var layout = new Layout();
                Document document = m_Editor.Document;
                if (m_Open < 0 || m_Open >= document.Length || document.GetSubstring(m_Open, 1) != "(")
                    return layout;
                DocumentLine line = document.Lines[document.OffsetToPosition(m_Open).Line];
                string text = document.GetSubstring(new TextRange(m_Open, line.EndOffset), LineTerminator.Newline);
                int caret = Caret;

                int start = m_Open + 1;
                int end = m_Open + text.Length;
                int depth = 0;
                char quote = '\0';
                for (int i = 1; i <= text.Length; i++)
                {
                    int offset = m_Open + i;
                    if (offset == caret)
                    {
                        layout.Index = layout.Starts.Count;
                        layout.Nested = depth > 0 || quote != '\0';
                    }
                    if (i == text.Length)
                        break;

                    char c = text[i];
                    if (quote != '\0')
                    {
                        if (c == quote)
                            quote = '\0';
                        continue;
                    }
                    if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                    {
                        end = offset;   // comentario hasta el final de la línea
                        break;
                    }
                    switch (c)
                    {
                        case '"':
                        case '\'':
                            quote = c;
                            break;
                        case '(':
                        case '[':
                            depth++;
                            break;
                        case ']':
                            if (depth > 0)
                                depth--;
                            break;
                        case ')':
                            if (depth > 0)
                            {
                                depth--;
                                break;
                            }
                            layout.Starts.Add(start);
                            layout.Ends.Add(offset);
                            layout.Close = offset;
                            return layout;
                        case ',':
                            if (depth == 0)
                            {
                                layout.Starts.Add(start);
                                layout.Ends.Add(offset);
                                start = offset + 1;
                            }
                            break;
                    }
                }
                // Sin ")" en la línea: el último argumento va hasta el final
                layout.Starts.Add(start);
                layout.Ends.Add(Math.Max(start, end));
                return layout;
            }

            string Text(int offset, int length) => length > 0 ? m_Editor.Document.GetSubstring(offset, length) : string.Empty;

            string ArgumentText(Layout layout, int index) => Text(layout.Starts[index], layout.Ends[index] - layout.Starts[index]);

            /// <summary>Donde empieza lo escrito en el argumento (después de los espacios).</summary>
            int ArgumentStart(Layout layout, int index)
            {
                string text = ArgumentText(layout, index);
                return layout.Starts[index] + (text.Length - text.TrimStart(' ', '\t').Length);
            }

            /// <summary>Donde termina lo escrito en el argumento (antes de los espacios).</summary>
            int ArgumentEnd(Layout layout, int index)
            {
                string text = ArgumentText(layout, index);
                return layout.Ends[index] - (text.Length - text.TrimEnd(' ', '\t').Length);
            }

            /// <summary>Escribe <paramref name="text"/> y deja el cursor después.</summary>
            void Insert(int offset, string text)
            {
                Edit(() => Replace(m_Editor.Document, offset, 0, text));
                SetCaret(offset + text.Length);
            }

            /// <summary>Lo que se escribe acá no es del usuario: no dispara otra llamada ni corre el "(".</summary>
            static void Edit(Action action)
            {
                s_Applying = true;
                try
                {
                    action();
                }
                finally
                {
                    s_Applying = false;
                }
            }

            void Defer(Action action)
            {
                if (!m_Editor.IsDisposed && m_Editor.IsHandleCreated)
                    m_Editor.BeginInvoke((Action)(() => Guard(action)));
            }
        }
    }
}
