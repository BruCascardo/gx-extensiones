using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Artech.Architecture.Common.Location;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Diagnostics;
using Artech.Udm.Framework;

namespace GxCodeCheck
{
    /// <summary>
    /// Tools &gt; GxCodeCheck: revisar el código. Elige qué revisar (lo modificado desde una fecha con lo que lo
    /// usa, un módulo o la KB entera), lo revisa de a un objeto por vez sin trabar GeneXus y deja lo encontrado
    /// en el Output "GxCodeCheck" (con doble clic se abre el objeto en la línea) y subrayado en los editores.
    /// </summary>
    sealed class ReviewForm : Form
    {
        public const string OutputId = "GxCodeCheck";
        const string SectionId = "GxCodeCheckReview";
        // Cuánto trabaja en cada tick antes de devolverle el control a GeneXus
        const int SliceMilliseconds = 60;

        readonly KBModel m_Model;
        readonly RadioButton m_Mine = new RadioButton { Text = "Mis cambios pendientes de commit (lo que vas a subir; no lo que trajo un Update)", AutoSize = true };
        readonly RadioButton m_Changed = new RadioButton { Text = "Todo lo modificado desde", AutoSize = true };
        readonly DateTimePicker m_Since = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm", Width = 140 };
        readonly CheckBox m_WithUsers = new CheckBox { Text = "y los objetos que usan lo cambiado (los que el cambio puede romper)", AutoSize = true };
        readonly RadioButton m_Module = new RadioButton { Text = "El módulo", AutoSize = true };
        readonly ComboBox m_Modules = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        readonly RadioButton m_Kb = new RadioButton { Text = "La KB entera", AutoSize = true };
        readonly CheckBox m_Warnings = new CheckBox { Text = "Incluir las advertencias y el estándar del equipo", AutoSize = true };
        readonly ProgressBar m_Progress = new ProgressBar { Width = 520, Height = 16, Visible = false };
        readonly Label m_Status = new Label { AutoSize = true, MaximumSize = new Size(520, 0) };
        readonly Button m_Start = new Button { Text = "Revisar", AutoSize = true };
        readonly Button m_Close = new Button { Text = "Cerrar", AutoSize = true };
        readonly Timer m_Timer = new Timer { Interval = 1 };

        // La revisión en curso
        List<EntityKey> m_Pending;
        int m_Next;
        int m_Checked;
        int m_WithErrors;
        int m_Errors;
        int m_WarningsFound;
        // Los que no se revisan por tener Generate Object en False
        int m_NotGenerated;
        readonly Stopwatch m_Time = new Stopwatch();
        KbCheck.Scope m_Scope;
        bool m_Running;
        // El Output donde va la revisión: el propio, o el de siempre si GeneXus no lo acepta
        string m_Output = OutputId;

        public ReviewForm(KBModel model, string currentModule)
        {
            m_Model = model;
            Text = "GxCodeCheck: revisar el código";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AcceptButton = m_Start;
            CancelButton = m_Close;

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12), Dock = DockStyle.Fill };
            void Add(Control control, int indent = 0)
            {
                control.Margin = new Padding(3 + indent, 3, 3, 3);
                layout.Controls.Add(control, 0, layout.RowCount++);
            }
            Add(new Label
            {
                Text = "Revisa sin build lo que hace fallar un build: parámetros de las llamadas contra el parm(), " +
                       "tipos, miembros de los SDT, valores de los dominios, objetos que no existen. Tarda unas " +
                       "decenas de milisegundos por objeto (lo que GeneXus tarda en leerlo). No reemplaza al build: " +
                       "no revisa la navegación ni los Data Providers.",
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                ForeColor = SystemColors.GrayText,
            });
            Add(new Label { Text = "Qué revisar", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
            Add(m_Mine);
            var changedRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
            changedRow.Controls.Add(m_Changed);
            changedRow.Controls.Add(m_Since);
            Add(changedRow);
            Add(m_WithUsers, 20);
            var moduleRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
            moduleRow.Controls.Add(m_Module);
            moduleRow.Controls.Add(m_Modules);
            Add(moduleRow);
            Add(m_Kb);
            Add(m_Warnings);
            Add(m_Progress);
            Add(m_Status);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(m_Close);
            buttons.Controls.Add(m_Start);
            Add(buttons);
            Controls.Add(layout);

            m_Modules.Items.Add(RootModule);
            foreach (string module in KbCheck.Modules(model))
                m_Modules.Items.Add(module);
            LoadSettings(currentModule);

            // Cada opción está en su renglón (otro contenedor): WinForms no las agrupa solo
            foreach (RadioButton option in Options)
                option.CheckedChanged += (s, e) => OnOptionChanged((RadioButton)s);
            m_Since.ValueChanged += (s, e) => m_Changed.Checked = true;
            m_Modules.SelectedIndexChanged += (s, e) => m_Module.Checked = true;
            m_Start.Click += (s, e) => Start();
            // Revisando, cancela (y deja ver el resumen); si no, cierra
            m_Close.Click += (s, e) =>
            {
                if (m_Running)
                    Finish(cancelled: true);
                else
                    Close();
            };
            m_Timer.Tick += OnTick;
        }

        const string RootModule = "(Módulo raíz y sus submódulos)";

        void OnOptionChanged(RadioButton option)
        {
            if (!option.Checked)
                return;
            foreach (RadioButton other in Options)
            {
                if (other != option)
                    other.Checked = false;
            }
            m_WithUsers.Enabled = HasUsers && !m_Running;
        }

        RadioButton[] Options => new[] { m_Mine, m_Changed, m_Module, m_Kb };

        // "Los que usan lo cambiado" vale para los cambios, no para un módulo o la KB (que ya los incluyen)
        bool HasUsers => m_Mine.Checked || m_Changed.Checked;

        void LoadSettings(string currentModule)
        {
            m_Since.Value = Settings.ReviewSince > m_Since.MinDate ? Settings.ReviewSince : DateTime.Today;
            m_WithUsers.Checked = Settings.ReviewWithUsers;
            m_Warnings.Checked = Settings.ReviewWarnings;
            string module = !string.IsNullOrEmpty(currentModule) ? currentModule : Settings.ReviewModule;
            int index = string.IsNullOrEmpty(module) ? 0 : m_Modules.Items.IndexOf(module);
            m_Modules.SelectedIndex = Math.Max(0, index);
            switch (Settings.ReviewScope)
            {
                case KbCheck.ScopeKind.Module:
                    m_Module.Checked = true;
                    break;
                case KbCheck.ScopeKind.Kb:
                    m_Kb.Checked = true;
                    break;
                case KbCheck.ScopeKind.ChangedSince:
                    m_Changed.Checked = true;
                    break;
                default:
                    m_Mine.Checked = true;
                    break;
            }
        }

        KbCheck.Scope ReadScope()
        {
            string module = m_Modules.SelectedIndex <= 0 ? "" : (string)m_Modules.SelectedItem;
            var scope = new KbCheck.Scope
            {
                Kind = m_Kb.Checked ? KbCheck.ScopeKind.Kb : m_Module.Checked ? KbCheck.ScopeKind.Module
                    : m_Changed.Checked ? KbCheck.ScopeKind.ChangedSince : KbCheck.ScopeKind.Pending,
                Module = module,
                Since = m_Since.Value,
                WithUsers = m_WithUsers.Checked,
            };
            Settings.ReviewScope = scope.Kind;
            Settings.ReviewModule = module;
            Settings.ReviewSince = scope.Since;
            Settings.ReviewWithUsers = scope.WithUsers;
            Settings.ReviewWarnings = m_Warnings.Checked;
            Settings.Save();
            return scope;
        }

        void Start()
        {
            if (m_Running)
                return;
            m_Scope = ReadScope();
            if (m_Scope.Kind == KbCheck.ScopeKind.Module && string.IsNullOrEmpty(m_Scope.Module))
                m_Scope.Kind = KbCheck.ScopeKind.Kb;
            m_Status.Text = "Buscando los objetos a revisar...";
            m_Status.Refresh();
            m_Time.Restart();
            try
            {
                m_Pending = KbCheck.List(m_Model, m_Scope, out int changed);
                if (m_Scope.Kind == KbCheck.ScopeKind.ChangedSince || m_Scope.Kind == KbCheck.ScopeKind.Pending)
                    Log.Write($"Revisión de {m_Scope}: {changed} objeto(s) cambiado(s), {m_Pending.Count} a revisar");
            }
            catch (Exception ex)
            {
                Log.Write("Error al listar los objetos a revisar: " + ex);
                m_Status.Text = "No se pudo armar la lista de objetos: " + ex.Message;
                return;
            }
            if (m_Pending.Count == 0)
            {
                m_Status.Text = $"No hay objetos para revisar en {m_Scope} (se revisan procedimientos, Web Panels y transacciones).";
                return;
            }

            m_Next = m_Checked = m_WithErrors = m_Errors = m_WarningsFound = m_NotGenerated = 0;
            m_Running = true;
            SetRunning(true);
            m_Progress.Maximum = m_Pending.Count;
            m_Progress.Value = 0;
            IOutputService output = CommonServices.Output;
            try
            {
                m_Output = OutputId;
                output.Clear(m_Output);
                output.Show(m_Output);
                output.SelectOutput(m_Output);
            }
            catch (Exception ex)
            {
                Log.Write($"Output propio no disponible ({ex.Message}): va al de siempre");
                m_Output = output.GetDefaultOutputId();
            }
            output.StartSection(m_Output, SectionId, $"GxCodeCheck: revisión de {m_Scope} ({m_Pending.Count} objetos)");
            m_Timer.Start();
        }

        void SetRunning(bool running)
        {
            m_Progress.Visible = running;
            m_Start.Enabled = !running;
            m_Close.Text = running ? "Cancelar" : "Cerrar";
            foreach (Control control in new Control[] { m_Mine, m_Changed, m_Since, m_Module, m_Modules, m_Kb, m_Warnings })
                control.Enabled = !running;
            m_WithUsers.Enabled = !running && HasUsers;
        }

        void OnTick(object sender, EventArgs e)
        {
            var slice = Stopwatch.StartNew();
            try
            {
                while (m_Next < m_Pending.Count && (slice.ElapsedMilliseconds < SliceMilliseconds || m_Next == 0))
                    CheckOne(m_Pending[m_Next++]);
            }
            catch (Exception ex)
            {
                Log.Write("Error en la revisión: " + ex);
            }
            m_Progress.Value = m_Next;
            if (m_Next >= m_Pending.Count)
                Finish(cancelled: false);
        }

        void CheckOne(EntityKey key)
        {
            KBObject obj;
            try
            {
                obj = KBObject.Get(m_Model, key);
                if (obj == null || KbCheck.Skip(obj))
                {
                    if (obj != null && KbCheck.IsNotGenerated(obj))
                        m_NotGenerated++;
                    return;
                }
                m_Status.Text = $"Revisando {m_Next} de {m_Pending.Count}: {obj.QualifiedName}";
                bool warnings = m_Warnings.Checked;
                var found = KbCheck.Check(obj, d => warnings && d.Severity == Severity.Warning
                                                    && (d.Origin != Origin.Standard || Settings.IsStandardOn(d.Code)));
                m_Checked++;
                KbCheck.Remember(obj.QualifiedName.ToString(), found);
                if (found.Count == 0)
                    return;
                int errors = found.Count(f => f.Diagnostic.Severity == Severity.Error);
                if (errors > 0)
                    m_WithErrors++;
                m_Errors += errors;
                m_WarningsFound += found.Count - errors;
                foreach (var (part, d) in found.OrderBy(f => f.Diagnostic.Line))
                {
                    var position = new SourcePosition(part, d.Line + 1, d.Column + 1);
                    MessageLevel level = d.Severity == Severity.Error ? MessageLevel.Error : MessageLevel.Warning;
                    CommonServices.Output.Add(m_Output, new OutputError(d.Code, $"{obj.QualifiedName}: {d.Message}", level, position));
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Error al revisar {key}: {ex.Message}");
            }
        }

        void Finish(bool cancelled)
        {
            m_Timer.Stop();
            m_Running = false;
            m_Time.Stop();
            SetRunning(false);
            string seconds = (m_Time.ElapsedMilliseconds / 1000.0).ToString("0.0");
            string summary = (cancelled ? $"Cancelado: revisados {m_Checked} de {m_Pending.Count} objetos" : $"Revisados {m_Checked} objetos")
                + $" en {seconds} s: "
                + (m_Errors == 0 ? "sin errores" : $"{m_Errors} error(es) en {m_WithErrors} objeto(s)")
                + (m_WarningsFound > 0 ? $", {m_WarningsFound} advertencia(s)" : "")
                + (m_NotGenerated > 0 ? $" ({m_NotGenerated} sin revisar por tener Generate Object en False)" : "")
                + (m_Errors + m_WarningsFound > 0 ? ". Están en el Output: con doble clic se abre el objeto en la línea." : ".");
            m_Status.Text = summary;
            IOutputService output = CommonServices.Output;
            output.AddLine(m_Output, "GxCodeCheck: " + summary);
            output.EndSection(m_Output, SectionId, $"GxCodeCheck: revisión de {m_Scope}", m_Errors == 0);
            Log.Write($"Revisión de {m_Scope}: {summary}");
            CodeCheckPackage.Status("GxCodeCheck: " + summary);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (m_Running)
                Finish(cancelled: true);
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                m_Timer.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Abre el diálogo, con el módulo del objeto que se está editando ya elegido.</summary>
        public static void Show(KBModel model, string currentModule)
        {
            if (model == null)
            {
                CodeCheckPackage.Status("GxCodeCheck: abrí una KB para revisar su código");
                return;
            }
            using (var form = new ReviewForm(model, currentModule))
                form.ShowDialog(Form.ActiveForm);
        }
    }
}
