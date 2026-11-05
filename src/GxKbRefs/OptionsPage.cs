using System.Drawing;
using System.Windows.Forms;
using Artech.Architecture.UI.Framework.Services;

namespace GxKbRefs
{
    /// <summary>Página "GxKbRefs" en Tools &gt; Options (mismo mecanismo que usa el editor de texto de GeneXus).</summary>
    sealed class OptionsCategory : ConfigurationCategory, ICategoryLoader
    {
        public OptionsCategory()
            : base("GxKbRefs", "GxKbRefs")
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
        readonly TextBox m_Folders = new TextBox();
        readonly TextBox m_Excluded = new TextBox();
        readonly CheckBox m_IncludeFixes = new CheckBox();
        readonly TextBox m_FixesPattern = new TextBox();
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

            AddTitle(layout, "Ventana References");
            AddNote(layout, "Checkbox \"Otras KBs\" (destildado al abrir la ventana): agrega debajo de Is Referenced By " +
                            "quién referencia al objeto en las demás KBs, agrupado por KB, también por servicio (un valor " +
                            "de dominio cuya descripción es la ruta del objeto). Se leen las bases de las KBs directamente, " +
                            "sin abrirlas en GeneXus.");
            AddCheck(layout, m_Enabled, "Activado (se aplica al abrir una ventana References)");

            AddTitle(layout, "Carpetas de KBs");
            AddNote(layout, "Carpetas donde están las KBs (por ejemplo C:\\KBGXSERVER\\CORE), separadas por punto y coma; " +
                            "se buscan hasta dos niveles abajo. Vacío: la carpeta que contiene a la KB abierta. Además se " +
                            "revisan las bases GX_KB_* adjuntas al servidor de la KB abierta. Una KB que nunca se abrió en " +
                            "esta máquina se adjunta al servidor con su .mdf, como hace GeneXus al abrirla.");
            m_Folders.Width = 540;
            layout.Controls.Add(m_Folders, 0, layout.RowCount++);

            AddTitle(layout, "KBs excluidas");
            AddNote(layout, "Nombres de KB (o de su base, GX_KB_...) separados por coma en los que no se busca. " +
                            "Por ejemplo, KBs de fixes o de capacitación.");
            m_Excluded.Width = 540;
            layout.Controls.Add(m_Excluded, 0, layout.RowCount++);

            AddTitle(layout, "KBs de fixes");
            AddNote(layout, "Las KBs cuyo nombre (o el de su base) contiene este texto se buscan solo si está tildado " +
                            "\"Incluir Fixes\" en la ventana References.");
            m_FixesPattern.Width = 200;
            layout.Controls.Add(m_FixesPattern, 0, layout.RowCount++);
            AddCheck(layout, m_IncludeFixes, "\"Incluir Fixes\" tildado al abrir la ventana");

            AddTitle(layout, "Diagnóstico");
            AddCheck(layout, m_Log, "Registrar errores en " + Log.FilePath);

            Controls.Add(layout);

            m_Enabled.CheckedChanged += (s, e) => OnChanged();
            m_Folders.TextChanged += (s, e) => OnChanged();
            m_Excluded.TextChanged += (s, e) => OnChanged();
            m_FixesPattern.TextChanged += (s, e) => OnChanged();
            m_IncludeFixes.CheckedChanged += (s, e) => OnChanged();
            m_Log.CheckedChanged += (s, e) => OnChanged();
        }

        public void LoadFromSettings(IParentDesigner parent)
        {
            m_Parent = parent;
            m_Loading = true;
            m_Enabled.Checked = Settings.Enabled;
            m_Folders.Text = Settings.KbFolders;
            m_Excluded.Text = Settings.ExcludedKbs;
            m_FixesPattern.Text = Settings.FixesPattern;
            m_IncludeFixes.Checked = Settings.IncludeFixes;
            m_Log.Checked = Settings.LogEnabled;
            m_Loading = false;
        }

        public bool SaveToSettings()
        {
            Settings.Enabled = m_Enabled.Checked;
            Settings.KbFolders = m_Folders.Text.Trim();
            Settings.ExcludedKbs = m_Excluded.Text.Trim();
            Settings.FixesPattern = m_FixesPattern.Text;
            Settings.IncludeFixes = m_IncludeFixes.Checked;
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
