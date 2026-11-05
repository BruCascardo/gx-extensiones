using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Artech.FrameworkDE.Controls;
using Infragistics.Win;
using Infragistics.Win.UltraWinTree;

namespace GxKbRefs
{
    /// <summary>Medidas de las filas de un árbol ya dibujado (0 mientras no se midieron).</summary>
    struct RowMetrics
    {
        public int Top;
        public int Height;
        public int Pitch;
    }

    /// <summary>
    /// Recuadro de una KB con su árbol de referencias. El árbol no tiene scroll propio: el recuadro crece con lo
    /// que se expande y la lista de KBs es la que se desplaza.
    /// </summary>
    sealed class KbGroup : GroupBox
    {
        readonly KbTree m_Tree;

        public KbDatabase Database { get; }

        public KbGroup(ReferencesExtender owner, KbDatabase database, KbReference target, List<KbReference> callers)
        {
            Database = database;
            Text = $"{database.Name} ({callers.Count})";
            BackColor = SystemColors.Window;
            Margin = new Padding(0, 0, 0, 4);
            Padding = new Padding(4, 2, 2, 2);
            m_Tree = new KbTree(owner, this, database, target) { Dock = DockStyle.Fill };
            Controls.Add(m_Tree);
            m_Tree.LoadNodes(m_Tree.Nodes, callers);
        }

        public void FitHeight() => m_Tree.FitHeight();

        /// <summary>Alto del recuadro para que el árbol muestre <paramref name="treeHeight"/> píxeles.</summary>
        public void SetTreeHeight(int treeHeight)
        {
            int chrome = Height - DisplayRectangle.Height;
            Height = treeHeight + chrome;
        }
    }

    /// <summary>
    /// Árbol de una KB con el mismo control y comportamiento que Is Referenced By: orden alfabético, ícono del tipo,
    /// cursiva para las referencias débiles, agrupado por tipo si está tildado "Group References by Type" y flecha
    /// en los objetos que a su vez son referenciados, que al expandirse traen quién los referencia en esa KB.
    /// </summary>
    sealed class KbTree : GxUltraTree
    {
        sealed class TextComparer : IComparer, IComparer<UltraTreeNode>
        {
            public static readonly TextComparer Instance = new TextComparer();

            public int Compare(UltraTreeNode x, UltraTreeNode y) => StringComparer.OrdinalIgnoreCase.Compare(x?.Text, y?.Text);

            public int Compare(object x, object y) => Compare(x as UltraTreeNode, y as UltraTreeNode);
        }

        readonly ReferencesExtender m_Owner;
        readonly KbGroup m_Group;
        readonly KbDatabase m_Database;
        readonly KbReference m_Target;

        public KbTree(ReferencesExtender owner, KbGroup group, KbDatabase database, KbReference target)
        {
            m_Owner = owner;
            m_Group = group;
            m_Database = database;
            m_Target = target;

            Font = owner.TreeFont;
            ImageList = owner.Images;
            Scrollable = Scrollbar.Hide;
            Override.SortComparer = TextComparer.Instance;
            Override.Sort = SortType.Ascending;
            Override.SelectionType = Infragistics.Win.UltraWinTree.SelectType.Extended;

            AfterExpand += OnAfterExpand;
            AfterCollapse += (s, e) => FitHeight();

            var copy = new ToolStripMenuItem("Copiar nombre");
            copy.Click += (s, e) => CopyNames();
            ContextMenuStrip = new ContextMenuStrip();
            ContextMenuStrip.Items.Add(copy);
            MouseDown += (s, e) =>
            {
                // Clic derecho sobre un nodo no seleccionado: se selecciona solo ese, como en el Explorer
                UltraTreeNode node = GetNodeFromPoint(e.Location);
                if (e.Button == MouseButtons.Right && node != null && !node.Selected)
                {
                    SelectedNodes.Clear();
                    node.Selected = true;
                }
            };
        }

        /// <summary>Agrega las referencias a <paramref name="nodes"/>, como CallBrowserControl.LoadInTree.</summary>
        public void LoadNodes(TreeNodesCollection nodes, IEnumerable<KbReference> references)
        {
            BeginUpdate();
            try
            {
                var groups = new Dictionary<string, UltraTreeNode>();
                foreach (KbReference reference in references)
                {
                    Image image = m_Owner.ImageFor(reference.TypeName, out string typeDescription);
                    TreeNodesCollection target = nodes;
                    if (m_Owner.GroupByType)
                    {
                        if (!groups.TryGetValue(reference.TypeName, out UltraTreeNode group))
                        {
                            group = new UltraTreeNode { Text = typeDescription };
                            group.Override.NodeAppearance.Image = image;
                            groups[reference.TypeName] = group;
                            nodes.Add(group);
                        }
                        target = group.Nodes;
                    }

                    string text = reference.Via == null ? reference.QualifiedName : $"{reference.QualifiedName}  (servicio {reference.Via})";
                    var node = new UltraTreeNode { Text = text, Tag = reference };
                    node.Override.NodeAppearance.Image = image;
                    if (reference.HasMoreRefs)
                        node.Override.ShowExpansionIndicator = ShowExpansionIndicator.Always;
                    if (!reference.Hard)
                        node.Override.NodeAppearance.FontData.Italic = DefaultableBoolean.True;
                    target.Add(node);
                }
                RefreshSort(nodes, true);
            }
            finally
            {
                EndUpdate();
            }
        }

        void OnAfterExpand(object sender, NodeEventArgs e)
        {
            UltraTreeNode node = e.TreeNode;
            if (node.Tag is KbReference reference && node.Nodes.Count == 0 && !IsRecursive(reference, node))
            {
                Cursor previous = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;
                try
                {
                    LoadNodes(node.Nodes, m_Database.GetCallers(reference));
                }
                catch (Exception ex)
                {
                    Log.Write("Error al expandir " + reference.QualifiedName + " en " + m_Database.DatabaseName + ": " + ex);
                }
                finally
                {
                    Cursor.Current = previous;
                }
            }
            FitHeight();
        }

        /// <summary>El objeto analizado o uno de los padres del nodo: no se vuelve a expandir (como IsRecursiveReference).</summary>
        bool IsRecursive(KbReference reference, UltraTreeNode node)
        {
            if (reference.SameEntity(m_Target))
                return true;
            for (UltraTreeNode parent = node.Parent; parent != null; parent = parent.Parent)
            {
                if (reference.SameEntity(parent.Tag as KbReference))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Ajusta el recuadro a los nodos visibles: lo que hay arriba de la primera fila, el paso entre filas y el alto
        /// de la última. Esas medidas se toman del árbol ya dibujado (<see cref="OnPaint"/>) y valen para todos los
        /// árboles; hasta tenerlas se estiman con el alto del nodo.
        /// </summary>
        public void FitHeight()
        {
            if (IsDisposed)
                return;
            int rows = Math.Max(1, CountVisible(Nodes));
            RowMetrics metrics = m_Owner.Metrics;
            int rowHeight = metrics.Height > 0 ? metrics.Height : EstimatedRowHeight;
            int pitch = metrics.Pitch > 0 ? metrics.Pitch : rowHeight + NodeSpacing;
            int clientHeight = Math.Max(0, metrics.Top) + (rows - 1) * pitch + rowHeight + BottomPadding;
            m_Group.SetTreeHeight(clientHeight + (Height - ClientSize.Height));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            RowMetrics metrics = m_Owner.Metrics;
            if (metrics.Pitch > 0)
                return;
            // Primera fila y, si hay dos en pantalla, el paso entre ellas
            List<Rectangle> rows = VisibleNodes(Nodes).Where(n => n.IsInView).Select(n => n.Bounds)
                .Where(b => b.Height > 0).OrderBy(b => b.Top).Take(2).ToList();
            if (rows.Count == 0)
                return;
            var measured = new RowMetrics
            {
                Top    = rows[0].Top,
                Height = rows[0].Height,
                Pitch  = rows.Count > 1 && rows[1].Top > rows[0].Top ? rows[1].Top - rows[0].Top : 0,
            };
            if (measured.Top != metrics.Top || measured.Height != metrics.Height || measured.Pitch != metrics.Pitch)
            {
                m_Owner.Metrics = measured;
                BeginInvoke((Action)m_Owner.FitAll);
            }
        }

        // Estimación hasta medir: GxUltraTree deja 4 píxeles después de cada nodo
        const int NodeSpacing = 4;
        const int BottomPadding = 2;

        int EstimatedRowHeight => Nodes.Count > 0 ? Math.Max(Nodes[0].ItemHeightResolved, Font.Height) : Font.Height + 4;

        int RowPitch => m_Owner.Metrics.Pitch > 0 ? m_Owner.Metrics.Pitch : EstimatedRowHeight + NodeSpacing;

        static int CountVisible(TreeNodesCollection nodes) => VisibleNodes(nodes).Count();

        static IEnumerable<UltraTreeNode> VisibleNodes(TreeNodesCollection nodes)
        {
            foreach (UltraTreeNode node in nodes)
            {
                yield return node;
                if (node.Expanded)
                {
                    foreach (UltraTreeNode child in VisibleNodes(node.Nodes))
                        yield return child;
                }
            }
        }

        void CopyNames()
        {
            string[] names = SelectedNodes.Cast<UltraTreeNode>()
                .Select(n => (n.Tag as KbReference)?.QualifiedName ?? n.Text)
                .ToArray();
            if (names.Length > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, names));
        }

        protected override void OnMouseWheel(MouseEventArgs e) => m_Owner.ScrollList(e.Delta, RowPitch);
    }
}
