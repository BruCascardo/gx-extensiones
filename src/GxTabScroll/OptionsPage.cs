using System.Drawing;
using System.Windows.Forms;
using Artech.Architecture.UI.Framework.Services;

namespace GxTabScroll
{
    /// <summary>Página "GxTabScroll" en Tools &gt; Options (mismo mecanismo que usa el editor de texto de GeneXus).</summary>
    sealed class OptionsCategory : ConfigurationCategory, ICategoryLoader
    {
        public OptionsCategory()
            : base("GxTabScroll", "GxTabScroll")
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
        readonly CheckBox m_SwitchTabs = new CheckBox();
        readonly NumericUpDown m_TabsPerStep = new NumericUpDown();
        readonly CheckBox m_CloseWithCtrlW = new CheckBox();
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
                ColumnCount = 2,
                Padding = new Padding(8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddTitle(layout, "Rueda del mouse sobre las pestañas");
            AddNote(layout, "Como en VS Code: cuando no entran todas las pestañas de objetos, la rueda sobre la barra la " +
                            "desplaza sin cambiar la pestaña activa (hacia abajo = a la derecha) y Shift+rueda pasa a la " +
                            "pestaña anterior o siguiente.");
            AddCheck(layout, m_Enabled, "Activado");
            AddCheck(layout, m_SwitchTabs, "La rueda cambia de pestaña y Shift+rueda desplaza la barra");

            int row = layout.RowCount++;
            layout.Controls.Add(new Label { Text = "Pestañas por paso de la rueda:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) }, 0, row);
            m_TabsPerStep.Minimum = Settings.MinTabsPerStep;
            m_TabsPerStep.Maximum = Settings.MaxTabsPerStep;
            m_TabsPerStep.Width = 50;
            layout.Controls.Add(m_TabsPerStep, 1, row);

            AddTitle(layout, "Teclado");
            AddCheck(layout, m_CloseWithCtrlW, "Ctrl+W cierra la pestaña donde está el foco (como Ctrl+F4)");

            AddTitle(layout, "Diagnóstico");
            AddCheck(layout, m_Log, "Registrar errores en " + Log.FilePath);

            Controls.Add(layout);

            m_Enabled.CheckedChanged += (s, e) => OnChanged();
            m_SwitchTabs.CheckedChanged += (s, e) => OnChanged();
            m_TabsPerStep.ValueChanged += (s, e) => OnChanged();
            m_CloseWithCtrlW.CheckedChanged += (s, e) => OnChanged();
            m_Log.CheckedChanged += (s, e) => OnChanged();
        }

        public void LoadFromSettings(IParentDesigner parent)
        {
            m_Parent = parent;
            m_Loading = true;
            m_Enabled.Checked = Settings.Enabled;
            m_SwitchTabs.Checked = Settings.SwitchTabs;
            m_TabsPerStep.Value = Settings.TabsPerStep;
            m_CloseWithCtrlW.Checked = Settings.CloseWithCtrlW;
            m_Log.Checked = Settings.LogEnabled;
            m_Loading = false;
        }

        public bool SaveToSettings()
        {
            Settings.Enabled = m_Enabled.Checked;
            Settings.SwitchTabs = m_SwitchTabs.Checked;
            Settings.TabsPerStep = (int)m_TabsPerStep.Value;
            Settings.CloseWithCtrlW = m_CloseWithCtrlW.Checked;
            Settings.LogEnabled = m_Log.Checked;
            Settings.Save();
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
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Margin = new Padding(3, 10, 3, 4),
            };
            layout.Controls.Add(label, 0, layout.RowCount);
            layout.SetColumnSpan(label, 2);
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
            layout.SetColumnSpan(label, 2);
            layout.RowCount++;
        }

        static void AddCheck(TableLayoutPanel layout, CheckBox check, string text)
        {
            check.Text = text;
            check.AutoSize = true;
            layout.Controls.Add(check, 0, layout.RowCount);
            layout.SetColumnSpan(check, 2);
            layout.RowCount++;
        }
    }
}
