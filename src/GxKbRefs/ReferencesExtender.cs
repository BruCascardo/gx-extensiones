using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Artech.Architecture.Common.Descriptors;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Packages;
using Artech.Architecture.UI.Framework.Services;
using Artech.Core.UI.Dialogs;
using IKBConnectionInfo = Artech.Udm.Architecture.Common.IKBConnectionInfo;
using Infragistics.Win.UltraWinTree;

namespace GxKbRefs
{
    /// <summary>
    /// Amplía una ventana References sin tocar lo que ya muestra: agrega el checkbox "Otras KBs" al lado de
    /// "Group References by Type" y, cuando está tildado, un área debajo del árbol Is Referenced By con un recuadro
    /// por KB (<see cref="KbGroup"/>) con quién referencia al objeto en esa KB.
    /// </summary>
    sealed class ReferencesExtender
    {
        const string CheckName = "GxKbRefsOtherKbs";

        // Objeto analizado: CallBrowserControl.Object no cambia cuando se elige otro desde el cuadro de búsqueda
        static readonly FieldInfo AnalyzedObjectField =
            typeof(CallBrowserControl).GetField("m_AnalyzedObject", BindingFlags.Instance | BindingFlags.NonPublic);

        static Dictionary<string, KBObjectDescriptor> s_Descriptors;

        readonly CallBrowserControl m_Control;
        readonly UltraTree m_Callers;
        readonly CheckBox m_GroupByType;
        readonly CheckBox m_OtherKbs = new CheckBox();
        readonly Splitter m_Splitter = new Splitter();
        readonly Panel m_Area = new Panel();
        readonly Panel m_Header = new Panel();
        readonly Label m_Status = new Label();
        readonly CheckBox m_IncludeFixes = new CheckBox();
        readonly KbList m_List = new KbList();
        readonly Dictionary<string, KbDatabase> m_Databases = new Dictionary<string, KbDatabase>(StringComparer.OrdinalIgnoreCase);
        int m_Generation;

        public ImageList Images { get; } = new ImageList { ColorDepth = ColorDepth.Depth32Bit };
        public bool GroupByType => m_GroupByType.Checked;
        public Font TreeFont => m_Callers.Font;

        /// <summary>Medidas de fila que tomó el primer árbol dibujado; las usan todos para su alto.</summary>
        public RowMetrics Metrics { get; set; }

        ReferencesExtender(CallBrowserControl control, UltraTree callers, CheckBox groupByType, Control objectLabel)
        {
            m_Control = control;
            m_Callers = callers;
            m_GroupByType = groupByType;
            Images.ImageSize = callers.ImageList?.ImageSize ?? new Size(16, 16);

            // Checkbox a la izquierda de "Group References by Type", con su mismo anclaje
            m_OtherKbs.Name = CheckName;
            m_OtherKbs.Text = "Otras KBs";
            m_OtherKbs.AutoSize = true;
            m_OtherKbs.BackColor = Color.Transparent;
            m_OtherKbs.Anchor = groupByType.Anchor;
            groupByType.Parent.Controls.Add(m_OtherKbs);
            m_OtherKbs.Location = new Point(groupByType.Left - m_OtherKbs.PreferredSize.Width - 12, groupByType.Top);
            m_OtherKbs.BringToFront();
            new ToolTip().SetToolTip(m_OtherKbs, "Busca quién referencia a este objeto en las demás KBs del servidor\n" +
                                                 "(Tools > Options > GxKbRefs para excluir KBs)");

            // Área debajo del árbol Is Referenced By: el árbol sigue ocupando el resto del panel. Arriba, el resumen
            // y el checkbox de las KBs de fixes; abajo, un recuadro por KB uno debajo del otro
            m_Status.Dock = DockStyle.Fill;
            m_Status.TextAlign = ContentAlignment.MiddleLeft;
            m_Status.ForeColor = SystemColors.GrayText;
            m_Status.Padding = new Padding(4, 0, 0, 0);
            m_IncludeFixes.Dock = DockStyle.Right;
            m_IncludeFixes.AutoSize = true;
            m_IncludeFixes.Text = "Incluir Fixes";
            m_IncludeFixes.Checked = Settings.IncludeFixes;
            m_IncludeFixes.Padding = new Padding(0, 0, 4, 0);
            new ToolTip().SetToolTip(m_IncludeFixes, "Buscar también en las KBs de fixes (las que tienen \"" + Settings.FixesPattern + "\" en el nombre)");
            m_Header.Dock = DockStyle.Top;
            m_Header.Height = m_Callers.Font.Height + 10;
            m_Header.Controls.Add(m_Status);
            m_Header.Controls.Add(m_IncludeFixes);
            m_List.Dock = DockStyle.Fill;
            m_List.Padding = new Padding(2, 0, 2, 2);
            m_List.ClientSizeChanged += (s, e) => FitWidths();
            m_Area.Dock = DockStyle.Bottom;
            m_Area.BackColor = SystemColors.Window;
            m_Area.Controls.Add(m_List);
            m_Area.Controls.Add(m_Header);
            m_List.BringToFront();
            m_Splitter.Dock = DockStyle.Bottom;
            m_Splitter.Height = 4;

            Control host = callers.Parent;
            m_Area.Height = Math.Max(120, host.Height * 2 / 5);
            m_Area.Visible = m_Splitter.Visible = false;
            // Al acoplar, el de índice más alto va primero: el área abajo de todo, el splitter arriba de ella
            host.Controls.Add(m_Splitter);
            host.Controls.Add(m_Area);
            callers.BringToFront();

            m_OtherKbs.CheckedChanged += (s, e) => OnOtherKbsChanged();
            m_IncludeFixes.CheckedChanged += (s, e) => SearchIfChecked();
            groupByType.CheckedChanged += (s, e) => SearchIfChecked();
            // CallBrowserControl pone el nombre del objeto en este label cada vez que analiza otro
            objectLabel.TextChanged += (s, e) => SearchIfChecked();
            control.Disposed += (s, e) => m_Generation++;
        }

        public static void Attach(CallBrowserControl control)
        {
            UltraTree callers = Find<UltraTree>(control, "callersTree");
            CheckBox groupByType = Find<CheckBox>(control, "checkBox1");
            Control objectLabel = Find<Control>(control, "label1");
            if (callers?.Parent == null || groupByType?.Parent == null || objectLabel == null)
            {
                Log.Write("La ventana References no tiene los controles esperados (callersTree, checkBox1, label1)");
                return;
            }
            if (groupByType.Parent.Controls.ContainsKey(CheckName))
                return;
            new ReferencesExtender(control, callers, groupByType, objectLabel);
        }

        static T Find<T>(Control parent, string name) where T : Control =>
            parent.Controls.Find(name, true).OfType<T>().FirstOrDefault();

        void OnOtherKbsChanged()
        {
            m_Area.Visible = m_Splitter.Visible = m_OtherKbs.Checked;
            if (m_OtherKbs.Checked)
            {
                Search();
            }
            else
            {
                m_Generation++;
                ClearGroups();
            }
        }

        void SearchIfChecked()
        {
            if (m_OtherKbs.Checked && !m_Control.IsDisposed)
                Search();
        }

        void Search()
        {
            int generation = ++m_Generation;
            ClearGroups();

            IKBObject analyzed = AnalyzedObjectField?.GetValue(m_Control) as IKBObject ?? m_Control.Object;
            if (analyzed == null)
            {
                m_Status.Text = "";
                return;
            }
            IKBConnectionInfo connection = UIServices.KB.CurrentKB?.ConnectionInfo;
            if (connection == null)
            {
                m_Status.Text = "No se pudo obtener la conexión de la KB abierta.";
                return;
            }
            var search = new SearchInput
            {
                Connection    = connection,
                Location      = UIServices.KB.CurrentKB.Location,
                Guid          = analyzed.Guid,
                QualifiedName = analyzed.QualifiedName?.ToString() ?? analyzed.Name,
                Excluded      = Settings.ExcludedKbSet(),
                Folders       = Settings.KbFolderList(),
                IncludeFixes  = m_IncludeFixes.Checked,
                FixesPattern  = Settings.FixesPattern,
            };
            m_Status.Text = "Buscando en otras KBs...";
            Task.Run(() => SearchAll(generation, search));
        }

        /// <summary>Lo que necesita la búsqueda en segundo plano, tomado antes en el hilo de la interfaz.</summary>
        sealed class SearchInput
        {
            public IKBConnectionInfo Connection;
            public string Location;
            public Guid Guid;
            public string QualifiedName;
            public HashSet<string> Excluded;
            public List<string> Folders;
            public bool IncludeFixes;
            public string FixesPattern;

            public bool IsFixes(KbDatabase kb) =>
                !string.IsNullOrEmpty(FixesPattern) &&
                (kb.Name.IndexOf(FixesPattern, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 kb.DatabaseName.IndexOf(FixesPattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        void SearchAll(int generation, SearchInput search)
        {
            List<KbDatabase> databases;
            try
            {
                databases = Databases(search);
            }
            catch (Exception ex)
            {
                Log.Write("No se pudieron listar las KBs del servidor: " + ex);
                OnUi(generation, () => m_Status.Text = "No se pudieron listar las KBs del servidor: " + ex.Message);
                return;
            }

            int hiddenFixes = search.IncludeFixes ? 0 : databases.Count(search.IsFixes);
            if (!search.IncludeFixes)
                databases = databases.Where(d => !search.IsFixes(d)).ToList();

            int withReferences = 0, references = 0;
            var failed = new List<string>();
            Parallel.ForEach(databases, database =>
            {
                try
                {
                    // Si el objeto no está en esa KB (un procedimiento de Servicios, por ejemplo) igual puede llamarse por servicio
                    KbReference target = database.Find(search.Guid, search.QualifiedName);
                    List<KbReference> callers = database.GetCallers(target);
                    if (callers.Count == 0)
                        return;
                    lock (failed)
                    {
                        withReferences++;
                        references += callers.Count;
                    }
                    OnUi(generation, () => AddGroup(database, target, callers));
                }
                catch (Exception ex)
                {
                    Log.Write("Error al buscar en " + database.DatabaseName + ": " + ex);
                    lock (failed)
                        failed.Add(database.Name);
                }
            });

            string status = withReferences == 0
                ? $"Sin referencias en otras KBs ({databases.Count} revisadas)."
                : $"{references} {(references == 1 ? "referencia" : "referencias")} en {withReferences} de {databases.Count} KBs.";
            if (hiddenFixes > 0)
                status += $" Sin las {hiddenFixes} de fixes.";
            if (failed.Count > 0)
                status += " Sin acceso a: " + string.Join(", ", failed.OrderBy(n => n)) + ".";
            OnUi(generation, () => m_Status.Text = status);
        }

        /// <summary>Las KBs del servidor; se reutilizan las ya leídas para no volver a cargar sus módulos.</summary>
        List<KbDatabase> Databases(SearchInput search)
        {
            List<KbDatabase> found = KbDiscovery.Discover(search.Connection, search.Location, search.Excluded, search.Folders);
            lock (m_Databases)
            {
                return found.Select(d => m_Databases.TryGetValue(d.DatabaseName, out KbDatabase known) ? known : m_Databases[d.DatabaseName] = d).ToList();
            }
        }

        void OnUi(int generation, Action action)
        {
            try
            {
                if (m_Control.IsDisposed || !m_Control.IsHandleCreated)
                    return;
                m_Control.BeginInvoke((Action)(() =>
                {
                    if (generation == m_Generation && !m_Control.IsDisposed)
                        action();
                }));
            }
            catch (InvalidOperationException)
            {
                // La ventana se cerró mientras se buscaba
            }
        }

        void AddGroup(KbDatabase database, KbReference target, List<KbReference> callers)
        {
            var group = new KbGroup(this, database, target, callers);
            group.Width = GroupWidth;
            m_List.Controls.Add(group);
            // La lista muestra los recuadros en el orden de Controls: alfabético por KB
            List<KbGroup> groups = m_List.Controls.OfType<KbGroup>().OrderBy(g => g.Database.Name, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < groups.Count; i++)
                m_List.Controls.SetChildIndex(groups[i], i);
            group.FitHeight();
        }

        int GroupWidth => Math.Max(50, m_List.ClientSize.Width - m_List.Padding.Horizontal - 1);

        /// <summary>Los recuadros ocupan el ancho de la lista (también cuando aparece o se va la barra de scroll).</summary>
        void FitWidths()
        {
            m_List.SuspendLayout();
            foreach (KbGroup group in m_List.Controls.OfType<KbGroup>())
                group.Width = GroupWidth;
            m_List.ResumeLayout();
        }

        public void FitAll()
        {
            m_List.SuspendLayout();
            foreach (KbGroup group in m_List.Controls.OfType<KbGroup>())
                group.FitHeight();
            m_List.ResumeLayout();
        }

        void ClearGroups()
        {
            foreach (Control group in m_List.Controls.OfType<KbGroup>().ToList())
                group.Dispose();
        }

        /// <summary>La rueda sobre un árbol (que no tiene scroll propio) desplaza la lista de KBs.</summary>
        public void ScrollList(int delta, int rowHeight) => m_List.ScrollBy(delta, rowHeight);

        /// <summary>Ícono del tipo (en <see cref="Images"/>) y su descripción, por el nombre del tipo en la otra KB.</summary>
        public Image ImageFor(string typeName, out string description)
        {
            description = typeName;
            KBObjectDescriptor descriptor = Descriptor(typeName);
            if (descriptor == null)
                return null;
            description = descriptor.Description;
            if (!Images.Images.ContainsKey(typeName) && descriptor.Icon != null)
                Images.Images.Add(typeName, descriptor.Icon);
            return Images.Images.ContainsKey(typeName) ? Images.Images[typeName] : null;
        }

        static KBObjectDescriptor Descriptor(string typeName)
        {
            if (s_Descriptors == null)
            {
                var descriptors = new Dictionary<string, KBObjectDescriptor>(StringComparer.OrdinalIgnoreCase);
                foreach (KBObjectDescriptor descriptor in PackageManager.Manager.GetKBObjectDescriptors())
                {
                    if (!descriptors.ContainsKey(descriptor.Name))
                        descriptors[descriptor.Name] = descriptor;
                }
                s_Descriptors = descriptors;
            }
            return s_Descriptors.TryGetValue(typeName, out KBObjectDescriptor found) ? found : null;
        }
    }

    /// <summary>
    /// Lista de recuadros con scroll. No se desplaza sola al hacer clic en un árbol (Panel lo hace para mostrar el
    /// control con el foco desde arriba, lo que hacía saltar la lista) y la rueda avanza de a filas.
    /// </summary>
    sealed class KbList : FlowLayoutPanel
    {
        public KbList()
        {
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoScroll = true;
        }

        protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;

        public void ScrollBy(int delta, int rowHeight)
        {
            int step = Math.Max(1, SystemInformation.MouseWheelScrollLines) * rowHeight * delta / 120;
            int max = Math.Max(0, DisplayRectangle.Height - ClientSize.Height);
            AutoScrollPosition = new Point(0, Math.Min(max, Math.Max(0, -AutoScrollPosition.Y - step)));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(e.Delta, Font.Height + 6);
            if (e is HandledMouseEventArgs handled)
                handled.Handled = true;
        }
    }
}
