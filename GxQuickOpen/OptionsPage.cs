using System;
using System.Drawing;
using System.Windows.Forms;
using Artech.Architecture.UI.Framework.Services;

namespace GxQuickOpen
{
    /// <summary>Página "GxQuickOpen" en Tools &gt; Options.</summary>
    sealed class OptionsCategory : ConfigurationCategory, ICategoryLoader
    {
        public OptionsCategory()
            : base("GxQuickOpen", "GxQuickOpen")
        {
            Loader = this;
        }

        public override Control CreateEditor() => new OptionsControl();

        public void LoadData(IParentDesigner parent, object data, ConfigurationCategory category)
        {
            ((OptionsControl)Control).LoadFromSettings(parent);
        }

        public new bool Flush()
        {
            if (Control != null)
                return ((OptionsControl)Control).SaveToSettings();
            return true;
        }
    }

    sealed class OptionsControl : UserControl
    {
        static readonly string[] ThemeNames = { "Automático (según el editor de código)", "Claro", "Oscuro" };

        readonly ShortcutBox m_ShowBox = new ShortcutBox();
        readonly ComboBox m_Theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly CheckBox m_Remember = new CheckBox();
        IParentDesigner m_Parent;
        bool m_Loading;

        public OptionsControl()
        {
            Font = SystemFonts.MessageBoxFont;
            AutoScroll = true;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                Padding = new Padding(8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddTitle(layout, "Atajo de teclado");
            AddNote(layout, "Por defecto es Ctrl+O, el mismo del diálogo \"Open Object\" de GeneXus, que así queda reemplazado " +
                            "(desde la búsqueda, Ctrl+O abre el diálogo clásico). Hacé clic en el campo y apretá la combinación; " +
                            "Supr o Retroceso lo deja sin atajo (queda en File > Abrir objeto).");
            int row = layout.RowCount++;
            layout.Controls.Add(new Label { Text = "Abrir la búsqueda rápida:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) }, 0, row);
            m_ShowBox.Dock = DockStyle.Fill;
            layout.Controls.Add(m_ShowBox, 1, row);
            var reset = new LinkLabel { Text = "Restablecer (" + Shortcuts.Format(Settings.DefaultShowKey) + ")", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 3, 3) };
            reset.LinkClicked += (s, e) => m_ShowBox.Keys = Settings.DefaultShowKey;
            layout.Controls.Add(reset, 2, row);

            AddTitle(layout, "Ventana");
            row = layout.RowCount++;
            layout.Controls.Add(new Label { Text = "Tema:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) }, 0, row);
            m_Theme.Items.AddRange(ThemeNames);
            m_Theme.Dock = DockStyle.Fill;
            layout.Controls.Add(m_Theme, 1, row);

            m_Remember.Text = "Abrir con la última búsqueda (seleccionada: escribir la reemplaza)";
            m_Remember.AutoSize = true;
            layout.Controls.Add(m_Remember, 0, layout.RowCount);
            layout.SetColumnSpan(m_Remember, 3);
            layout.RowCount++;

            var clearRecents = new LinkLabel { Text = "Borrar objetos recientes", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            clearRecents.LinkClicked += (s, e) =>
            {
                Recents.Clear();
                clearRecents.Text = "Recientes borrados";
                clearRecents.Enabled = false;
            };
            layout.Controls.Add(clearRecents, 0, layout.RowCount);
            layout.SetColumnSpan(clearRecents, 3);
            layout.RowCount++;

            Controls.Add(layout);

            m_ShowBox.KeysChanged += (s, e) => OnChanged();
            m_Theme.SelectedIndexChanged += (s, e) => OnChanged();
            m_Remember.CheckedChanged += (s, e) => OnChanged();
        }

        public void LoadFromSettings(IParentDesigner parent)
        {
            m_Parent = parent;
            m_Loading = true;
            m_ShowBox.Keys = Settings.ShowKey;
            m_Theme.SelectedIndex = (int)Settings.Theme;
            m_Remember.Checked = Settings.RememberSearch;
            m_Loading = false;
        }

        public bool SaveToSettings()
        {
            Settings.ShowKey = m_ShowBox.Keys;
            Settings.Theme = (ThemeChoice)Math.Max(0, m_Theme.SelectedIndex);
            Settings.RememberSearch = m_Remember.Checked;
            Settings.Save();
            return true;
        }

        void OnChanged()
        {
            if (!m_Loading && m_Parent != null)
                m_Parent.Dirty = true;
        }

        static void AddTitle(TableLayoutPanel layout, string text)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Margin = new Padding(3, 10, 3, 4),
            };
            layout.Controls.Add(label, 0, layout.RowCount);
            layout.SetColumnSpan(label, 3);
            layout.RowCount++;
        }

        static void AddNote(TableLayoutPanel layout, string text)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 0, 3, 8),
            };
            layout.Controls.Add(label, 0, layout.RowCount);
            layout.SetColumnSpan(label, 3);
            layout.RowCount++;
        }
    }

    /// <summary>Campo que captura una combinación de teclas en vez de texto.</summary>
    sealed class ShortcutBox : TextBox
    {
        Keys m_Keys;

        public event EventHandler KeysChanged;

        public ShortcutBox()
        {
            ReadOnly = true;
            BackColor = SystemColors.Window;
            ShortcutsEnabled = false;
            Cursor = Cursors.IBeam;
        }

        public Keys Keys
        {
            get => m_Keys;
            set
            {
                m_Keys = value;
                Text = Shortcuts.Format(value);
                KeysChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            // Tab sin modificadores sigue moviendo el foco
            if (key == Keys.Tab && (keyData & Keys.Modifiers) == 0)
                return base.ProcessCmdKey(ref msg, keyData);

            if ((key == Keys.Delete || key == Keys.Back) && (keyData & Keys.Modifiers) == 0)
            {
                Keys = Keys.None;
                return true;
            }
            if (Shortcuts.IsModifierKey(key))
                return true;   // esperar la tecla principal

            if (Shortcuts.IsValid(keyData))
                Keys = keyData;
            else
                Text = Shortcuts.Format(keyData) + "  ← necesita Ctrl o Alt";
            return true;
        }

        protected override void OnKeyPress(KeyPressEventArgs e) => e.Handled = true;

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            Text = Shortcuts.Format(m_Keys);   // descarta un intento inválido
        }
    }
}
