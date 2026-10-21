using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GxQuickVar
{
    enum PopupOutcome { Accepted, Skipped, Stopped }

    sealed class RecentType
    {
        public string Text;
        public TypeEntry Entry;
    }

    sealed class PopupOptions
    {
        public string VariableName;
        public bool AlreadyDeclared;
        public string InitialText = string.Empty;
        /// <summary>Por qué se propone el texto inicial ("lo usaste antes para &amp;X"...), o null.</summary>
        public string Hint;
        public List<TypeEntry> Types;
        public List<RecentType> Recents = new List<RecentType>();
        /// <summary>Módulo del objeto que se está editando ("" = raíz).</summary>
        public string ModulePath = string.Empty;
        /// <summary>SDTs de ese módulo y de los superiores, ordenados por cercanía.</summary>
        public List<TypeEntry> ModuleTypes = new List<TypeEntry>();
        /// <summary>Aplica el tipo; devuelve el mensaje de error o null.</summary>
        public Func<string, string> Apply;
        public Func<TypeEntry, string> Describe;
        /// <summary>Todos los atributos, para buscarlos como los tipos ("att cli nom"); null si no se pueden listar.</summary>
        public Func<List<TypeEntry>> AllAttributes = () => null;
        /// <summary>Si no se pueden listar todos: los que empiezan con el texto ("att Rec…").</summary>
        public Func<string, List<TypeEntry>> FindAttributes = prefix => new List<TypeEntry>();
        public int BatchIndex;
        public int BatchCount;
        public Palette Palette;
        public Font CodeFont;

        public bool InBatch => BatchCount > 0;
    }

    /// <summary>
    /// Mini desplegable junto al cursor: se escribe el tipo, la lista filtra.
    ///   Tab        completa con el tipo seleccionado (y sus parámetros por defecto)
    ///   Enter      acepta (si el tipo lleva parámetros y no los escribiste, primero completa)
    ///   ↑ ↓ RePág AvPág  mueven la selección
    ///   Esc        cancela (en "declarar todas": saltea esta variable)
    ///   Shift+Esc  termina "declarar todas"
    /// </summary>
    sealed class TypePopup : Form
    {
        const int MaxShown = 300;
        const int MaxModuleShown = 40;
        const int MinAttributeLetters = 3;
        const int ExactBonus = 5000;
        const int EM_SETCUEBANNER = 0x1501;

        sealed class Row
        {
            public string Header;
            public TypeEntry Entry;
            /// <summary>Lo que se escribe al elegir la fila.</summary>
            public string Text;
            public bool Recent;
            /// <summary>Posiciones del nombre que coinciden con lo escrito, o null.</summary>
            public int[] Highlights;
            /// <summary>Posiciones del módulo que coinciden con lo escrito, o null.</summary>
            public int[] ModuleHighlights;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        readonly PopupOptions m_Options;
        readonly Palette m_Palette;
        readonly float m_Scale;

        readonly Font m_UiFont;
        readonly Font m_SmallFont;
        readonly Font m_SmallBoldFont;
        readonly Font m_BadgeFont;
        readonly Font m_CodeFont;
        readonly Font m_CodeBoldFont;

        readonly Panel m_Header;
        readonly Panel m_Search;
        readonly TextBox m_Text;
        readonly ListBox m_List;
        readonly Panel m_Details;
        readonly Panel m_Footer;

        string m_Filter = string.Empty;
        /// <summary>Segmentos de los módulos de la KB, para reconocer "sdt ventas".</summary>
        readonly HashSet<string> m_ModuleSegments;
        /// <summary>Todos los atributos (se piden la primera vez que se escribe "att "), o null si no se pudieron listar.</summary>
        List<TypeEntry> m_Attributes;
        HashSet<string> m_AttributeModuleSegments;
        bool m_AttributesLoaded;
        /// <summary>Sin la lista de atributos, se buscan por "empieza con" (a partir de 3 letras).</summary>
        bool m_AttributesByPrefix;
        string m_Error;
        bool m_Closing;

        public PopupOutcome Outcome { get; private set; } = PopupOutcome.Stopped;

        public TypePopup(PopupOptions options)
        {
            m_Options = options;
            m_Palette = options.Palette;
            m_ModuleSegments = TypeMatcher.ModuleSegments(options.Types.Select(t => t.Module));
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                m_Scale = g.DpiX / 96f;

            m_UiFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 9f);
            m_SmallFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 8f);
            m_SmallBoldFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 8f, FontStyle.Bold);
            m_BadgeFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 7f, FontStyle.Bold);
            m_CodeFont = new Font(options.CodeFont.FontFamily, 10f);
            m_CodeBoldFont = new Font(options.CodeFont.FontFamily, 10f, FontStyle.Bold);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Font = m_UiFont;
            BackColor = m_Palette.Border;
            Padding = new Padding(1);
            Size = new Size(S(460), S(options.Hint != null ? 384 : 368));

            m_Header = new BufferedPanel { Dock = DockStyle.Top, Height = S(options.Hint != null ? 50 : 34), BackColor = m_Palette.Back };
            m_Header.Paint += PaintHeader;

            m_Search = new BufferedPanel { Dock = DockStyle.Top, Height = S(38), BackColor = m_Palette.Back };
            m_Search.Paint += PaintSearch;
            m_Text = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = m_Palette.InputBack,
                ForeColor = m_Palette.Text,
                Font = m_CodeFont,
                Text = options.InitialText,
            };
            m_Text.TextChanged += (s, e) => { m_Error = null; Refilter(); };
            m_Text.GotFocus += (s, e) => m_Search.Invalidate();
            m_Text.LostFocus += (s, e) => m_Search.Invalidate();
            m_Search.Controls.Add(m_Text);
            m_Search.Resize += (s, e) => LayoutSearchBox();

            m_List = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                BorderStyle = BorderStyle.None,
                BackColor = m_Palette.Back,
                ItemHeight = S(26),
                TabStop = false,
            };
            m_List.DrawItem += DrawRow;
            m_List.SelectedIndexChanged += (s, e) => { SkipHeaderSelection(); m_Details.Invalidate(); };
            m_List.DoubleClick += (s, e) => AcceptOrComplete();
            m_List.MouseDown += (s, e) => BeginInvoke((Action)(() => m_Text.Focus()));
            if (m_Palette.Dark)
                m_List.HandleCreated += (s, e) => SetWindowTheme(m_List.Handle, "DarkMode_Explorer", null);

            m_Details = new BufferedPanel { Dock = DockStyle.Bottom, Height = S(26), BackColor = m_Palette.Back };
            m_Details.Paint += PaintDetails;

            m_Footer = new BufferedPanel { Dock = DockStyle.Bottom, Height = S(30), BackColor = m_Palette.Back };
            m_Footer.Paint += PaintFooter;

            // El orden importa para el docking: lo agregado último se acomoda primero (más afuera)
            Controls.Add(m_List);
            Controls.Add(m_Search);
            Controls.Add(m_Header);
            Controls.Add(m_Details);
            Controls.Add(m_Footer);

            Deactivate += (s, e) => Close(PopupOutcome.Stopped);
            Refilter();
        }

        int S(int value) => (int)Math.Round(value * m_Scale);

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x00020000;
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // En Windows 11 el borde y las esquinas redondeadas los dibuja el sistema
            if (Dwm.TryRoundCorners(Handle, m_Palette.Border))
            {
                Padding = Padding.Empty;
                BackColor = m_Palette.Back;
            }
        }

        // Barra de scroll oscura nativa (Windows 10 1809+); en versiones anteriores no hace nada
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SendMessage(m_Text.Handle, EM_SETCUEBANNER, (IntPtr)1, "VarChar(100), sdtcli ventas, att cli nom");
            m_Text.Focus();
            m_Text.SelectAll();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_UiFont.Dispose();
                m_SmallFont.Dispose();
                m_SmallBoldFont.Dispose();
                m_BadgeFont.Dispose();
                m_CodeFont.Dispose();
                m_CodeBoldFont.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                    Close(m_Options.InBatch ? PopupOutcome.Skipped : PopupOutcome.Stopped);
                    return true;
                case Keys.Shift | Keys.Escape:
                    Close(PopupOutcome.Stopped);
                    return true;
                case Keys.Enter:
                    AcceptOrComplete();
                    return true;
                case Keys.Tab:
                    if (SelectedRow != null) Complete(SelectedRow);
                    return true;
                case Keys.Down: MoveSelection(1); return true;
                case Keys.Up: MoveSelection(-1); return true;
                case Keys.PageDown: MoveSelection(VisibleRows); return true;
                case Keys.PageUp: MoveSelection(-VisibleRows); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- Lista ----

        Row SelectedRow => (m_List.SelectedItem as Row)?.Entry != null ? (Row)m_List.SelectedItem : null;

        int VisibleRows => Math.Max(1, m_List.ClientSize.Height / m_List.ItemHeight - 1);

        bool IsHeader(int index) => ((Row)m_List.Items[index]).Header != null;

        void MoveSelection(int delta)
        {
            int count = m_List.Items.Count;
            if (count == 0)
                return;
            int step = delta > 0 ? 1 : -1;
            int target = Math.Max(0, Math.Min(count - 1, m_List.SelectedIndex + delta));
            while (target >= 0 && target < count && IsHeader(target))
                target += step;
            if (target < 0 || target >= count)
                return;
            m_List.SelectedIndex = target;
        }

        void SkipHeaderSelection()
        {
            int index = m_List.SelectedIndex;
            if (index < 0 || !IsHeader(index))
                return;
            if (index + 1 < m_List.Items.Count)
                m_List.SelectedIndex = index + 1;
        }

        string ModuleHeader => m_Options.ModulePath.Length > 0
            ? $"SDTs DE {m_Options.ModulePath} Y SUPERIORES"
            : "SDTs DE LA RAÍZ";

        void Refilter()
        {
            string text = m_Text.Text.Trim();
            if (TypeSyntax.TryParseAttribute(m_Text.Text, out string attributePrefix))
            {
                RefilterAttributes(attributePrefix);
                return;
            }
            m_Filter = TypeSyntax.BaseName(text, out _);
            TypeQuery query = TypeQuery.Parse(m_Filter, m_ModuleSegments);
            var scores = new Dictionary<TypeEntry, TypeScore>();
            TypeScore ScoreOf(TypeEntry entry)
            {
                if (!scores.TryGetValue(entry, out TypeScore score))
                {
                    score = TypeMatcher.Evaluate(query, entry.ObjectName, entry.Module, entry.Group);
                    // Lo escrito es exactamente este tipo (p.ej. el tipo actual de la variable): primero
                    if (score != null && entry.IsNamed(m_Filter))
                        score.Score += ExactBonus;
                    scores[entry] = score;
                }
                return score;
            }
            Row MakeRow(TypeEntry entry, string rowText, bool recent)
            {
                TypeScore score = query.IsEmpty ? null : ScoreOf(entry);
                return new Row { Entry = entry, Text = rowText, Recent = recent, Highlights = score?.Highlights, ModuleHighlights = score?.ModuleHighlights };
            }

            var rows = new List<Row>();

            // Arriba solo lo que coincide de verdad (sin letras sueltas), en su orden
            var recents = query.IsEmpty
                ? m_Options.Recents
                : m_Options.Recents.Where(r => ScoreOf(r.Entry)?.Strong == true || r.Text.StartsWith(m_Filter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (recents.Count > 0)
            {
                rows.Add(new Row { Header = "RECIENTES" });
                rows.AddRange(recents.Select(r => MakeRow(r.Entry, r.Text, true)));
            }

            // SDTs del módulo del objeto y superiores (ya vienen ordenados por cercanía)
            var moduleTypes = (query.IsEmpty
                    ? m_Options.ModuleTypes
                    : m_Options.ModuleTypes.Where(t => ScoreOf(t)?.Strong == true).OrderByDescending(t => ScoreOf(t).Score).AsEnumerable())
                .Take(MaxModuleShown)
                .ToList();
            if (moduleTypes.Count > 0)
            {
                rows.Add(new Row { Header = ModuleHeader });
                rows.AddRange(moduleTypes.Select(t => MakeRow(t, t.Name, false)));
            }

            if (rows.Count > 0)
                rows.Add(new Row { Header = query.IsEmpty ? "TODOS LOS TIPOS" : "OTRAS COINCIDENCIAS" });

            IEnumerable<TypeEntry> matches;
            if (query.IsEmpty)
            {
                matches = m_Options.Types;
            }
            else
            {
                // Los del módulo ya se mostraron arriba; OrderBy es estable: a igual puntaje, el orden de GeneXus
                var shown = new HashSet<TypeEntry>(moduleTypes);
                matches = m_Options.Types
                    .Where(t => !shown.Contains(t) && ScoreOf(t) != null)
                    .OrderByDescending(t => ScoreOf(t).Score);
            }
            rows.AddRange(matches.Take(MaxShown).Select(t => MakeRow(t, t.Name, false)));

            // Preferir la fila que coincide exactamente con lo escrito (p.ej. el tipo actual de la variable)
            ShowRows(rows, rows.FindIndex(r => r.Entry != null &&
                (string.Equals(r.Text, text, StringComparison.OrdinalIgnoreCase) || (!r.Recent && r.Entry.IsNamed(m_Filter)))));
        }

        /// <summary>
        /// "att cli nom": los atributos, con la misma búsqueda que los tipos (letras en orden, varias
        /// palabras, módulo). Sin texto, todos por orden alfabético.
        /// </summary>
        void RefilterAttributes(string search)
        {
            m_Filter = search;
            if (!m_AttributesLoaded)
            {
                m_AttributesLoaded = true;
                try
                {
                    m_Attributes = m_Options.AllAttributes();
                }
                catch (Exception)
                {
                    m_Attributes = null;
                }
                if (m_Attributes != null)
                    m_AttributeModuleSegments = TypeMatcher.ModuleSegments(m_Attributes.Select(a => a.Module));
            }
            m_AttributesByPrefix = m_Attributes == null;
            if (m_AttributesByPrefix)
            {
                RefilterAttributesByPrefix(search);
                return;
            }

            TypeQuery query = TypeQuery.Parse(search, m_AttributeModuleSegments, groupCodes: false);
            List<Row> found;
            if (query.IsEmpty)
            {
                found = m_Attributes.Take(MaxShown).Select(e => AttributeRow(e, null)).ToList();
            }
            else
            {
                // La lista viene por nombre y OrderBy es estable: a igual puntaje, primero los más cortos y después alfabético
                found = m_Attributes
                    .Select(e => new { Entry = e, Score = TypeMatcher.Evaluate(query, e.ObjectName, e.Module, e.Group) })
                    .Where(m => m.Score != null)
                    .OrderByDescending(m => m.Score.Score)
                    .ThenBy(m => m.Entry.ObjectName.Length)
                    .Take(MaxShown)
                    .Select(m => AttributeRow(m.Entry, m.Score))
                    .ToList();
            }

            var rows = new List<Row> { new Row { Header = found.Count > 0 ? "ATRIBUTOS" : "NINGÚN ATRIBUTO COINCIDE CON " + search.ToUpperInvariant() } };
            rows.AddRange(found);
            ShowRows(rows, rows.FindIndex(r => r.Entry != null && string.Equals(r.Entry.ObjectName, search, StringComparison.OrdinalIgnoreCase)));
        }

        static Row AttributeRow(TypeEntry entry, TypeScore score) => new Row
        {
            Entry = entry,
            Text = TypeSyntax.AttributePrefix + entry.ObjectName,
            Highlights = score?.Highlights,
            ModuleHighlights = score?.ModuleHighlights,
        };

        /// <summary>Si no se pudieron listar los atributos: los que empiezan con lo escrito, a partir de 3 letras.</summary>
        void RefilterAttributesByPrefix(string search)
        {
            string prefix = search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            var rows = new List<Row>();
            if (prefix.Length < MinAttributeLetters)
            {
                rows.Add(new Row { Header = $"ATRIBUTOS · ESCRIBÍ {MinAttributeLetters} LETRAS O MÁS" });
            }
            else
            {
                List<TypeEntry> found;
                try
                {
                    found = m_Options.FindAttributes(prefix);
                }
                catch (Exception)
                {
                    found = new List<TypeEntry>();
                }
                rows.Add(new Row { Header = found.Count > 0 ? "ATRIBUTOS" : "NINGÚN ATRIBUTO EMPIEZA CON " + prefix.ToUpperInvariant() });
                int[] highlights = Enumerable.Range(0, prefix.Length).ToArray();
                rows.AddRange(found.Select(e => new Row { Entry = e, Text = TypeSyntax.AttributePrefix + e.ObjectName, Highlights = highlights }));
            }
            ShowRows(rows, rows.FindIndex(r => r.Entry != null && string.Equals(r.Entry.ObjectName, prefix, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Carga las filas y selecciona <paramref name="preferred"/> (o la primera seleccionable).</summary>
        void ShowRows(List<Row> rows, int preferred)
        {
            m_List.BeginUpdate();
            m_List.Items.Clear();
            m_List.Items.AddRange(rows.Cast<object>().ToArray());
            int selected = preferred >= 0 ? preferred : rows.FindIndex(r => r.Entry != null);
            if (selected >= 0)
                m_List.SelectedIndex = selected;
            m_List.EndUpdate();
            m_Details.Invalidate();
        }

        // ---- Completar / aceptar ----

        /// <summary>Reemplaza lo escrito por la fila elegida, conservando el sufijo de colección.</summary>
        void Complete(Row row)
        {
            TypeSyntax.BaseName(m_Text.Text, out string suffix);
            if (row.Recent || row.Entry.Caps == null)   // recientes y atributos: el texto ya está completo
            {
                string text = row.Text;
                TypeSyntax.BaseName(text, out string rowSuffix);
                m_Text.Text = rowSuffix.Length == 0 ? text + suffix : text;
                // "VarChar(100)", "Numeric(10.2)": queda seleccionado lo de adentro para pisarlo escribiendo
                int open = m_Text.Text.IndexOf('(');
                int close = open >= 0 ? m_Text.Text.IndexOf(')', open) : -1;
                if (close > open + 1 && TypeCatalog.TakesParameters(row.Entry.Group))
                    m_Text.Select(open + 1, close - open - 1);
                else
                    m_Text.Select(m_Text.Text.Length, 0);
                return;
            }

            TypeEntry entry = row.Entry;
            if (entry.DefaultParameters.Length > 0)
            {
                m_Text.Text = $"{entry.Name}({entry.DefaultParameters}){suffix}";
                m_Text.Select(entry.Name.Length + 1, entry.DefaultParameters.Length);
            }
            else
            {
                m_Text.Text = entry.Name + suffix;
                m_Text.Select(m_Text.Text.Length, 0);
            }
        }

        void AcceptOrComplete()
        {
            string text = m_Text.Text.Trim();
            Row row = SelectedRow;
            if (row != null && !TypeSyntax.HasParameters(text))
            {
                string typed = TypeSyntax.TryParseAttribute(text, out string attributeName)
                    ? attributeName
                    : TypeSyntax.BaseName(text, out _);
                if (row.Recent)
                {
                    if (!string.Equals(text, row.Text, StringComparison.OrdinalIgnoreCase))
                    {
                        Complete(row);
                        text = m_Text.Text;
                    }
                }
                else
                {
                    bool exact = row.Entry.IsNamed(typed)
                        || string.Equals(typed, row.Entry.ObjectName, StringComparison.OrdinalIgnoreCase);
                    if (!exact || row.Entry.DefaultParameters.Length > 0)
                    {
                        Complete(row);
                        // Si lleva parámetros, queda seleccionado el valor por defecto para pisarlo
                        if (row.Entry.DefaultParameters.Length > 0)
                            return;
                        text = m_Text.Text;
                    }
                }
            }

            string error;
            try
            {
                error = m_Options.Apply(text);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            if (error != null)
            {
                m_Error = error;
                m_Details.Invalidate();
                return;
            }
            Close(PopupOutcome.Accepted);
        }

        void Close(PopupOutcome outcome)
        {
            if (m_Closing)
                return;
            m_Closing = true;
            Outcome = outcome;
            DialogResult = outcome == PopupOutcome.Accepted ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }

        // ---- Dibujo ----

        void PaintHeader(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int x = S(12);
            int lineHeight = S(34);
            var line = new Rectangle(x, 0, m_Header.Width - 2 * x, lineHeight);

            string verb = m_Options.AlreadyDeclared ? "Cambiar tipo de" : "Declarar";
            Size verbSize = TextRenderer.MeasureText(g, verb, m_UiFont, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, verb, m_UiFont, line, m_Palette.Gray, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var nameRect = new Rectangle(x + verbSize.Width + S(6), 0, line.Width - verbSize.Width - S(80), lineHeight);
            TextRenderer.DrawText(g, "&" + m_Options.VariableName, m_CodeBoldFont, nameRect, m_Palette.Accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            if (m_Options.InBatch)
                TextRenderer.DrawText(g, $"{m_Options.BatchIndex} de {m_Options.BatchCount}", m_SmallFont, line, m_Palette.Gray,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (m_Options.Hint != null)
            {
                var hintRect = new Rectangle(x, lineHeight - S(8), line.Width, S(20));
                TextRenderer.DrawText(g, m_Options.Hint, m_SmallFont, hintRect, m_Palette.Gray,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        Rectangle SearchFrame => new Rectangle(S(10), S(3), m_Search.Width - S(20), m_Search.Height - S(9));

        void LayoutSearchBox()
        {
            Rectangle frame = SearchFrame;
            int height = m_Text.PreferredHeight;
            m_Text.SetBounds(frame.X + S(8), frame.Y + (frame.Height - height) / 2 + 1, frame.Width - S(16), height);
        }

        void PaintSearch(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle frame = SearchFrame;
            using (GraphicsPath path = Drawing.RoundRect(frame, S(5)))
            using (var fill = new SolidBrush(m_Palette.InputBack))
            using (var pen = new Pen(m_Text.Focused ? m_Palette.Accent : m_Palette.ChipBorder, m_Text.Focused ? 1.5f * m_Scale : 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
        }

        void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;
            Graphics g = e.Graphics;
            var row = (Row)m_List.Items[e.Index];
            Rectangle r = e.Bounds;

            if (row.Header != null)
            {
                using (var back = new SolidBrush(m_Palette.Back))
                    g.FillRectangle(back, r);
                var headerRect = new Rectangle(r.X + S(12), r.Y + S(4), r.Width - S(24), r.Height - S(4));
                TextRenderer.DrawText(g, row.Header, m_BadgeFont, headerRect, m_Palette.Gray,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }

            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(selected ? m_Palette.Selection : m_Palette.Back))
                g.FillRectangle(back, r);
            if (selected)
                using (var bar = new SolidBrush(m_Palette.SelectionBar))
                    g.FillRectangle(bar, r.X, r.Y + S(3), S(3), r.Height - S(6));

            // Etiqueta de categoría
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var badge = new Rectangle(r.X + S(12), r.Y + (r.Height - S(16)) / 2, S(34), S(16));
            m_Palette.Badge(row.Entry.Group, out Color badgeBack, out Color badgeFore);
            using (GraphicsPath path = Drawing.RoundRect(badge, S(4)))
            using (var brush = new SolidBrush(badgeBack))
                g.FillPath(brush, path);
            TextRenderer.DrawText(g, TypeCatalog.BadgeText(row.Entry.Group), m_BadgeFont, badge, badgeFore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // Texto secundario a la derecha (módulo, con lo que coincide resaltado / parámetros por defecto)
            row.Entry.EnsureResolved();
            SplitDisplay(row, out string main, out string secondary, out int[] secondaryHighlights);
            int right = r.Right - S(12);
            if (secondary.Length > 0)
            {
                Color secondaryColor = selected ? m_Palette.Text : m_Palette.Gray;
                int fullWidth = MeasureHighlighted(g, secondary, secondaryHighlights, m_SmallFont, m_SmallBoldFont);
                int width = Math.Min(fullWidth, r.Width / 2);
                var secondaryRect = new Rectangle(right - width, r.Y, width, r.Height);
                if (secondaryHighlights != null && fullWidth <= width)
                    DrawHighlighted(g, secondary, secondaryHighlights, secondaryRect, m_SmallFont, m_SmallBoldFont, secondaryColor);
                else
                    TextRenderer.DrawText(g, secondary, m_SmallFont, secondaryRect, secondaryColor,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                right -= width + S(12);
            }

            var mainRect = new Rectangle(badge.Right + S(10), r.Y, right - badge.Right - S(10), r.Height);
            DrawHighlighted(g, main, row.Highlights, mainRect, m_CodeFont, m_CodeBoldFont, m_Palette.Text);
        }

        static void SplitDisplay(Row row, out string main, out string secondary, out int[] secondaryHighlights)
        {
            secondaryHighlights = null;
            if (row.Recent)
            {
                int comma = row.Text.IndexOf(", ", StringComparison.Ordinal);
                main = comma >= 0 ? row.Text.Substring(0, comma) : row.Text;
                secondary = comma >= 0 ? row.Text.Substring(comma + 2) : string.Empty;
                return;
            }
            main = row.Entry.ObjectName;
            if (row.Entry.Secondary != null)
                secondary = row.Entry.Secondary;
            else if (row.Entry.Module.Length > 0)
            {
                secondary = row.Entry.Module;
                secondaryHighlights = row.ModuleHighlights;
            }
            else if (row.Entry.DefaultParameters.Length > 0)
                secondary = "(" + row.Entry.DefaultParameters + ")";
            else
                secondary = row.Entry.Group == TypeGroup.Sdt || row.Entry.Group == TypeGroup.Bc ? "raíz" : string.Empty;
        }

        const TextFormatFlags HighlightFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        /// <summary>Cada tramo del texto y si va resaltado.</summary>
        static IEnumerable<KeyValuePair<string, bool>> HighlightSegments(string text, int[] highlights)
        {
            var marks = new bool[text.Length];
            if (highlights != null)
                foreach (int p in highlights)
                    if (p >= 0 && p < marks.Length)
                        marks[p] = true;

            int start = 0;
            while (start < text.Length)
            {
                bool on = marks[start];
                int end = start;
                while (end < text.Length && marks[end] == on)
                    end++;
                yield return new KeyValuePair<string, bool>(text.Substring(start, end - start), on);
                start = end;
            }
        }

        /// <summary>Ancho del texto dibujado con <see cref="DrawHighlighted"/>.</summary>
        static int MeasureHighlighted(Graphics g, string text, int[] highlights, Font normalFont, Font boldFont)
        {
            if (highlights == null || highlights.Length == 0)
                return TextRenderer.MeasureText(g, text, normalFont, Size.Empty, HighlightFlags).Width;
            return HighlightSegments(text, highlights)
                .Sum(s => TextRenderer.MeasureText(g, s.Key, s.Value ? boldFont : normalFont, Size.Empty, HighlightFlags).Width);
        }

        /// <summary>Dibuja el texto con las letras que coinciden con lo escrito en negrita.</summary>
        static void DrawHighlighted(Graphics g, string text, int[] highlights, Rectangle r, Font normalFont, Font boldFont, Color color)
        {
            Size full = TextRenderer.MeasureText(g, text, boldFont, Size.Empty, HighlightFlags);
            if (highlights == null || highlights.Length == 0 || full.Width > r.Width)
            {
                TextRenderer.DrawText(g, text, normalFont, r, color, HighlightFlags | TextFormatFlags.EndEllipsis);
                return;
            }

            int x = r.X;
            foreach (var segment in HighlightSegments(text, highlights))
            {
                Font font = segment.Value ? boldFont : normalFont;
                TextRenderer.DrawText(g, segment.Key, font, new Rectangle(x, r.Y, r.Right - x, r.Height), color, HighlightFlags);
                x += TextRenderer.MeasureText(g, segment.Key, font, Size.Empty, HighlightFlags).Width;
            }
        }

        void PaintDetails(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var pen = new Pen(m_Palette.Border))
                g.DrawLine(pen, 0, 0, m_Details.Width, 0);

            string text;
            Color color;
            if (m_Error != null)
            {
                text = m_Error;
                color = m_Palette.Error;
            }
            else
            {
                Row row = SelectedRow;
                if (row != null)
                    text = m_Options.Describe(row.Entry);
                else if (TypeSyntax.TryParseAttribute(m_Text.Text, out string attribute))
                    text = !m_AttributesByPrefix ? $"Ningún atributo coincide con {attribute}"
                        : attribute.Length < MinAttributeLetters ? $"Escribí al menos {MinAttributeLetters} letras del nombre del atributo"
                        : $"Ningún atributo empieza con {attribute}";
                else
                    text = "Ningún tipo coincide; Enter intenta igual (ej. Attribute:ClienteId)";
                color = m_Palette.Gray;
            }
            var rect = new Rectangle(S(12), 1, m_Details.Width - S(24), m_Details.Height - 1);
            TextRenderer.DrawText(g, text, m_SmallFont, rect, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        void PaintFooter(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(m_Palette.Border))
                g.DrawLine(pen, 0, 0, m_Footer.Width, 0);

            var hints = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Tab", "completar"),
                new KeyValuePair<string, string>("Enter", "aceptar"),
                new KeyValuePair<string, string>("Esc", m_Options.InBatch ? "saltear" : "cancelar"),
            };
            if (m_Options.InBatch)
                hints.Add(new KeyValuePair<string, string>("Shift+Esc", "terminar"));

            int x = S(12);
            int chipHeight = S(17);
            int y = (m_Footer.Height - chipHeight) / 2 + 1;
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            foreach (var hint in hints)
            {
                Size keySize = TextRenderer.MeasureText(g, hint.Key, m_BadgeFont, Size.Empty, flags);
                var chip = new Rectangle(x, y, keySize.Width + S(10), chipHeight);
                using (GraphicsPath path = Drawing.RoundRect(chip, S(3)))
                using (var pen = new Pen(m_Palette.ChipBorder))
                    g.DrawPath(pen, path);
                TextRenderer.DrawText(g, hint.Key, m_BadgeFont, chip, m_Palette.Text, flags | TextFormatFlags.HorizontalCenter);
                x = chip.Right + S(5);

                Size labelSize = TextRenderer.MeasureText(g, hint.Value, m_SmallFont, Size.Empty, flags);
                TextRenderer.DrawText(g, hint.Value, m_SmallFont, new Rectangle(x, y, labelSize.Width + 2, chipHeight), m_Palette.Gray, flags);
                x += labelSize.Width + S(14);
            }

            // Ayuda de colecciones solo si entra entera (en "declarar todas" suele no entrar)
            const string collectionHint = "colección: sufijo []";
            var right = new Rectangle(x, y, m_Footer.Width - x - S(12), chipHeight);
            if (TextRenderer.MeasureText(g, collectionHint, m_SmallFont, Size.Empty, flags).Width <= right.Width)
                TextRenderer.DrawText(g, collectionHint, m_SmallFont, right, m_Palette.Gray, flags | TextFormatFlags.Right);
        }

        /// <summary>Panel con doble buffer para que el repintado no parpadee.</summary>
        sealed class BufferedPanel : Panel
        {
            public BufferedPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }
        }
    }
}
