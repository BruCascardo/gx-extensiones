using System;
using System.Drawing;
using System.Windows.Forms;
using Artech.Architecture.UI.Framework.Services;

namespace GxCallParms
{
    /// <summary>Página "GxCallParms" en Tools &gt; Options (mismo mecanismo que usa el editor de texto de GeneXus).</summary>
    sealed class OptionsCategory : ConfigurationCategory, ICategoryLoader
    {
        public OptionsCategory()
            : base("GxCallParms", "GxCallParms")
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
        readonly ShortcutBox m_FillBox = new ShortcutBox();
        readonly Label m_Warning = new Label();
        readonly CheckBox m_AutoFill = new CheckBox();
        readonly CheckBox m_CallToUdp = new CheckBox();
        readonly CheckBox m_Log = new CheckBox();
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

            AddTitle(layout, "Completar llamadas");
            AddCheck(layout, m_AutoFill, "Completar los parámetros al escribir el \"(\" de Objeto(, Objeto.call( u Objeto.udp(");
            AddCheck(layout, m_CallToUdp, "Con un único parámetro de salida (el último), escribir .call( como &&Salida = Objeto.udp(...)");

            AddTitle(layout, "Atajo de teclado");
            AddNote(layout, "Completa la llamada bajo el cursor si tiene los paréntesis vacíos. " +
                            "Hacé clic en el campo y apretá la combinación; Supr o Retroceso lo deja sin atajo.");
            AddShortcutRow(layout, "Completar parámetros de la llamada:", m_FillBox, Settings.DefaultFillKey);

            m_Warning.AutoSize = true;
            m_Warning.MaximumSize = new Size(560, 0);
            m_Warning.ForeColor = Color.FromArgb(0xB2, 0x5E, 0x00);
            m_Warning.Margin = new Padding(3, 2, 3, 8);
            layout.Controls.Add(m_Warning, 0, layout.RowCount);
            layout.SetColumnSpan(m_Warning, 3);
            layout.RowCount++;

            AddTitle(layout, "Diagnóstico");
            AddCheck(layout, m_Log, "Registrar lo que detecta y completa en " + Log.FilePath);

            Controls.Add(layout);

            m_FillBox.KeysChanged += (s, e) => OnChanged();
            m_AutoFill.CheckedChanged += (s, e) => OnChanged();
            m_CallToUdp.CheckedChanged += (s, e) => OnChanged();
            m_Log.CheckedChanged += (s, e) => OnChanged();
        }

        public void LoadFromSettings(IParentDesigner parent)
        {
            m_Parent = parent;
            m_Loading = true;
            m_FillBox.Keys = Settings.FillKey;
            m_AutoFill.Checked = Settings.AutoFill;
            m_CallToUdp.Checked = Settings.CallToUdp;
            m_Log.Checked = Settings.LogEnabled;
            m_Loading = false;
            UpdateWarning();
        }

        public bool SaveToSettings()
        {
            Settings.FillKey = m_FillBox.Keys;
            Settings.AutoFill = m_AutoFill.Checked;
            Settings.CallToUdp = m_CallToUdp.Checked;
            Settings.LogEnabled = m_Log.Checked;
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
            Keys fill = m_FillBox.Keys;
            string warning = Shortcuts.IsUsedByGeneXus(fill)
                ? "GeneXus ya usa " + Shortcuts.Format(fill) + ". Dentro del editor de código va a ganar GxCallParms; en el resto del IDE sigue el de GeneXus."
                : null;
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
