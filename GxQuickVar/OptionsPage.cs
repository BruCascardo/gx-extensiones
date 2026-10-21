using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Artech.Architecture.UI.Framework.Services;

namespace GxQuickVar
{
    /// <summary>Página "GxQuickVar" en Tools &gt; Options (mismo mecanismo que usa el editor de texto de GeneXus).</summary>
    sealed class OptionsCategory : ConfigurationCategory, ICategoryLoader
    {
        public OptionsCategory()
            : base("GxQuickVar", "GxQuickVar")
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
        readonly ShortcutBox m_DeclareBox = new ShortcutBox();
        readonly ShortcutBox m_DeclareAllBox = new ShortcutBox();
        readonly Label m_Warning = new Label();
        readonly CheckBox m_Remember = new CheckBox();
        readonly CheckBox m_Suggest = new CheckBox();
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
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddTitle(layout, "Atajos de teclado");
            AddNote(layout, "Funcionan dentro de los editores de código (Events, Source, Rules, Conditions). " +
                            "Hacé clic en el campo y apretá la combinación; Supr o Retroceso lo deja sin atajo.");
            AddShortcutRow(layout, "Declarar la variable bajo el cursor:", m_DeclareBox, Settings.DefaultDeclareKey);
            AddShortcutRow(layout, "Declarar todas las variables sin definir:", m_DeclareAllBox, Settings.DefaultDeclareAllKey);

            m_Warning.AutoSize = true;
            m_Warning.MaximumSize = new Size(560, 0);
            m_Warning.ForeColor = Color.FromArgb(0xB2, 0x5E, 0x00);
            m_Warning.Margin = new Padding(3, 2, 3, 8);
            layout.Controls.Add(m_Warning, 0, layout.RowCount);
            layout.SetColumnSpan(m_Warning, 3);
            layout.RowCount++;

            AddTitle(layout, "Sugerencias");
            AddCheck(layout, m_Remember, "Recordar el último tipo usado para cada nombre de variable");
            AddCheck(layout, m_Suggest, "Sugerir un SDT, BC o dominio que se llame igual que la variable");

            var clearHistory = new LinkLabel { Text = "Borrar historial de tipos recientes", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            clearHistory.LinkClicked += (s, e) =>
            {
                History.Clear();
                clearHistory.Text = "Historial borrado";
                clearHistory.Enabled = false;
            };
            layout.Controls.Add(clearHistory, 0, layout.RowCount);
            layout.SetColumnSpan(clearHistory, 3);
            layout.RowCount++;

            Controls.Add(layout);

            m_DeclareBox.KeysChanged += (s, e) => OnChanged();
            m_DeclareAllBox.KeysChanged += (s, e) => OnChanged();
            m_Remember.CheckedChanged += (s, e) => OnChanged();
            m_Suggest.CheckedChanged += (s, e) => OnChanged();
        }

        public void LoadFromSettings(IParentDesigner parent)
        {
            m_Parent = parent;
            m_Loading = true;
            m_DeclareBox.Keys = Settings.DeclareKey;
            m_DeclareAllBox.Keys = Settings.DeclareAllKey;
            m_Remember.Checked = Settings.RememberByName;
            m_Suggest.Checked = Settings.SuggestByName;
            m_Loading = false;
            UpdateWarning();
        }

        public bool SaveToSettings()
        {
            Settings.DeclareKey = m_DeclareBox.Keys;
            Settings.DeclareAllKey = m_DeclareAllBox.Keys;
            Settings.RememberByName = m_Remember.Checked;
            Settings.SuggestByName = m_Suggest.Checked;
            Settings.Save();
            return true;
        }

        void OnChanged()
        {
            if (m_Loading)
                return;
            UpdateWarning();
            if (m_Parent != null)
                m_Parent.Dirty = true;
        }

        void UpdateWarning()
        {
            Keys declare = m_DeclareBox.Keys;
            Keys declareAll = m_DeclareAllBox.Keys;
            string warning = null;
            if (declare != Keys.None && declare == declareAll)
                warning = "Los dos comandos tienen el mismo atajo: solo va a funcionar el primero.";
            else if (Shortcuts.IsUsedByGeneXus(declare) || Shortcuts.IsUsedByGeneXus(declareAll))
                warning = "GeneXus ya usa " + string.Join(" y ", new[] { declare, declareAll }
                              .Where(Shortcuts.IsUsedByGeneXus).Select(Shortcuts.Format)) +
                          ". Dentro del editor de código va a ganar GxQuickVar; en el resto del IDE sigue el de GeneXus.";
            m_Warning.Text = warning ?? string.Empty;
            m_Warning.Visible = warning != null;
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
                MaximumSize = new Size(520, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 0, 3, 8),
            };
            layout.Controls.Add(label, 0, layout.RowCount);
            layout.SetColumnSpan(label, 3);
            layout.RowCount++;
        }

        static void AddShortcutRow(TableLayoutPanel layout, string caption, ShortcutBox box, Keys defaultKeys)
        {
            int row = layout.RowCount++;
            layout.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) }, 0, row);
            box.Dock = DockStyle.Fill;
            layout.Controls.Add(box, 1, row);
            var reset = new LinkLabel { Text = "Restablecer (" + Shortcuts.Format(defaultKeys) + ")", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 3, 3) };
            reset.LinkClicked += (s, e) => box.Keys = defaultKeys;
            layout.Controls.Add(reset, 2, row);
        }

        static void AddCheck(TableLayoutPanel layout, CheckBox check, string text)
        {
            check.Text = text;
            check.AutoSize = true;
            layout.Controls.Add(check, 0, layout.RowCount);
            layout.SetColumnSpan(check, 3);
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
