using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GxQuickOpen
{
    enum QuickOpenOutcome { Cancelled, Open, Classic }

    sealed class QuickOpenOptions
    {
        /// <summary>Todos los objetos abribles; null mientras se carga el índice (ver <see cref="QuickOpenPopup.SetEntries"/>).</summary>
        public IReadOnlyList<ObjectEntry> Entries;
        public RankContext Rank = new RankContext();
        public string InitialText = string.Empty;
        public Palette Palette = Palette.LightPalette();
        /// <summary>Ícono del tipo (16x16), o null.</summary>
        public Func<TypeInfo, Image> IconFor = type => null;
        /// <summary>Texto para la barra inferior mientras no hay objetos ("Cargando...").</summary>
        public string LoadingText = "Cargando los objetos de la KB…";
    }

    /// <summary>
    /// Búsqueda rápida de objetos (reemplaza Ctrl+O). Se escribe y la lista filtra al instante;
    /// cada fila muestra el módulo, y arriba hay un chip por módulo con la cantidad de resultados.
    ///   Enter            abre el seleccionado (o los marcados)
    ///   Ctrl+Enter       marca / desmarca para abrir varios
    ///   Tab / Shift+Tab  siguiente / anterior módulo
    ///   ↑ ↓ RePág AvPág  mueven la selección
    ///   Ctrl+O           abre el diálogo clásico de GeneXus
    ///   Esc              cierra
    /// </summary>
    sealed class QuickOpenPopup : Form
    {
        const int MaxShown = 500;
        const int DebounceThreshold = 20000;
        const int EM_SETCUEBANNER = 0x1501;

        sealed class Row
        {
            public string Header;
            public Match Match;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        readonly QuickOpenOptions m_Options;
        readonly Palette m_Palette;
        readonly float m_Scale;

        readonly Font m_UiFont;
        readonly Font m_SmallFont;
        readonly Font m_SmallBoldFont;
        readonly Font m_TinyBoldFont;
        readonly Font m_NameFont;
        readonly Font m_NameBoldFont;
        readonly Font m_SearchFont;

        readonly Panel m_Search;
        readonly TextBox m_Text;
        readonly Panel m_Facets;
        readonly ListBox m_List;
        readonly Panel m_Footer;
        readonly ChromeButton m_CloseButton;
        readonly ChromeButton m_CancelButton;
        readonly Timer m_Debounce;

        IReadOnlyList<ObjectEntry> m_Entries;
        SearchResult m_Result = new SearchResult();
        Query m_Query = new Query();
        /// <summary>Módulo elegido en los chips: null = todos, "" = raíz.</summary>
        string m_Facet;
        int m_FacetScroll;
        readonly List<KeyValuePair<Rectangle, ModuleFacet>> m_FacetHits = new List<KeyValuePair<Rectangle, ModuleFacet>>();
        readonly HashSet<ObjectEntry> m_Marked = new HashSet<ObjectEntry>();
        bool m_Closing;

        public QuickOpenOutcome Outcome { get; private set; } = QuickOpenOutcome.Cancelled;

        /// <summary>Lo que hay que abrir (los marcados en orden, o el seleccionado).</summary>
        public List<ObjectEntry> SelectedEntries { get; } = new List<ObjectEntry>();

        public string SearchText => m_Text.Text;

        /// <summary>Qué la cerró (para el log de diagnóstico).</summary>
        public string CloseReason { get; private set; } = string.Empty;

        public QuickOpenPopup(QuickOpenOptions options)
        {
            m_Options = options;
            m_Palette = options.Palette;
            m_Entries = options.Entries;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                m_Scale = g.DpiX / 96f;

            FontFamily family = SystemFonts.MessageBoxFont.FontFamily;
            m_UiFont = new Font(family, 9f);
            m_SmallFont = new Font(family, 8.25f);
            m_SmallBoldFont = new Font(family, 8.25f, FontStyle.Bold);
            m_TinyBoldFont = new Font(family, 7f, FontStyle.Bold);
            m_NameFont = new Font(family, 10f);
            m_NameBoldFont = new Font(family, 10f, FontStyle.Bold);
            m_SearchFont = new Font(family, 11.5f);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Font = m_UiFont;
            BackColor = m_Palette.Border;
            Padding = new Padding(1);
            Size = new Size(S(780), S(540));

            m_Search = new BufferedPanel { Dock = DockStyle.Top, Height = S(52), BackColor = m_Palette.Back };
            m_Search.Paint += PaintSearch;
            m_Text = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = m_Palette.InputBack,
                ForeColor = m_Palette.Text,
                Font = m_SearchFont,
                Text = options.InitialText ?? string.Empty,
            };
            m_Text.TextChanged += (s, e) => ScheduleRefilter();
            m_Text.GotFocus += (s, e) => m_Search.Invalidate();
            m_Text.LostFocus += (s, e) => m_Search.Invalidate();
            m_Search.Controls.Add(m_Text);
            // La cruz y "Cancelar", donde están en el diálogo clásico; los dos cierran como Esc
            m_CloseButton = new ChromeButton(m_Palette, m_Scale, null);
            m_CloseButton.Click += (s, e) => Close(QuickOpenOutcome.Cancelled, "botón cerrar");
            m_Search.Controls.Add(m_CloseButton);
            m_Search.Resize += (s, e) => LayoutSearchBox();

            m_Facets = new BufferedPanel { Dock = DockStyle.Top, Height = S(34), BackColor = m_Palette.Back };
            m_Facets.Paint += PaintFacets;
            m_Facets.MouseDown += FacetsMouseDown;
            m_Facets.MouseWheel += (s, e) => ScrollFacets(e.Delta > 0 ? -1 : 1);

            m_List = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                BorderStyle = BorderStyle.None,
                BackColor = m_Palette.Back,
                ItemHeight = S(44),
                TabStop = false,
            };
            m_List.DrawItem += DrawRow;
            m_List.SelectedIndexChanged += (s, e) => { SkipHeaderSelection(); m_Footer.Invalidate(); };
            m_List.DoubleClick += (s, e) => Accept();
            m_List.MouseDown += ListMouseDown;
            if (m_Palette.Dark)
                m_List.HandleCreated += (s, e) => SetWindowTheme(m_List.Handle, "DarkMode_Explorer", null);

            m_Footer = new BufferedPanel { Dock = DockStyle.Bottom, Height = S(40), BackColor = m_Palette.Back };
            m_Footer.Paint += PaintFooter;
            m_CancelButton = new ChromeButton(m_Palette, m_Scale, "Cancelar") { Font = m_UiFont };
            m_CancelButton.Click += (s, e) => Close(QuickOpenOutcome.Cancelled, "botón cancelar");
            m_Footer.Controls.Add(m_CancelButton);
            m_Footer.Resize += (s, e) => LayoutFooter();

            // El orden importa para el docking: lo agregado último se acomoda primero (más afuera)
            Controls.Add(m_List);
            Controls.Add(m_Facets);
            Controls.Add(m_Search);
            Controls.Add(m_Footer);

            m_Debounce = new Timer { Interval = 60 };
            m_Debounce.Tick += (s, e) => { m_Debounce.Stop(); Refilter(); };

            Deactivate += (s, e) => Close(QuickOpenOutcome.Cancelled, "perdió el foco");
            Refilter();
        }

        int S(int value) => (int)Math.Round(value * m_Scale);

        /// <summary>Carga (o actualiza) los objetos, conservando lo seleccionado.</summary>
        public void SetEntries(IReadOnlyList<ObjectEntry> entries)
        {
            Guid? selected = SelectedMatch?.Entry.Guid;
            m_Entries = entries;
            var markedGuids = new HashSet<Guid>(m_Marked.Select(e => e.Guid));
            m_Marked.Clear();
            foreach (ObjectEntry entry in entries.Where(e => markedGuids.Contains(e.Guid)))
                m_Marked.Add(entry);
            Refilter(selected);
        }

        /// <summary>No se pudo cargar el índice: mostrar por qué en la barra inferior.</summary>
        public void SetLoadingText(string text)
        {
            m_Options.LoadingText = text;
            m_Footer.Invalidate();
        }

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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SendMessage(m_Text.Handle, EM_SETCUEBANNER, (IntPtr)1, "Nombre del objeto…   @módulo   #tipo   módulo.nombre");
            m_Text.Focus();
            m_Text.SelectAll();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_Debounce.Dispose();
                m_UiFont.Dispose();
                m_SmallFont.Dispose();
                m_SmallBoldFont.Dispose();
                m_TinyBoldFont.Dispose();
                m_NameFont.Dispose();
                m_NameBoldFont.Dispose();
                m_SearchFont.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                    Close(QuickOpenOutcome.Cancelled);
                    return true;
                case Keys.Enter:
                    Accept();
                    return true;
                case Keys.Control | Keys.Enter:
                case Keys.Shift | Keys.Enter:
                    ToggleMark();
                    return true;
                case Keys.Tab:
                    CycleFacet(1);
                    return true;
                case Keys.Shift | Keys.Tab:
                    CycleFacet(-1);
                    return true;
                case Keys.Control | Keys.O:
                    Close(QuickOpenOutcome.Classic);
                    return true;
                case Keys.Control | Keys.Back:
                    DeletePreviousWord();
                    return true;
                case Keys.Down: MoveSelection(1); return true;
                case Keys.Up: MoveSelection(-1); return true;
                case Keys.PageDown: MoveSelection(VisibleRows); return true;
                case Keys.PageUp: MoveSelection(-VisibleRows); return true;
                case Keys.Control | Keys.Home: MoveSelection(-m_List.Items.Count); return true;
                case Keys.Control | Keys.End: MoveSelection(m_List.Items.Count); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- Filtrado ----

        void ScheduleRefilter()
        {
            if (m_Entries != null && m_Entries.Count > DebounceThreshold)
            {
                m_Debounce.Stop();
                m_Debounce.Start();
            }
            else
            {
                Refilter();
            }
        }

        void Refilter(Guid? keepSelected = null)
        {
            m_Debounce.Stop();
            m_Query = Query.Parse(m_Text.Text);
            var rows = new List<Row>();

            if (m_Entries == null)
            {
                m_Result = new SearchResult();
                rows.Add(new Row { Header = "CARGANDO OBJETOS…" });
                ShowRows(rows, -1);
                return;
            }

            m_Result = Search.Run(m_Entries, m_Query, m_Options.Rank, m_Facet, MaxShown);
            // El módulo elegido sigue visible aunque ya no tenga resultados, para poder salir con Tab
            if (m_Facet != null && !m_Result.Facets.Any(f => f.Module != null && string.Equals(f.Module, m_Facet, StringComparison.OrdinalIgnoreCase)))
                m_Result.Facets.Add(new ModuleFacet { Module = m_Facet, Count = 0 });
            EnsureFacetVisible();

            if (m_Query.IsEmpty)
                rows.Add(new Row { Header = m_Result.Matches.Count > 0 ? "RECIENTES" : "ESCRIBÍ PARTE DEL NOMBRE  ·  prc/dp/wp/wc/sdt/trn…  ·  nombre módulo  ·  @módulo" });
            else if (m_Result.Matches.Count == 0)
                rows.Add(new Row { Header = "NINGÚN OBJETO COINCIDE" });
            rows.AddRange(m_Result.Matches.Select(m => new Row { Match = m }));

            int preferred = keepSelected.HasValue ? rows.FindIndex(r => r.Match != null && r.Match.Entry.Guid == keepSelected.Value) : -1;
            ShowRows(rows, preferred);
        }

        void ShowRows(List<Row> rows, int preferred)
        {
            m_List.BeginUpdate();
            m_List.Items.Clear();
            m_List.Items.AddRange(rows.Cast<object>().ToArray());
            int selected = preferred >= 0 ? preferred : rows.FindIndex(r => r.Match != null);
            if (selected >= 0)
                m_List.SelectedIndex = selected;
            m_List.EndUpdate();
            m_Facets.Invalidate();
            m_Footer.Invalidate();
        }

        // ---- Chips de módulo ----

        void CycleFacet(int delta)
        {
            List<ModuleFacet> facets = m_Result.Facets;
            if (facets.Count <= 1)
                return;
            int current = Math.Max(0, CurrentFacetIndex);
            int next = (current + delta + facets.Count) % facets.Count;
            SelectFacet(facets[next].Module);
        }

        int CurrentFacetIndex => m_Result.Facets.FindIndex(f =>
            f.Module == null ? m_Facet == null : m_Facet != null && string.Equals(f.Module, m_Facet, StringComparison.OrdinalIgnoreCase));

        void SelectFacet(string module)
        {
            m_Facet = module;
            Refilter();
        }

        void ScrollFacets(int delta)
        {
            m_FacetScroll = Math.Max(0, Math.Min(m_Result.Facets.Count - 1, m_FacetScroll + delta));
            m_Facets.Invalidate();
        }

        /// <summary>Que el chip elegido quede a la vista (los chips se dibujan desde <see cref="m_FacetScroll"/>).</summary>
        void EnsureFacetVisible()
        {
            int current = CurrentFacetIndex;
            if (current <= 0)
            {
                m_FacetScroll = 0;
                return;
            }
            if (current < m_FacetScroll)
                m_FacetScroll = current;
            using (Graphics g = m_Facets.CreateGraphics())
            {
                while (m_FacetScroll < current && !FacetFits(g, m_FacetScroll, current))
                    m_FacetScroll++;
            }
        }

        bool FacetFits(Graphics g, int from, int index)
        {
            int x = S(12) + (from > 0 ? ChipWidth(g, "‹") + S(6) : 0);
            int limit = m_Facets.Width - S(12) - ChipWidth(g, "+999") - S(6);
            for (int i = from; i <= index; i++)
            {
                x += ChipWidth(g, ChipText(m_Result.Facets[i])) + S(6);
                if (x > limit + S(6))
                    return false;
            }
            return true;
        }

        static string ChipText(ModuleFacet facet) => facet.Label + "  " + facet.Count;

        int ChipWidth(Graphics g, string text) =>
            TextRenderer.MeasureText(g, text, m_SmallFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + S(18);

        void FacetsMouseDown(object sender, MouseEventArgs e)
        {
            foreach (var hit in m_FacetHits)
            {
                if (!hit.Key.Contains(e.Location))
                    continue;
                if (hit.Value == null)
                    ScrollFacets(-1);
                else if (hit.Value.Count < 0)
                    ScrollFacets(1);
                else
                    SelectFacet(hit.Value.Module);
                break;
            }
            BeginInvoke((Action)(() => m_Text.Focus()));
        }

        // ---- Lista ----

        Match SelectedMatch => (m_List.SelectedItem as Row)?.Match;

        int VisibleRows => Math.Max(1, m_List.ClientSize.Height / m_List.ItemHeight - 1);

        bool IsHeader(int index) => ((Row)m_List.Items[index]).Match == null;

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
            {
                // Al chocar con el encabezado de arriba, quedarse en la primera fila
                target = Math.Max(0, Math.Min(count - 1, m_List.SelectedIndex + delta));
                while (target >= 0 && target < count && IsHeader(target))
                    target -= step;
                if (target < 0 || target >= count)
                    return;
            }
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

        void ListMouseDown(object sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0)
            {
                int index = m_List.IndexFromPoint(e.Location);
                if (index >= 0 && !IsHeader(index))
                {
                    m_List.SelectedIndex = index;
                    ToggleMark();
                }
            }
            BeginInvoke((Action)(() => m_Text.Focus()));
        }

        void ToggleMark()
        {
            Match match = SelectedMatch;
            if (match == null)
                return;
            if (!m_Marked.Remove(match.Entry))
                m_Marked.Add(match.Entry);
            m_List.Invalidate();
            m_Footer.Invalidate();
            MoveSelection(1);
        }

        void DeletePreviousWord()
        {
            int caret = m_Text.SelectionStart;
            if (m_Text.SelectionLength > 0)
            {
                m_Text.SelectedText = string.Empty;
                return;
            }
            string text = m_Text.Text;
            int start = caret;
            while (start > 0 && char.IsWhiteSpace(text[start - 1]))
                start--;
            while (start > 0 && !char.IsWhiteSpace(text[start - 1]) && text[start - 1] != '.')
                start--;
            if (start == caret && start > 0)
                start--;   // un punto o separador suelto
            m_Text.Text = text.Substring(0, start) + text.Substring(caret);
            m_Text.SelectionStart = start;
        }

        void Accept()
        {
            SelectedEntries.Clear();
            if (m_Marked.Count > 0)
                SelectedEntries.AddRange(m_Marked);
            else if (SelectedMatch != null)
                SelectedEntries.Add(SelectedMatch.Entry);
            if (SelectedEntries.Count == 0)
                return;
            Close(QuickOpenOutcome.Open);
        }

        void Close(QuickOpenOutcome outcome, string reason = "tecla")
        {
            if (m_Closing)
                return;
            m_Closing = true;
            CloseReason = reason;
            Outcome = outcome;
            DialogResult = outcome == QuickOpenOutcome.Cancelled ? DialogResult.Cancel : DialogResult.OK;
            Close();
        }

        // ---- Dibujo ----

        /// <summary>El cuadro de búsqueda; a su derecha queda la cruz de cerrar, del mismo alto.</summary>
        Rectangle SearchFrame
        {
            get
            {
                int height = m_Search.Height - S(12);
                return new Rectangle(S(10), S(8), m_Search.Width - S(10) - S(6) - height - S(6), height);
            }
        }

        void LayoutSearchBox()
        {
            Rectangle frame = SearchFrame;
            int height = m_Text.PreferredHeight;
            int left = frame.X + S(30);
            m_Text.SetBounds(left, frame.Y + (frame.Height - height) / 2 + 1, frame.Right - S(10) - left, height);
            m_CloseButton.SetBounds(m_Search.Width - S(6) - frame.Height, frame.Y, frame.Height, frame.Height);
        }

        void LayoutFooter()
        {
            int width = Math.Max(S(80), TextRenderer.MeasureText(m_CancelButton.Text, m_UiFont).Width + S(24));
            int height = S(26);
            m_CancelButton.SetBounds(m_Footer.Width - S(10) - width, (m_Footer.Height - height) / 2 + 1, width, height);
        }

        void PaintSearch(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle frame = SearchFrame;
            using (GraphicsPath path = Drawing.RoundRect(frame, S(6)))
            using (var fill = new SolidBrush(m_Palette.InputBack))
            using (var pen = new Pen(m_Text.Focused ? m_Palette.Accent : m_Palette.ChipBorder, m_Text.Focused ? 1.5f * m_Scale : 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            // Lupa
            int size = S(11);
            var lens = new Rectangle(frame.X + S(10), frame.Y + (frame.Height - size) / 2 - S(1), size - S(3), size - S(3));
            using (var pen = new Pen(m_Palette.Gray, 1.6f * m_Scale))
            {
                g.DrawEllipse(pen, lens);
                g.DrawLine(pen, lens.Right - S(1), lens.Bottom - S(1), lens.Right + S(3), lens.Bottom + S(3));
            }
        }

        void PaintFacets(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            m_FacetHits.Clear();

            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            List<ModuleFacet> facets = m_Result.Facets;
            int chipHeight = S(22);
            int y = (m_Facets.Height - chipHeight) / 2 - S(1);
            int x = S(12);

            if (facets.Count <= 1)
            {
                // Sin resultados o sin texto: explicar el filtro
                string help = m_Query.IsEmpty
                    ? "Los chips de módulo aparecen al escribir; Tab pasa de uno a otro"
                    : string.Empty;
                TextRenderer.DrawText(g, help, m_SmallFont, new Rectangle(x, y, m_Facets.Width - 2 * x, chipHeight), m_Palette.Gray,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                DrawSeparator(g, m_Facets.Height - 1, m_Facets.Width);
                return;
            }

            int scroll = Math.Min(m_FacetScroll, facets.Count - 1);
            if (scroll > 0)
            {
                var left = new Rectangle(x, y, ChipWidth(g, "‹"), chipHeight);
                DrawChip(g, left, "‹", false, flags);
                m_FacetHits.Add(new KeyValuePair<Rectangle, ModuleFacet>(left, null));
                x = left.Right + S(6);
            }

            int current = CurrentFacetIndex;
            int moreWidth = ChipWidth(g, "+" + facets.Count);
            for (int i = scroll; i < facets.Count; i++)
            {
                string text = ChipText(facets[i]);
                int width = ChipWidth(g, text);
                bool last = i == facets.Count - 1;
                if (x + width > m_Facets.Width - S(12) - (last ? 0 : moreWidth + S(6)))
                {
                    string more = "+" + (facets.Count - i);
                    var moreRect = new Rectangle(x, y, ChipWidth(g, more), chipHeight);
                    DrawChip(g, moreRect, more, false, flags);
                    m_FacetHits.Add(new KeyValuePair<Rectangle, ModuleFacet>(moreRect, new ModuleFacet { Count = -1 }));
                    break;
                }
                var chip = new Rectangle(x, y, width, chipHeight);
                DrawFacetChip(g, chip, facets[i], i == current);
                m_FacetHits.Add(new KeyValuePair<Rectangle, ModuleFacet>(chip, facets[i]));
                x = chip.Right + S(6);
            }
            DrawSeparator(g, m_Facets.Height - 1, m_Facets.Width);
        }

        void DrawChip(Graphics g, Rectangle chip, string text, bool selected, TextFormatFlags flags)
        {
            using (GraphicsPath path = Drawing.RoundRect(chip, S(11)))
            using (var fill = new SolidBrush(selected ? m_Palette.ChipSelectedBack : m_Palette.ChipBack))
            using (var pen = new Pen(selected ? m_Palette.ChipSelectedBack : m_Palette.ChipBorder))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, text, m_SmallFont, chip, selected ? m_Palette.ChipSelectedText : m_Palette.Text, flags);
        }

        /// <summary>Chip "Módulo  12": el nombre normal y la cantidad más tenue.</summary>
        void DrawFacetChip(Graphics g, Rectangle chip, ModuleFacet facet, bool selected)
        {
            using (GraphicsPath path = Drawing.RoundRect(chip, S(11)))
            using (var fill = new SolidBrush(selected ? m_Palette.ChipSelectedBack : m_Palette.ChipBack))
            using (var pen = new Pen(selected ? m_Palette.ChipSelectedBack : m_Palette.ChipBorder))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
            string label = facet.Label;
            string count = facet.Count.ToString();
            Size labelSize = TextRenderer.MeasureText(g, label + "  ", m_SmallFont, Size.Empty, flags);
            Color labelColor = selected ? m_Palette.ChipSelectedText : facet.Module == null ? m_Palette.Text : m_Palette.Module;
            Color countColor = selected ? m_Palette.ChipSelectedText : m_Palette.Gray;
            var labelRect = new Rectangle(chip.X + S(9), chip.Y, labelSize.Width + S(2), chip.Height);
            TextRenderer.DrawText(g, label, m_SmallFont, labelRect, labelColor, flags);
            TextRenderer.DrawText(g, count, m_SmallFont, new Rectangle(chip.X + S(9) + labelSize.Width, chip.Y, chip.Right - chip.X - S(9) - labelSize.Width, chip.Height), countColor, flags);
        }

        void DrawSeparator(Graphics g, int y, int width)
        {
            using (var pen = new Pen(m_Palette.Border))
                g.DrawLine(pen, 0, y, width, y);
        }

        void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;
            Graphics g = e.Graphics;
            var row = (Row)m_List.Items[e.Index];
            Rectangle r = e.Bounds;

            if (row.Match == null)
            {
                using (var back = new SolidBrush(m_Palette.Back))
                    g.FillRectangle(back, r);
                var headerRect = new Rectangle(r.X + S(14), r.Y + S(8), r.Width - S(28), r.Height - S(8));
                TextRenderer.DrawText(g, row.Header, m_TinyBoldFont, headerRect, m_Palette.Gray,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                return;
            }

            ObjectEntry entry = row.Match.Entry;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool marked = m_Marked.Contains(entry);
            using (var back = new SolidBrush(selected ? m_Palette.Selection : m_Palette.Back))
                g.FillRectangle(back, r);
            if (selected)
                using (var bar = new SolidBrush(m_Palette.SelectionBar))
                    g.FillRectangle(bar, r.X, r.Y + S(4), S(3), r.Height - S(8));

            // Ícono del tipo (o la marca de "abrir también")
            int iconSize = S(16);
            var iconRect = new Rectangle(r.X + S(14), r.Y + (r.Height - iconSize) / 2, iconSize, iconSize);
            if (marked)
            {
                DrawCheck(g, iconRect);
            }
            else
            {
                Image icon = SafeIcon(entry.Type);
                if (icon != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(icon, iconRect);
                }
            }

            int left = iconRect.Right + S(12);
            int right = r.Right - S(14);
            int top = r.Y + S(4);
            int lineHeight = (r.Height - S(8)) / 2;
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

            // Línea 1: nombre (con lo que coincide resaltado) y el tipo a la derecha
            string type = entry.Type.Name ?? string.Empty;
            Size typeSize = TextRenderer.MeasureText(g, type, m_SmallFont, Size.Empty, flags);
            int typeWidth = Math.Min(typeSize.Width, r.Width / 3);
            TextRenderer.DrawText(g, type, m_SmallFont, new Rectangle(right - typeWidth, top, typeWidth, lineHeight),
                m_Palette.Gray, flags | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
            DrawHighlighted(g, entry.Name, row.Match.Highlights, new Rectangle(left, top, right - typeWidth - S(12) - left, lineHeight),
                m_NameFont, m_NameBoldFont, m_Palette.Text);

            // Línea 2: módulo (siempre a la vista) · descripción
            var second = new Rectangle(left, top + lineHeight, right - left, lineHeight);
            string module = entry.Module.Length > 0 ? entry.Module : "(raíz)";
            // Con algo resaltado se mide en negrita (ocupa un poco más)
            Font moduleFont = row.Match.ModuleHighlights != null ? m_SmallBoldFont : m_SmallFont;
            Size moduleSize = TextRenderer.MeasureText(g, module, moduleFont, Size.Empty, flags);
            int moduleWidth = Math.Min(moduleSize.Width, second.Width * 2 / 3);
            var moduleRect = new Rectangle(second.X, second.Y, moduleWidth, second.Height);
            if (entry.Module.Length > 0)
                DrawHighlighted(g, module, row.Match.ModuleHighlights, moduleRect, m_SmallFont, m_SmallBoldFont, m_Palette.Module);
            else
                TextRenderer.DrawText(g, module, m_SmallFont, moduleRect, m_Palette.Gray, flags | TextFormatFlags.EndEllipsis);

            string description = entry.Description;
            if (!string.IsNullOrWhiteSpace(description) && !string.Equals(description, entry.Name, StringComparison.OrdinalIgnoreCase))
            {
                int x = second.X + moduleWidth;
                TextRenderer.DrawText(g, "  ·  " + description, m_SmallFont, new Rectangle(x, second.Y, second.Right - x, second.Height),
                    m_Palette.Gray, flags | TextFormatFlags.EndEllipsis);
            }
        }

        Image SafeIcon(TypeInfo type)
        {
            try
            {
                return m_Options.IconFor(type);
            }
            catch (Exception)
            {
                return null;
            }
        }

        void DrawCheck(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Drawing.RoundRect(r, S(3)))
            using (var fill = new SolidBrush(m_Palette.Accent))
                g.FillPath(fill, path);
            using (var pen = new Pen(Color.White, 2f * m_Scale))
                g.DrawLines(pen, new[]
                {
                    new Point(r.X + r.Width * 1 / 4, r.Y + r.Height / 2),
                    new Point(r.X + r.Width * 5 / 12, r.Y + r.Height * 7 / 10),
                    new Point(r.X + r.Width * 3 / 4, r.Y + r.Height * 3 / 10),
                });
        }

        /// <summary>El texto con las letras que coinciden en negrita y color.</summary>
        void DrawHighlighted(Graphics g, string name, int[] highlights, Rectangle r, Font normalFont, Font boldFont, Color color)
        {
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            Size full = TextRenderer.MeasureText(g, name, boldFont, Size.Empty, flags);
            if (highlights == null || highlights.Length == 0 || full.Width > r.Width)
            {
                TextRenderer.DrawText(g, name, normalFont, r, color, flags | TextFormatFlags.EndEllipsis);
                return;
            }

            var marks = new bool[name.Length];
            foreach (int p in highlights)
                if (p >= 0 && p < marks.Length)
                    marks[p] = true;

            int x = r.X;
            int start = 0;
            while (start < name.Length)
            {
                bool on = marks[start];
                int end = start;
                while (end < name.Length && marks[end] == on)
                    end++;
                string segment = name.Substring(start, end - start);
                Font font = on ? boldFont : normalFont;
                TextRenderer.DrawText(g, segment, font, new Rectangle(x, r.Y, r.Right - x, r.Height), on ? m_Palette.Highlight : color, flags);
                x += TextRenderer.MeasureText(g, segment, font, Size.Empty, flags).Width;
                start = end;
            }
        }

        void PaintFooter(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawSeparator(g, 0, m_Footer.Width);
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;

            // Izquierda: cuántos hay
            string status;
            if (m_Entries == null)
                status = m_Options.LoadingText;
            else if (m_Query.IsEmpty)
                status = $"{m_Entries.Count:N0} objetos en la KB";
            else
            {
                int modules = m_Result.Facets.Count(f => f.Module != null && f.Count > 0);
                status = m_Result.Total > m_Result.Matches.Count
                    ? $"{m_Result.Matches.Count} de {m_Result.Total:N0}"
                    : $"{m_Result.Total:N0} {(m_Result.Total == 1 ? "objeto" : "objetos")}";
                if (m_Facet == null && modules > 1)
                    status += $" en {modules} módulos";
            }
            if (m_Marked.Count > 0)
                status += $"  ·  {m_Marked.Count} marcado{(m_Marked.Count == 1 ? "" : "s")}";
            Size statusSize = TextRenderer.MeasureText(g, status, m_SmallFont, Size.Empty, flags);
            int chipHeight = S(18);
            int y = (m_Footer.Height - chipHeight) / 2 + 1;
            TextRenderer.DrawText(g, status, m_SmallFont, new Rectangle(S(12), y, statusSize.Width + 2, chipHeight), m_Palette.Gray, flags);

            // Derecha: atajos, de derecha a izquierda mientras entren
            var hints = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Enter", m_Marked.Count > 0 ? "abrir marcados" : "abrir"),
                new KeyValuePair<string, string>("Tab", "módulo"),
                new KeyValuePair<string, string>("Ctrl+Enter", "marcar"),
                new KeyValuePair<string, string>("Ctrl+O", "diálogo clásico"),
                new KeyValuePair<string, string>("Esc", "cerrar"),
            };
            int limit = S(12) + statusSize.Width + S(16);
            int right = m_CancelButton.Left - S(8);
            var widths = hints.Select(h =>
                TextRenderer.MeasureText(g, h.Key, m_TinyBoldFont, Size.Empty, flags).Width + S(10) + S(5)
                + TextRenderer.MeasureText(g, h.Value, m_SmallFont, Size.Empty, flags).Width + S(14)).ToList();
            // Sacar los menos importantes (desde el final, menos Esc) hasta que entren
            while (hints.Count > 1 && limit + widths.Sum() > right)
            {
                int remove = hints.Count >= 3 ? hints.Count - 2 : hints.Count - 1;
                hints.RemoveAt(remove);
                widths.RemoveAt(remove);
            }

            int x = right - widths.Sum() + S(2);
            foreach (var hint in hints)
            {
                Size keySize = TextRenderer.MeasureText(g, hint.Key, m_TinyBoldFont, Size.Empty, flags);
                var chip = new Rectangle(x, y, keySize.Width + S(10), chipHeight);
                using (GraphicsPath path = Drawing.RoundRect(chip, S(3)))
                using (var pen = new Pen(m_Palette.ChipBorder))
                    g.DrawPath(pen, path);
                TextRenderer.DrawText(g, hint.Key, m_TinyBoldFont, chip, m_Palette.Text, flags | TextFormatFlags.HorizontalCenter);
                x = chip.Right + S(5);

                Size labelSize = TextRenderer.MeasureText(g, hint.Value, m_SmallFont, Size.Empty, flags);
                TextRenderer.DrawText(g, hint.Value, m_SmallFont, new Rectangle(x, y, labelSize.Width + 2, chipHeight), m_Palette.Gray, flags);
                x += labelSize.Width + S(14);
            }
        }

        /// <summary>
        /// Botón dibujado que no le saca el foco a la búsqueda: sin texto es la cruz de cerrar
        /// (roja al pasar el mouse, como la de la barra de título), con texto un botón de diálogo.
        /// </summary>
        sealed class ChromeButton : Control
        {
            static readonly Color CloseHover = Color.FromArgb(0xC4, 0x2B, 0x1C);
            static readonly Color ClosePressed = Color.FromArgb(0xB2, 0x27, 0x1A);

            readonly Palette m_Palette;
            readonly float m_Scale;
            bool m_Hover;
            bool m_Pressed;

            public ChromeButton(Palette palette, float scale, string text)
            {
                m_Palette = palette;
                m_Scale = scale;
                Text = text ?? string.Empty;
                SetStyle(ControlStyles.Selectable, false);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
                BackColor = palette.Back;
            }

            bool IsClose => Text.Length == 0;

            int S(int value) => (int)Math.Round(value * m_Scale);

            protected override void OnMouseEnter(EventArgs e) { m_Hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { m_Hover = false; m_Pressed = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { m_Pressed = true; Invalidate(); }
                base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                m_Pressed = false;
                Invalidate();
                base.OnMouseUp(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

                if (IsClose)
                {
                    bool hot = m_Hover || m_Pressed;
                    if (hot)
                        using (GraphicsPath path = Drawing.RoundRect(r, S(6)))
                        using (var fill = new SolidBrush(m_Pressed ? ClosePressed : CloseHover))
                            g.FillPath(fill, path);
                    int half = S(5);
                    int cx = Width / 2, cy = Height / 2;
                    using (var pen = new Pen(hot ? Color.White : m_Palette.Gray, 1.3f * m_Scale))
                    {
                        g.DrawLine(pen, cx - half, cy - half, cx + half, cy + half);
                        g.DrawLine(pen, cx + half, cy - half, cx - half, cy + half);
                    }
                    return;
                }

                Color back = m_Pressed ? m_Palette.ChipBorder : m_Hover ? m_Palette.Selection : m_Palette.ChipBack;
                Color border = m_Hover || m_Pressed ? m_Palette.Accent : m_Palette.ChipBorder;
                using (GraphicsPath path = Drawing.RoundRect(r, S(4)))
                using (var fill = new SolidBrush(back))
                using (var pen = new Pen(border))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }
                TextRenderer.DrawText(g, Text, Font, r, m_Palette.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }
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
