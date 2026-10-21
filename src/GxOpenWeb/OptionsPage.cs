using System.Drawing;
using System.Windows.Forms;
using Artech.Architecture.UI.Framework.Services;

namespace GxOpenWeb
{
    /// <summary>Página "GxOpenWeb" en Tools &gt; Options (mismo mecanismo que usa el editor de texto de GeneXus).</summary>
    sealed class OptionsCategory : ConfigurationCategory, ICategoryLoader
    {
        public OptionsCategory()
            : base("GxOpenWeb", "GxOpenWeb")
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
        readonly TextBox m_LocalUrl = new TextBox();
        readonly TextBox m_CiUrl = new TextBox();
        readonly TextBox m_PreprodUrl = new TextBox();
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
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddTitle(layout, "URLs base");
            AddNote(layout, "Clic derecho en la pestaña de un Web Panel > Abrir lo abre en el navegador con la URL base del " +
                            "ambiente seguida del paquete Java y el nombre del objeto con su módulo, en minúsculas " +
                            "(ej. com.miempresa.mikb.ventas.clientewp). " +
                            "{kb} se reemplaza por el nombre de la KB en minúsculas. " +
                            "Local vacía usa el Web root del environment Java de la KB.");
            AddUrlRow(layout, "Local:", m_LocalUrl, string.Empty);
            AddUrlRow(layout, "CI:", m_CiUrl, Settings.DefaultCiUrl);
            AddUrlRow(layout, "Preprod:", m_PreprodUrl, Settings.DefaultPreprodUrl);

            AddTitle(layout, "Diagnóstico");
            AddCheck(layout, m_Log, "Registrar las URLs que abre en " + Log.FilePath);

            Controls.Add(layout);

            m_LocalUrl.TextChanged += (s, e) => OnChanged();
            m_CiUrl.TextChanged += (s, e) => OnChanged();
            m_PreprodUrl.TextChanged += (s, e) => OnChanged();
            m_Log.CheckedChanged += (s, e) => OnChanged();
        }

        public void LoadFromSettings(IParentDesigner parent)
        {
            m_Parent = parent;
            m_Loading = true;
            m_LocalUrl.Text = Settings.LocalUrl;
            m_CiUrl.Text = Settings.CiUrl;
            m_PreprodUrl.Text = Settings.PreprodUrl;
            m_Log.Checked = Settings.LogEnabled;
            m_Loading = false;
        }

        public bool SaveToSettings()
        {
            Settings.LocalUrl = m_LocalUrl.Text.Trim();
            Settings.CiUrl = m_CiUrl.Text.Trim();
            Settings.PreprodUrl = m_PreprodUrl.Text.Trim();
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

        static void AddUrlRow(TableLayoutPanel layout, string caption, TextBox box, string defaultUrl)
        {
            int row = layout.RowCount++;
            layout.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) }, 0, row);
            box.Dock = DockStyle.Fill;
            layout.Controls.Add(box, 1, row);
            var reset = new LinkLabel { Text = "Restablecer", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 3, 3) };
            reset.LinkClicked += (s, e) => box.Text = defaultUrl;
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
}
