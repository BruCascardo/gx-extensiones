using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GxCodeCheck
{
    /// <summary>
    /// El cartel de un subrayado, como el hover de VS Code: qué pasa, la aclaración si es consecuencia de otro
    /// error y el mensaje original de GeneXus. El texto se puede seleccionar y copiar (Ctrl+C), y "Copiar" copia
    /// todo con la ubicación. Abajo, las correcciones que se pueden aplicar con un clic (o, si se abrió con el
    /// teclado, con Tab y Enter o con su número). Se cierra cuando el mouse se va del subrayado y del cartel,
    /// o con Esc.
    /// </summary>
    sealed class ErrorPopup : Form
    {
        const int PopupWidth = 520;

        readonly List<Diagnostic> m_Diagnostics;
        readonly string m_Title;
        readonly Rectangle m_Anchor;
        readonly Timer m_MouseTimer = new Timer { Interval = 120 };
        readonly RichTextBox m_Text;
        readonly bool m_Dark;
        readonly List<(Diagnostic Diagnostic, Fix Fix)> m_Fixes;
        readonly Action<Diagnostic, Fix> m_OnFix;
        readonly List<LinkLabel> m_FixLinks = new List<LinkLabel>();
        // Abierto con el teclado: no se cierra por dónde esté el mouse, sino con Esc o al ir a otro lado
        bool m_Keyboard;
        int m_TicksOutside;

        public ErrorPopup(List<Diagnostic> diagnostics, string title, Color editorBack, Rectangle anchor,
                          List<(Diagnostic Diagnostic, Fix Fix)> fixes = null, Action<Diagnostic, Fix> onFix = null)
        {
            m_Fixes = onFix == null ? new List<(Diagnostic, Fix)>() : fixes ?? new List<(Diagnostic, Fix)>();
            m_OnFix = onFix;
            m_Diagnostics = diagnostics;
            m_Title = title;
            m_Anchor = anchor;
            m_Dark = editorBack.GetBrightness() < 0.5f;

            Color back = m_Dark ? Color.FromArgb(0x25, 0x25, 0x26) : Color.FromArgb(0xF8, 0xF8, 0xF8);
            Color border = m_Dark ? Color.FromArgb(0x45, 0x45, 0x4C) : Color.FromArgb(0xC8, 0xC8, 0xC8);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            BackColor = border;
            Padding = new Padding(1);
            Font = SystemFonts.MessageBoxFont;

            var body = new Panel { Dock = DockStyle.Fill, BackColor = back, Padding = new Padding(10, 8, 10, 6) };
            m_Text = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = back,
                ScrollBars = RichTextBoxScrollBars.None,
                DetectUrls = false,
                ShortcutsEnabled = true,
                Font = Font,
            };
            var copy = new LinkLabel
            {
                Text = "Copiar",
                AutoSize = true,
                Dock = DockStyle.Right,
                LinkColor = m_Dark ? Color.FromArgb(0x4F, 0xA3, 0xFF) : Color.FromArgb(0x0B, 0x6B, 0xCB),
                ActiveLinkColor = m_Dark ? Color.FromArgb(0x4F, 0xA3, 0xFF) : Color.FromArgb(0x0B, 0x6B, 0xCB),
                LinkBehavior = LinkBehavior.HoverUnderline,
            };
            copy.LinkClicked += (s, e) => CopyAll(copy);
            var fixesPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = back, WrapContents = true, Margin = Padding.Empty };
            for (int i = 0; i < m_Fixes.Count; i++)
            {
                var (diagnostic, fix) = m_Fixes[i];
                var link = new LinkLabel
                {
                    Text = (m_Fixes.Count > 1 && i < 9 ? $"{i + 1}. " : "") + fix.Title,
                    AutoSize = true,
                    LinkColor = copy.LinkColor,
                    ActiveLinkColor = copy.ActiveLinkColor,
                    LinkBehavior = LinkBehavior.HoverUnderline,
                    Margin = new Padding(0, 0, 14, 2),
                    TabIndex = i,
                };
                link.LinkClicked += (s, e) => ApplyFix(diagnostic, fix);
                m_FixLinks.Add(link);
                fixesPanel.Controls.Add(link);
            }
            var footer = new Panel { Dock = DockStyle.Bottom, BackColor = back, Padding = new Padding(0, 6, 0, 0) };
            footer.Controls.Add(fixesPanel);
            footer.Controls.Add(copy);
            int fixesWidth = PopupWidth - body.Padding.Horizontal - Padding.Horizontal - copy.PreferredWidth - 8;
            int fixesHeight = m_Fixes.Count == 0 ? 0 : fixesPanel.GetPreferredSize(new Size(fixesWidth, 0)).Height;
            footer.Height = Math.Max(copy.PreferredHeight, fixesHeight) + 6;
            body.Controls.Add(m_Text);
            body.Controls.Add(footer);
            Controls.Add(body);

            // Con los handles creados y el ancho final, para medir cuánto ocupa el texto
            int maxText = Screen.FromRectangle(anchor).WorkingArea.Height / 2;
            Size = new Size(PopupWidth, maxText);
            CreateControl();
            PerformLayout();
            Fill(m_Dark ? Color.FromArgb(0xDC, 0xDC, 0xDC) : Color.FromArgb(0x1F, 0x23, 0x28),
                 m_Dark ? Color.FromArgb(0x9A, 0x9A, 0x9A) : Color.FromArgb(0x6B, 0x72, 0x80));

            int textHeight = m_Text.GetPositionFromCharIndex(Math.Max(0, m_Text.TextLength - 1)).Y + m_Text.Font.Height + 4;
            // Barra de scroll solo si no entra (muchos errores en el mismo lugar)
            if (textHeight > maxText)
                m_Text.ScrollBars = RichTextBoxScrollBars.Vertical;
            Size = new Size(PopupWidth, Math.Min(textHeight, maxText) + footer.Height + body.Padding.Vertical + Padding.Vertical);
            Location = PlaceNear(anchor, Size);

            m_MouseTimer.Tick += OnMouseTick;
        }

        /// <summary>Si ya muestra estos mismos errores.</summary>
        public bool Shows(List<Diagnostic> diagnostics) => diagnostics.SequenceEqual(m_Diagnostics);

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x00020000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            m_Text.Select(0, 0);
            m_MouseTimer.Start();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
            else if (e.Modifiers == Keys.None && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9 && e.KeyCode - Keys.D1 < m_Fixes.Count)
            {
                var (diagnostic, fix) = m_Fixes[e.KeyCode - Keys.D1];
                e.Handled = true;
                ApplyFix(diagnostic, fix);
            }
        }

        /// <summary>Abierto con el teclado: toma el foco, en la primera corrección, para elegir sin el mouse.</summary>
        public void ShowFocused(Form owner)
        {
            m_Keyboard = true;
            Show(owner);
            Activate();
            if (m_FixLinks.Count > 0)
                m_FixLinks[0].Focus();
        }

        void ApplyFix(Diagnostic diagnostic, Fix fix)
        {
            Close();
            m_OnFix?.Invoke(diagnostic, fix);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (m_Keyboard || !Bounds.Contains(Cursor.Position))
                Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                m_MouseTimer.Dispose();
            base.Dispose(disposing);
        }

        void Fill(Color text, Color gray)
        {
            Font bold = new Font(Font, FontStyle.Bold);
            Font small = new Font(Font.FontFamily, Font.Size - 0.5f);
            bool first = true;
            foreach (Diagnostic d in m_Diagnostics)
            {
                if (!first)
                    Append("\n", text, Font);
                first = false;
                Append(SeverityText(d.Severity), SeverityColor(d.Severity), bold);
                Append($"  ·  {OriginText(d.Origin)}{(d.Code.Length > 0 ? "  ·  " + d.Code : "")}  ·  línea {d.Line + 1}\n", gray, small);
                Append(d.Message + "\n", text, Font);
                if (!string.IsNullOrEmpty(d.Note))
                    Append(d.Note + "\n", gray, new Font(Font, FontStyle.Italic));
                foreach (string original in d.Original.Where(o => o != d.Message))
                    Append("GeneXus: " + original + "\n", gray, small);
            }
            // Sin el salto de línea final
            if (m_Text.TextLength > 0)
            {
                m_Text.Select(m_Text.TextLength - 1, 1);
                m_Text.SelectedText = "";
            }
        }

        void Append(string value, Color color, Font font)
        {
            m_Text.SelectionStart = m_Text.TextLength;
            m_Text.SelectionLength = 0;
            m_Text.SelectionColor = color;
            m_Text.SelectionFont = font;
            m_Text.AppendText(value);
        }

        void CopyAll(LinkLabel link)
        {
            try
            {
                Clipboard.SetText(string.Join("\r\n\r\n", m_Diagnostics.Select(d => d.CopyText(m_Title))));
                link.Text = "Copiado";
            }
            catch (Exception ex)
            {
                Log.Write("No se pudo copiar: " + ex.Message);
            }
        }

        void OnMouseTick(object sender, EventArgs e)
        {
            // Con el foco en el cartel (se está seleccionando texto, o se abrió con el teclado) no se cierra por el mouse
            if (ContainsFocus || m_Keyboard)
                return;
            Point mouse = Cursor.Position;
            Rectangle near = Rectangle.Union(Rectangle.Inflate(m_Anchor, 8, 4), Bounds);
            m_TicksOutside = near.Contains(mouse) ? 0 : m_TicksOutside + 1;
            if (m_TicksOutside >= 2)
                Close();
        }

        Color SeverityColor(Severity severity) =>
            severity == Severity.Error ? (m_Dark ? Color.FromArgb(0xF4, 0x87, 0x71) : Color.FromArgb(0xC6, 0x28, 0x28))
            : severity == Severity.Warning ? (m_Dark ? Color.FromArgb(0xE5, 0xB0, 0x4B) : Color.FromArgb(0xB2, 0x6B, 0x00))
            : (m_Dark ? Color.FromArgb(0x9A, 0xB0, 0xCC) : Color.FromArgb(0x55, 0x65, 0x80));

        static string SeverityText(Severity severity) =>
            severity == Severity.Error ? "Error" : severity == Severity.Warning ? "Advertencia" : "Consecuencia";

        static string OriginText(Origin origin) =>
            origin == Origin.Specifier ? "especificación" : origin == Origin.Check ? "revisión del texto"
            : origin == Origin.Types ? "chequeo de tipos" : origin == Origin.Standard ? "estándar del equipo" : "parser";

        /// <summary>Debajo del subrayado, o arriba si no entra, sin salirse de la pantalla.</summary>
        static Point PlaceNear(Rectangle anchor, Size size)
        {
            var location = new Point(anchor.Left, anchor.Bottom + 4);
            Rectangle screen = Screen.FromRectangle(anchor).WorkingArea;
            if (location.X + size.Width > screen.Right)
                location.X = screen.Right - size.Width;
            location.X = Math.Max(screen.Left, location.X);
            if (location.Y + size.Height > screen.Bottom)
                location.Y = Math.Max(screen.Top, anchor.Top - size.Height - 4);
            return location;
        }
    }
}
