using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Artech.Common.Controls.Editors.CustomListView;
using Artech.Genexus.Common.CustomTypes;
using Artech.Packages.Genexus.UI.TypeEditors;

namespace GxEnumValues
{
    /// <summary>
    /// Agrega al diálogo Enum Values una barra de búsqueda (filtra por nombre, descripción y valor) y el orden
    /// por columna (clic en el encabezado: ascendente, descendente y de nuevo el orden original).
    /// </summary>
    /// <remarks>
    /// El diálogo guarda los valores en el orden de la lista (GridToValues recorre sus ítems): ordenar cambia el
    /// orden del dominio, igual que Move Up / Move Down. Filtrar saca de la lista los ítems que no coinciden, así
    /// que antes de que el diálogo se cierre (OK, Cancel, la X) se vuelven a poner todos.
    /// </remarks>
    sealed class EnumValuesEnhancer
    {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly CompareInfo Compare = CultureInfo.CurrentCulture.CompareInfo;

        // Diálogos a los que ya se les agregó (el hook avisa en cada activación)
        static readonly ConditionalWeakTable<Form, object> s_Attached = new ConditionalWeakTable<Form, object>();

        readonly EnumValuesEditorControl m_Dialog;
        readonly ListViewEx m_List;
        readonly SearchBox m_Search = new SearchBox();
        readonly MethodInfo m_GridToValues;
        readonly MethodInfo m_ValidateItems;
        readonly Dictionary<ListViewItem, int> m_OriginalOrder = new Dictionary<ListViewItem, int>();

        // Todos los ítems en el orden de la última vez que se filtró u ordenó, y los que el filtro dejó afuera
        List<ListViewItem> m_Order = new List<ListViewItem>();
        readonly HashSet<ListViewItem> m_Hidden = new HashSet<ListViewItem>();

        int m_SortColumn = -1;
        SortOrder m_SortOrder = SortOrder.None;

        // NativeWindow no se mantiene viva sola
        CloseWatcher m_CloseWatcher;

        public static void Attach(EnumValuesEditorControl dialog)
        {
            if (dialog.IsDisposed || !dialog.IsHandleCreated || s_Attached.TryGetValue(dialog, out _))
                return;
            s_Attached.Add(dialog, new object());
            try
            {
                // Queda vivo por los eventos del diálogo
                new EnumValuesEnhancer(dialog);
            }
            catch (Exception ex)
            {
                Log.Write("No se pudo agregar la búsqueda al diálogo Enum Values: " + ex);
            }
        }

        EnumValuesEnhancer(EnumValuesEditorControl dialog)
        {
            m_Dialog = dialog;
            m_List = (ListViewEx)Field("listView1");
            var ok = (Button)Field("btnMainOk");
            var cancel = (Button)Field("btnCancel");
            m_GridToValues = Method("GridToValues");
            m_ValidateItems = Method("ValidateItems");

            int index = 0;
            foreach (ListViewItem item in m_List.Items)
                m_OriginalOrder[item] = index++;

            AddSearchBox();

            m_Search.TextChanged += (s, e) => Guard(() => Apply(false));
            m_Search.Accept += (s, e) => Guard(GoToList);
            m_List.ColumnClick += (s, e) => Guard(() => SortBy(e.Column));
            // Corren después de los del diálogo: OK ya armó los valores con lo que se ve
            ok.Click += (s, e) => Guard(OnOk);
            cancel.Click += (s, e) => Guard(ShowAll);
            m_CloseWatcher = new CloseWatcher(dialog, () => Guard(ShowAll));

            m_Dialog.KeyPreview = true;
            m_Dialog.KeyDown += OnDialogKeyDown;
        }

        object Field(string name)
        {
            FieldInfo field = typeof(EnumValuesEditorControl).GetField(name, Members)
                ?? throw new MissingFieldException(typeof(EnumValuesEditorControl).FullName, name);
            return field.GetValue(m_Dialog);
        }

        static MethodInfo Method(string name) =>
            typeof(EnumValuesEditorControl).GetMethod(name, Members, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException(typeof(EnumValuesEditorControl).FullName, name);

        /// <summary>La barra va arriba de la lista; la lista y los botones de al lado bajan para dejarle lugar.</summary>
        void AddSearchBox()
        {
            m_Search.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            m_Search.Bounds = new System.Drawing.Rectangle(m_List.Left, m_List.Top, m_List.Width, m_Search.Height);
            m_Dialog.Controls.Add(m_Search);

            int shift = m_Search.Height + m_Search.Margin.Vertical;
            foreach (Control control in m_Dialog.Controls)
            {
                // Incluye el editor de celda si hay uno abierto (el diálogo empieza editando si no hay valores)
                bool top = (control.Anchor & AnchorStyles.Top) != 0 && (control.Anchor & AnchorStyles.Bottom) == 0;
                if (control != m_Search && control != m_List && control.Visible && top && control.Top < m_List.Bottom)
                    control.Top += shift;
            }
            m_List.Top += shift;
            m_List.Height -= shift;
        }

        void OnDialogKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData != (Keys.Control | Keys.F))
                return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            m_Search.Focus();
            m_Search.SelectAll();
        }

        void GoToList()
        {
            m_List.Focus();
            if (m_List.SelectedItems.Count == 0 && m_List.Items.Count > 0)
                Select(m_List.Items[0]);
        }

        void SortBy(int column)
        {
            if (column != m_SortColumn)
            {
                m_SortColumn = column;
                m_SortOrder = SortOrder.Ascending;
            }
            else
            {
                m_SortOrder = m_SortOrder == SortOrder.Ascending ? SortOrder.Descending
                    : m_SortOrder == SortOrder.Descending ? SortOrder.None
                    : SortOrder.Ascending;
            }

            Apply(true);
            try
            {
                m_List.DisplaySortIcon(column, m_SortOrder);
            }
            catch (Win32Exception)
            {
                // Sin flecha en el encabezado: el orden ya quedó
            }
        }

        /// <summary>Vuelve a armar la lista: todos los ítems (ordenados si se pide) que coinciden con la búsqueda.</summary>
        void Apply(bool sort)
        {
            FinishCellEdit();
            List<ListViewItem> all = AllItems();
            if (sort)
                all = Sorted(all);

            string text = m_Search.Text.Trim();
            ListViewItem[] visible = all.Where(item => Matches(item, text)).ToArray();
            ListViewItem selected = m_List.SelectedItems.Count > 0 ? m_List.SelectedItems[0] : null;

            m_List.BeginUpdate();
            try
            {
                m_List.Items.Clear();
                m_List.Items.AddRange(visible);
            }
            finally
            {
                m_List.EndUpdate();
            }
            m_Order = all;
            m_Hidden.Clear();
            m_Hidden.UnionWith(all.Except(visible));

            if (selected != null && selected.ListView == m_List)
                Select(selected);
            else if (m_List.Items.Count > 0)
                Select(m_List.Items[0]);
        }

        /// <summary>
        /// Los ítems de la lista más los que dejó afuera el filtro, en orden. Mientras se filtra el diálogo puede
        /// agregar, borrar o mover ítems: los visibles ocupan, en su orden actual, los lugares que tenían los
        /// visibles, y los que sobran (agregados) van al final.
        /// </summary>
        List<ListViewItem> AllItems()
        {
            List<ListViewItem> visible = m_List.Items.Cast<ListViewItem>().ToList();
            if (m_Hidden.Count == 0)
                return visible;

            var all = new List<ListViewItem>(visible.Count + m_Hidden.Count);
            int next = 0;
            foreach (ListViewItem item in m_Order)
            {
                if (m_Hidden.Contains(item))
                    all.Add(item);
                else if (next < visible.Count)
                    all.Add(visible[next++]);
            }
            all.AddRange(visible.Skip(next));
            return all;
        }

        List<ListViewItem> Sorted(List<ListViewItem> items)
        {
            if (m_SortOrder == SortOrder.None)
                return items.OrderBy(OriginalIndex).ToList();

            int column = m_SortColumn;
            Comparer<ListViewItem> comparer = Comparer<ListViewItem>.Create((a, b) => CompareCells(Cell(a, column), Cell(b, column)));
            return (m_SortOrder == SortOrder.Ascending ? items.OrderBy(i => i, comparer) : items.OrderByDescending(i => i, comparer)).ToList();
        }

        // Los agregados después de abrir el diálogo van al final, en el orden en que estén
        int OriginalIndex(ListViewItem item) => m_OriginalOrder.TryGetValue(item, out int index) ? index : int.MaxValue;

        static string Cell(ListViewItem item, int column) => column < item.SubItems.Count ? item.SubItems[column].Text : "";

        static int CompareCells(string a, string b)
        {
            // Los números como números (2 antes que 10); el resto como texto, sin distinguir mayúsculas
            if (decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal x)
                && decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal y))
                return x.CompareTo(y);
            return Compare.Compare(a, b, CompareOptions.IgnoreCase);
        }

        static bool Matches(ListViewItem item, string text)
        {
            if (text.Length == 0 || !(item.Tag is EnumValue value))
                return true;
            return Contains(value.Name, text) || Contains(value.Description, text) || Contains(value.Value, text);
        }

        // Sin distinguir mayúsculas ni tildes
        static bool Contains(string source, string text) =>
            source != null && Compare.IndexOf(source, text, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

        void Select(ListViewItem item)
        {
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
        }

        /// <summary>Cierra la celda que se está editando antes de rearmar la lista.</summary>
        void FinishCellEdit()
        {
            if (!m_List.IsEditing)
                return;
            // Como al hacer clic afuera: el diálogo acepta el texto solo si se modificó
            m_List.Focus();
            if (m_List.IsEditing)
                m_List.EndEditing(true);
        }

        /// <summary>Vuelve a poner en la lista los ítems que dejó afuera el filtro.</summary>
        void ShowAll()
        {
            m_Search.Text = "";
            if (m_Hidden.Count > 0)
                Apply(false);
        }

        void OnOk()
        {
            if (m_Hidden.Count == 0)
                return;

            // El diálogo armó los valores solo con lo que se veía: se vuelven a armar con todos
            ShowAll();
            m_GridToValues.Invoke(m_Dialog, null);
            // Un valor oculto con error no se validó con el resto
            m_ValidateItems.Invoke(m_Dialog, null);
            if (m_Hidden.Count > 0 || m_Dialog.HasError)
                m_Dialog.DialogResult = DialogResult.None;
        }

        static void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Write("Error en el diálogo Enum Values: " + ex);
            }
        }

        /// <summary>Barra de búsqueda: Esc la borra (si está vacía, cierra el diálogo como siempre); Enter o flecha abajo pasan a la lista.</summary>
        sealed class SearchBox : TextBox
        {
            const int EM_SETCUEBANNER = 0x1501;
            const string Placeholder = "Buscar por nombre, descripción o valor (Ctrl+F)";

            public event EventHandler Accept;

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                // wParam = 1: el texto de ayuda se ve también con el foco puesto
                SendMessage(Handle, EM_SETCUEBANNER, (IntPtr)1, Placeholder);
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                switch (keyData)
                {
                    case Keys.Escape:
                        if (TextLength == 0)
                            break;
                        Clear();
                        return true;

                    case Keys.Enter:
                    case Keys.Down:
                        Accept?.Invoke(this, EventArgs.Empty);
                        return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        }

        /// <summary>
        /// Avisa antes de que la X o Alt+F4 cierren el diálogo: su OnClosing compara la lista con los valores de
        /// antes para preguntar si guardar, y tiene que ver todos los ítems.
        /// </summary>
        sealed class CloseWatcher : NativeWindow
        {
            const int WM_CLOSE = 0x0010;

            readonly Action m_BeforeClose;

            public CloseWatcher(Form form, Action beforeClose)
            {
                m_BeforeClose = beforeClose;
                // NativeWindow suelta el handle sola al destruirse la ventana
                AssignHandle(form.Handle);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_CLOSE)
                    m_BeforeClose();
                base.WndProc(ref m);
            }
        }
    }
}
