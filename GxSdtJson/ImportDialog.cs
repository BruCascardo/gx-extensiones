using System;
using System.Drawing;
using System.Windows.Forms;

namespace GxSdtJson
{
    enum ImportMode
    {
        Replace,
        Append,
        New,
    }

    sealed class ImportRequest
    {
        public SdtNode Node;
        public ImportMode Mode;
        public string NewName;
    }

    /// <summary>
    /// Pegar el JSON y elegir qué hacer: reemplazar la estructura del SDT abierto, agregar al final
    /// o crear un SDT nuevo. <c>apply</c> hace la importación; si devuelve false el diálogo queda abierto.
    /// </summary>
    sealed class ImportDialog : Form
    {
        readonly Func<ImportRequest, bool> m_Apply;
        readonly TextBox m_Json;
        readonly Label m_Preview;
        readonly RadioButton m_Replace;
        readonly RadioButton m_Append;
        readonly RadioButton m_New;
        readonly TextBox m_NewName;
        readonly Button m_Ok;

        SdtNode m_Node;
        bool m_NameEdited;

        /// <param name="targetName">SDT abierto donde importar, o null si solo se puede crear uno nuevo.</param>
        /// <param name="targetItems">Cantidad de ítems del SDT abierto (para elegir el modo por defecto).</param>
        /// <param name="newLocation">Dónde se crea el SDT nuevo (módulo o carpeta), para mostrarlo.</param>
        public ImportDialog(string targetName, int targetItems, string newLocation, Func<ImportRequest, bool> apply)
        {
            m_Apply = apply;

            Text = "Importar SDT desde JSON";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(640, 520);
            MinimumSize = new Size(480, 400);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 1,
                RowCount = 6,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Pegá el JSON copiado con \"Copiar SDT como JSON\" (o escribilo a mano):",
                Margin = new Padding(0, 0, 0, 4),
            });

            m_Json = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.75f),
                MaxLength = 0,
            };
            m_Json.TextChanged += (s, e) => Parse();
            layout.Controls.Add(m_Json);

            m_Preview = new Label { AutoSize = true, Margin = new Padding(0, 4, 0, 8) };
            layout.Controls.Add(m_Preview);

            var modes = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top, Margin = new Padding(0) };
            modes.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            m_Replace = new RadioButton { AutoSize = true, Text = targetName == null ? "Reemplazar la estructura del SDT abierto" : $"Reemplazar la estructura de {targetName}" };
            m_Append = new RadioButton { AutoSize = true, Text = targetName == null ? "Agregar al final del SDT abierto" : $"Agregar al final de {targetName}" };
            m_New = new RadioButton { AutoSize = true, Text = "Crear un SDT nuevo:" };
            m_NewName = new TextBox { Width = 260, Anchor = AnchorStyles.Left };
            m_NewName.TextChanged += (s, e) => m_NameEdited = m_NewName.Focused;
            m_NewName.Enter += (s, e) => m_New.Checked = true;

            modes.Controls.Add(m_Replace);
            modes.SetColumnSpan(m_Replace, 2);
            modes.Controls.Add(m_Append);
            modes.SetColumnSpan(m_Append, 2);
            modes.Controls.Add(m_New);
            modes.Controls.Add(m_NewName);
            if (!string.IsNullOrEmpty(newLocation))
            {
                var where = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = "en " + newLocation, Margin = new Padding(20, 0, 0, 0) };
                modes.Controls.Add(where);
                modes.SetColumnSpan(where, 2);
            }
            layout.Controls.Add(modes);

            var hint = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 8, 0, 0),
                Text = targetName == null
                    ? "Abrí un SDT para importar en él."
                    : "Los cambios quedan sin grabar en el editor: si no te sirven, cerrá el SDT sin grabar.",
            };
            layout.Controls.Add(hint);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
            m_Ok = new Button { Text = "Importar", AutoSize = true };
            m_Ok.Click += (s, e) => Accept();
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(m_Ok);
            layout.Controls.Add(buttons);

            Controls.Add(layout);
            AcceptButton = null;   // Enter es salto de línea en el JSON
            CancelButton = cancel;

            if (targetName == null)
            {
                m_Replace.Enabled = m_Append.Enabled = false;
                m_New.Checked = true;
            }
            else if (targetItems == 0)
            {
                m_Replace.Checked = true;
            }
            else
            {
                m_Append.Checked = true;
            }

            string clipboard = ClipboardText();
            if (clipboard.Length > 0 && (clipboard[0] == '{' || clipboard[0] == '['))
                m_Json.Text = clipboard;
            Parse();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            m_Json.Focus();
            m_Json.Select(0, 0);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Enter))
            {
                Accept();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void Parse()
        {
            try
            {
                m_Node = SdtJsonFormat.Read(m_Json.Text);
                m_Preview.ForeColor = SystemColors.ControlText;
                string name = string.IsNullOrEmpty(m_Node.Name) ? "" : m_Node.Name + " · ";
                m_Preview.Text = $"{name}{m_Node.CountMembers()} miembros" + (m_Node.Collection ? " · colección" : "") + "   (Ctrl+Enter importa)";
                if (!m_NameEdited && !string.IsNullOrEmpty(m_Node.Name))
                    m_NewName.Text = m_Node.Name;
            }
            catch (FormatException ex)
            {
                m_Node = null;
                m_Preview.ForeColor = Color.Firebrick;
                m_Preview.Text = ex.Message;
            }
            m_Ok.Enabled = m_Node != null;
        }

        void Accept()
        {
            if (m_Node == null)
                return;
            var request = new ImportRequest
            {
                Node = m_Node,
                Mode = m_Replace.Checked ? ImportMode.Replace : m_Append.Checked ? ImportMode.Append : ImportMode.New,
                NewName = m_NewName.Text.Trim(),
            };
            if (request.Mode == ImportMode.New && request.NewName.Length == 0)
            {
                MessageBox.Show(this, "Escribí el nombre del SDT nuevo.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                m_NewName.Focus();
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                if (!m_Apply(request))
                    return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            DialogResult = DialogResult.OK;
        }

        static string ClipboardText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText().TrimStart() : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;   // portapapeles tomado por otra aplicación
            }
        }
    }
}
