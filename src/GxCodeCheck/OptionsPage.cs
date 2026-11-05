using System;
using System.Drawing;
using System.Windows.Forms;
using Artech.Architecture.UI.Framework.Services;

namespace GxCodeCheck
{
    /// <summary>Página "GxCodeCheck" en Tools &gt; Options (mismo mecanismo que usa el editor de texto de GeneXus).</summary>
    sealed class OptionsCategory : ConfigurationCategory, ICategoryLoader
    {
        public OptionsCategory()
            : base("GxCodeCheck", "GxCodeCheck")
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
        readonly CheckBox m_Enabled = new CheckBox();
        readonly NumericUpDown m_SpecifierDelay = new NumericUpDown();
        readonly CheckBox m_Specifier = new CheckBox();
        readonly CheckBox m_QuickTypes = new CheckBox();
        readonly CheckBox m_CheckCallers = new CheckBox();
        readonly CheckBox m_Warnings = new CheckBox();
        readonly CheckBox m_Standards = new CheckBox();
        readonly CheckedListBox m_Rules = new CheckedListBox();
        readonly ShortcutBox m_FixKey = new ShortcutBox();
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
                ColumnCount = 1,
                Padding = new Padding(8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddTitle(layout, "Errores en el código");
            AddNote(layout, "Al guardar, subraya los errores que encuentra GeneXus (rojo los errores, naranja las " +
                            "advertencias, gris lo que es consecuencia de otro error) y revisa paréntesis, comillas y " +
                            "líneas que terminan en un operador. Con el mouse encima se ve la explicación y el mensaje " +
                            "de GeneXus, que se puede copiar. Mientras se escribe no revisa nada.");
            AddCheck(layout, m_Enabled, "Activado");
            AddNote(layout, "Las correcciones (cambiar un nombre mal escrito, borrar un punto suelto, declarar una " +
                            "variable con GxQuickVar...) aparecen abajo en el cartel. Con el cursor sobre lo marcado, " +
                            "el atajo abre el cartel para elegirlas con el teclado (Tab y Enter, o su número).");
            var fixRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            fixRow.Controls.Add(new Label { Text = "Atajo de las correcciones:", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
            m_FixKey.Width = 140;
            fixRow.Controls.Add(m_FixKey);
            var resetFix = new LinkLabel { Text = "Restablecer (" + Shortcuts.Format(Settings.DefaultFixKey) + ")", AutoSize = true, Margin = new Padding(8, 6, 3, 0) };
            resetFix.LinkClicked += (s, e) => m_FixKey.Keys = Settings.DefaultFixKey;
            fixRow.Controls.Add(resetFix);
            layout.Controls.Add(fixRow, 0, layout.RowCount++);

            AddTitle(layout, "Especificación");
            AddNote(layout, "GeneXus encuentra los errores de tipos y de parámetros al especificar, en segundo plano " +
                            "después de guardar (el Output \"Background Build\") y en cada build: tarda varios segundos. " +
                            "El chequeo rápido revisa al guardar, en milisegundos, los casos comunes (parámetros de los " +
                            "procedimientos, métodos, asignaciones, variables no definidas); cuando termina la " +
                            "especificación quedan sus errores, que son los que valen.");
            AddCheck(layout, m_QuickTypes, "Chequeo rápido de tipos al guardar");
            AddCheck(layout, m_CheckCallers, "Al cambiar el parm() de un procedimiento, revisar los objetos que lo llaman");
            AddCheck(layout, m_Specifier, "Marcar los errores de la especificación");
            AddCheck(layout, m_Warnings, "Marcar también las advertencias");
            var delayRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            m_SpecifierDelay.Minimum = 0;
            m_SpecifierDelay.Maximum = Settings.MaxSpecifierDelay;
            m_SpecifierDelay.Increment = 100;
            m_SpecifierDelay.Width = 70;
            delayRow.Controls.Add(new Label { Text = "Especificar", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
            delayRow.Controls.Add(m_SpecifierDelay);
            delayRow.Controls.Add(new Label { Text = "milisegundos después de guardar (GeneXus trae 3000)", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
            layout.Controls.Add(delayRow, 0, layout.RowCount++);

            AddTitle(layout, "Estándar del equipo");
            AddNote(layout, "Al guardar, marca como advertencia lo que GeneXus acepta pero el estándar no: un literal " +
                            "donde va un valor de dominio, .Clone() en vez de new(), Commit en vez de prcCommit.Call(), " +
                            "submit directo, funciones o 'and' en un Where... Cada regla se puede apagar.");
            AddCheck(layout, m_Standards, "Marcar lo que no sigue el estándar");
            m_Rules.CheckOnClick = true;
            m_Rules.IntegralHeight = false;
            m_Rules.Width = 560;
            m_Rules.Height = (TypeChecker.StandardRules.Length + 1) * m_Rules.ItemHeight;
            m_Rules.Margin = new Padding(20, 0, 3, 3);
            foreach (var (_, title) in TypeChecker.StandardRules)
                m_Rules.Items.Add(title);
            layout.Controls.Add(m_Rules, 0, layout.RowCount++);

            AddTitle(layout, "Diagnóstico");
            AddCheck(layout, m_Log, "Registrar errores en " + Log.FilePath);

            Controls.Add(layout);

            m_Enabled.CheckedChanged += (s, e) => OnChanged();
            m_SpecifierDelay.ValueChanged += (s, e) => OnChanged();
            m_Specifier.CheckedChanged += (s, e) => OnChanged();
            m_QuickTypes.CheckedChanged += (s, e) => OnChanged();
            m_CheckCallers.CheckedChanged += (s, e) => OnChanged();
            m_Warnings.CheckedChanged += (s, e) => OnChanged();
            m_Standards.CheckedChanged += (s, e) => { m_Rules.Enabled = m_Standards.Checked; OnChanged(); };
            m_Rules.ItemCheck += (s, e) => OnChanged();
            m_FixKey.KeysChanged += (s, e) => OnChanged();
            m_Log.CheckedChanged += (s, e) => OnChanged();
        }

        public void LoadFromSettings(IParentDesigner parent)
        {
            m_Parent = parent;
            m_Loading = true;
            m_Enabled.Checked = Settings.Enabled;
            m_SpecifierDelay.Value = Settings.SpecifierDelay;
            m_Specifier.Checked = Settings.ShowSpecifier;
            m_QuickTypes.Checked = Settings.QuickTypes;
            m_CheckCallers.Checked = Settings.CheckCallers;
            m_Warnings.Checked = Settings.ShowWarnings;
            m_FixKey.Keys = Settings.FixKey;
            m_Standards.Checked = Settings.Standards;
            m_Rules.Enabled = Settings.Standards;
            for (int i = 0; i < TypeChecker.StandardRules.Length; i++)
                m_Rules.SetItemChecked(i, !Settings.StandardsOff.Contains(TypeChecker.StandardRules[i].Code));
            m_Log.Checked = Settings.LogEnabled;
            m_Loading = false;
        }

        public bool SaveToSettings()
        {
            Settings.Enabled = m_Enabled.Checked;
            Settings.SpecifierDelay = (int)m_SpecifierDelay.Value;
            Settings.ShowSpecifier = m_Specifier.Checked;
            Settings.QuickTypes = m_QuickTypes.Checked;
            Settings.CheckCallers = m_CheckCallers.Checked;
            Settings.ShowWarnings = m_Warnings.Checked;
            Settings.FixKey = m_FixKey.Keys;
            Settings.Standards = m_Standards.Checked;
            Settings.StandardsOff.Clear();
            for (int i = 0; i < TypeChecker.StandardRules.Length; i++)
            {
                if (!m_Rules.GetItemChecked(i))
                    Settings.StandardsOff.Add(TypeChecker.StandardRules[i].Code);
            }
            Settings.LogEnabled = m_Log.Checked;
            Settings.Save();
            CodeCheckPackage.RefreshAll();
            return true;
        }

        void OnChanged()
        {
            if (m_Loading)
                return;
            if (m_Parent != null)
                m_Parent.Dirty = true;
        }

        static void AddTitle(TableLayoutPanel layout, string text)
        {
            layout.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Margin = new Padding(3, 10, 3, 4),
            }, 0, layout.RowCount++);
        }

        static void AddNote(TableLayoutPanel layout, string text)
        {
            layout.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 0, 3, 8),
            }, 0, layout.RowCount++);
        }

        static void AddCheck(TableLayoutPanel layout, CheckBox check, string text)
        {
            check.Text = text;
            check.AutoSize = true;
            layout.Controls.Add(check, 0, layout.RowCount++);
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
