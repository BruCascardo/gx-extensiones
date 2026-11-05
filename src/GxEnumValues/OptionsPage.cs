using System.Drawing;
using System.Windows.Forms;
using Artech.Architecture.UI.Framework.Services;

namespace GxEnumValues
{
    /// <summary>Página "GxEnumValues" en Tools &gt; Options (mismo mecanismo que usa el editor de texto de GeneXus).</summary>
    sealed class OptionsCategory : ConfigurationCategory, ICategoryLoader
    {
        public OptionsCategory()
            : base("GxEnumValues", "GxEnumValues")
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

            AddTitle(layout, "Diálogo Enum Values");
            AddNote(layout, "En la propiedad Enum Values de dominios y atributos: una barra de búsqueda (Ctrl+F) filtra por " +
                            "nombre, descripción y valor, y el clic en un encabezado ordena por esa columna (ascendente, " +
                            "descendente y de nuevo el orden original). El orden que queda en la lista es el que se guarda.");
            AddCheck(layout, m_Enabled, "Activado (se aplica al abrir el diálogo)");

            AddTitle(layout, "Diagnóstico");
            AddCheck(layout, m_Log, "Registrar errores en " + Log.FilePath);

            Controls.Add(layout);

            m_Enabled.CheckedChanged += (s, e) => OnChanged();
            m_Log.CheckedChanged += (s, e) => OnChanged();
        }

        public void LoadFromSettings(IParentDesigner parent)
        {
            m_Parent = parent;
            m_Loading = true;
            m_Enabled.Checked = Settings.Enabled;
            m_Log.Checked = Settings.LogEnabled;
            m_Loading = false;
        }

        public bool SaveToSettings()
        {
            Settings.Enabled = m_Enabled.Checked;
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
}
