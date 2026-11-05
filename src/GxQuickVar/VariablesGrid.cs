using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Controls;
using Artech.Common.Controls.Editors;
using Artech.Common.Controls.Editors.Commands;
using Artech.Genexus.Common;
using Artech.Packages.Editors.Variables;
using DevExpress.XtraTreeList;
using DevExpress.XtraTreeList.Columns;
using DevExpress.XtraTreeList.Nodes;
using DevExpress.XtraTreeList.ViewInfo;

namespace GxQuickVar
{
    /// <summary>
    /// El mismo desplegable de tipos en la pestaña Variables (sobre la fila de la variable) y en la ventana
    /// Properties (con una variable seleccionada). Si la variable está en una grilla de variables abierta, el
    /// tipo se asigna con un comando de esa grilla, como el combo de la columna Type: se puede deshacer con
    /// Ctrl+Z y la grilla marca el objeto como modificado. Si no, se asigna directo en la parte de variables,
    /// como hace la ventana Properties.
    /// </summary>
    static class VariablesGrid
    {
        const string TypeColumn = "Type";

        static readonly Font CodeFont = new Font("Consolas", 10f);

        /// <summary>
        /// Lo que hace el atajo de declarar con el foco en <paramref name="focused"/>, o null si ahí no corresponde
        /// (fuera de la grilla de variables y de Properties, o Properties sin una variable seleccionada).
        /// </summary>
        public static Action ForFocus(Control focused)
        {
            for (Control control = focused; control != null; control = control.Parent)
            {
                if (control is VariablesEditor editor)
                    return editor.ReadOnly ? null : (Action)(() => Guard(() => FromGrid(editor)));
                if (control is IPropertyInspector inspector)
                {
                    Variable variable = SelectedVariable(inspector);
                    return variable == null ? null : (Action)(() => Guard(() => FromProperties(control, variable)));
                }
            }
            return null;
        }

        static void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddErrorLine("GxQuickVar: error inesperado: " + ex);
            }
        }

        /// <summary>La variable que muestra Properties, si es una sola.</summary>
        static Variable SelectedVariable(IPropertyInspector inspector)
        {
            object[] selected = inspector.SelectedObjects;
            if (selected != null && selected.Length > 0)
                return selected.Length == 1 ? selected[0] as Variable : null;
            return inspector.SelectedObject as Variable;
        }

        // ---- Pestaña Variables ----

        static void FromGrid(VariablesEditor editor)
        {
            if (!FinishCellEdit(editor))
                return;
            StructItem item = Editor.CurrentItem(editor);
            if (!(item?.Data is Variable variable))
            {
                VariableDeclarer.Status("GxQuickVar: elegí una variable de la lista");
                return;
            }

            DeclareInGrid(editor, item, variable,
                size => VariableDeclarer.PlaceNear(TypeCellBounds(editor, item), size, alignRight: false),
                Palette.For(BackColorOf(editor, item)));
            editor.SetFocus();
        }

        /// <summary>Si se está editando una celda (p.ej. el nombre de una variable nueva), la confirma.</summary>
        static bool FinishCellEdit(StructEditor editor)
        {
            if (!Editor.IsEditing(editor) || Editor.CloseActiveEditor(editor, EditorCloseAction.PostEdit))
                return true;
            VariableDeclarer.Status("GxQuickVar: corregí la celda que estás editando antes de elegir el tipo");
            return false;
        }

        static void DeclareInGrid(VariablesEditor editor, StructItem item, Variable variable, Func<Size, Point> locate, Palette palette)
        {
            if (!CanDeclare(variable))
                return;
            KBModel model = variable.Model;
            PopupOptions options = VariableDeclarer.CreateOptions(model, variable.KBObject, variable.Name, variable,
                text => ApplyInGrid(editor, item, variable, model, text), palette, CodeFont);
            VariableDeclarer.ShowPopup(options, locate, editor.FindForm());
        }

        static bool CanDeclare(Variable variable)
        {
            if (string.IsNullOrEmpty(variable.Name))
            {
                VariableDeclarer.Status("GxQuickVar: escribí primero el nombre de la variable");
                return false;
            }
            if (variable.IsStandard)
            {
                VariableDeclarer.Status($"GxQuickVar: &{variable.Name} es una variable estándar, su tipo no se cambia");
                return false;
            }
            if (variable.IsReadOnly)
            {
                VariableDeclarer.Status("GxQuickVar: el objeto es de solo lectura");
                return false;
            }
            return true;
        }

        /// <summary>Lo mismo que hace la grilla al confirmar la celda Type, con el tipo escrito en el desplegable.</summary>
        static string ApplyInGrid(VariablesEditor editor, StructItem item, Variable variable, KBModel model, string typeText)
        {
            var command = new SetTypeCommand(item, variable, model, typeText);
            editor.ExecuteCommand(command);   // ejecuta Do y lo agrega a deshacer
            if (command.Error != null)
                return command.Error;

            // Como después de editar una celda: avisa al documento que cambió y refresca la fila y Properties
            StructCell cell = item.Cells[TypeColumn];
            Editor.AfterEditItem(editor, new StructItemCellEventArgs(item, cell, cell.Value));
            item.UpdateNode();
            if (Editor.SelectedItems(editor).Contains(item))
                Editor.SignalSelectionChanged(editor);

            VariableDeclarer.Declared(model, variable.Name, typeText, variable);
            return null;
        }

        /// <summary>Rectángulo en pantalla de la celda Type de la fila (o de la grilla, si no se puede saber).</summary>
        static Rectangle TypeCellBounds(StructEditor editor, StructItem item)
        {
            try
            {
                TreeListNode node = item.TreeNode;
                TreeList tree = node?.TreeList;
                TreeListColumn column = editor.Columns[TypeColumn]?.TreeColumn;
                if (tree != null && column != null)
                {
                    item.MakeNodeVisible();
                    tree.Update();
                    RowInfo row = tree.ViewInfo.RowsInfo[node];
                    if (row != null)
                    {
                        CellInfo cell = row.Cells.OfType<CellInfo>().FirstOrDefault(c => c.ColumnInfo?.Column == column);
                        return tree.RectangleToScreen(cell?.Bounds ?? row.DataBounds);
                    }
                }
            }
            catch (Exception)
            {
                // Es solo para ubicar el desplegable
            }
            return editor.RectangleToScreen(new Rectangle(0, 0, editor.Width, 0));
        }

        static Color BackColorOf(StructEditor editor, StructItem item)
        {
            try
            {
                TreeList tree = item.TreeNode?.TreeList;
                Color row = tree?.ViewInfo.PaintAppearance.Row.BackColor ?? Color.Empty;
                if (!row.IsEmpty && row.A == 255)
                    return row;
                if (tree != null)
                    return tree.BackColor;
            }
            catch (Exception)
            {
                // Se usa el fondo del control
            }
            return editor.BackColor;
        }

        // ---- Ventana Properties ----

        static void FromProperties(Control inspector, Variable selected)
        {
            Control focused = EditorLocator.FocusedControl();
            Func<Size, Point> locate = size => VariableDeclarer.PlaceNear(PropertiesAnchor(inspector, focused), size, alignRight: true);
            var palette = Palette.For((focused ?? inspector).BackColor);

            if (FindInGrid(selected, out VariablesEditor editor, out StructItem item))
            {
                if (editor.ReadOnly)
                {
                    VariableDeclarer.Status("GxQuickVar: el objeto es de solo lectura");
                    return;
                }
                if (FinishCellEdit(editor))
                    DeclareInGrid(editor, item, (Variable)item.Data, locate, palette);
            }
            else if (CanDeclare(selected) && selected.Part != null)
            {
                // Sin la grilla abierta, como Properties: directo en la parte de variables
                KBModel model = selected.Model;
                string name = selected.Name;
                PopupOptions options = VariableDeclarer.CreateOptions(model, selected.KBObject, name, selected,
                    text => VariableDeclarer.ApplyToPart(model, selected.Part, name, text), palette, CodeFont);
                VariableDeclarer.ShowPopup(options, locate, inspector.FindForm());
            }

            if (focused != null && !focused.IsDisposed && focused.CanFocus)
                focused.Focus();
        }

        /// <summary>Debajo del editor de la propiedad si hay uno abierto; si no, arriba de Properties.</summary>
        static Rectangle PropertiesAnchor(Control inspector, Control focused)
        {
            if (focused != null && focused != inspector && focused.Height < inspector.Height / 3)
                return focused.RectangleToScreen(focused.ClientRectangle);
            return inspector.RectangleToScreen(new Rectangle(0, 0, inspector.Width, 0));
        }

        /// <summary>La fila de la variable en una grilla de variables abierta (aunque esté en otra pestaña).</summary>
        static bool FindInGrid(Variable variable, out VariablesEditor editor, out StructItem item)
        {
            foreach (VariablesEditor candidate in Application.OpenForms.Cast<Form>().SelectMany(OpenEditors))
            {
                List<StructItem> items = candidate.GetRootItem()?.GetAllItems().Where(i => i.Data is Variable).ToList();
                if (items == null)
                    continue;
                // La misma instancia; si la grilla se recargó, la de igual nombre del mismo objeto
                item = items.FirstOrDefault(i => ReferenceEquals(i.Data, variable))
                    ?? items.FirstOrDefault(i => IsSameVariable((Variable)i.Data, variable));
                if (item != null)
                {
                    editor = candidate;
                    return true;
                }
            }
            editor = null;
            item = null;
            return false;
        }

        static bool IsSameVariable(Variable a, Variable b) =>
            Equals(a.KBObject, b.KBObject)
            && !string.IsNullOrEmpty(a.Name)
            && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

        static IEnumerable<VariablesEditor> OpenEditors(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child.IsDisposed)
                    continue;
                if (child is VariablesEditor editor)
                    yield return editor;
                else
                    foreach (VariablesEditor nested in OpenEditors(child))
                        yield return nested;
            }
        }

        /// <summary>
        /// Cambiar el tipo de una variable de la grilla. Deshacer y rehacer dejan la variable exactamente como
        /// estaba antes y después (todas sus propiedades), no vuelven a interpretar el texto.
        /// </summary>
        sealed class SetTypeCommand : StructCommand
        {
            readonly StructItem m_Item;
            readonly Variable m_Variable;
            readonly KBModel m_Model;
            readonly string m_TypeText;
            VariableSnapshot m_Before;
            VariableSnapshot m_After;

            public SetTypeCommand(StructItem item, Variable variable, KBModel model, string typeText)
                : base("GxQuickVar: tipo de " + variable.Name)   // sin "&": en el menú Deshacer subraya la letra
            {
                m_Item = item;
                m_Variable = variable;
                m_Model = model;
                m_TypeText = typeText;
            }

            /// <summary>Por qué no se pudo asignar el tipo la primera vez, o null.</summary>
            public string Error { get; private set; }

            public override bool Do(StructEditor editor)
            {
                if (m_After != null)
                    return Restore(m_After);   // rehacer

                VariableSnapshot before = VariableSnapshot.Take(m_Variable);
                Error = VariableDeclarer.ApplyType(m_Model, m_Variable, m_TypeText);
                if (Error != null)
                    return false;   // ApplyType ya la dejó como estaba; el comando no queda en deshacer
                m_Before = before;
                m_After = VariableSnapshot.Take(m_Variable);
                return true;
            }

            public override bool Undo(StructEditor editor) => Restore(m_Before);

            /// <summary>
            /// Si algo no se pudo restaurar se avisa en Output; el comando igual cuenta como hecho, para que
            /// deshacer y rehacer de la grilla no queden desfasados de la variable.
            /// </summary>
            bool Restore(VariableSnapshot snapshot)
            {
                try
                {
                    snapshot.RestoreInto(m_Variable);
                }
                catch (Exception ex)
                {
                    CommonServices.Output.AddErrorLine("GxQuickVar: " + ex.Message + (ex.InnerException != null ? ": " + ex.InnerException.Message : ""));
                }
                return true;
            }

            public override IList<StructItem> ItemsAffected() => new List<StructItem> { m_Item };
        }

        /// <summary>Lo que la grilla no expone públicamente (GeneXus 18 U15).</summary>
        static class Editor
        {
            const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            static readonly PropertyInfo s_CurrentItem = typeof(StructEditor).GetProperty("CurrentItem", Members);
            static readonly MethodInfo s_IsEditing = Method("IsEditing");
            static readonly MethodInfo s_CloseActiveEditor = Method("CloseActiveEditor", typeof(EditorCloseAction));
            static readonly MethodInfo s_OnAfterEditItem = Method("OnAfterEditItem", typeof(StructItemCellEventArgs));
            static readonly MethodInfo s_SignalSelectionChanged = Method("SignalSelectionChanged");
            static readonly MethodInfo s_GetSelectedItems = Method("GetSelectedItems");

            public static StructItem CurrentItem(StructEditor editor) =>
                (StructItem)(s_CurrentItem ?? throw Missing("CurrentItem")).GetValue(editor);

            public static bool IsEditing(StructEditor editor) => (bool)Call(s_IsEditing, "IsEditing", editor);

            public static bool CloseActiveEditor(StructEditor editor, EditorCloseAction action) =>
                (bool)Call(s_CloseActiveEditor, "CloseActiveEditor", editor, action);

            /// <summary>Virtual: llega a la grilla de variables, que avisa al documento que cambió.</summary>
            public static void AfterEditItem(StructEditor editor, StructItemCellEventArgs args) =>
                Call(s_OnAfterEditItem, "OnAfterEditItem", editor, args);

            public static List<StructItem> SelectedItems(StructEditor editor) =>
                (List<StructItem>)Call(s_GetSelectedItems, "GetSelectedItems", editor);

            /// <summary>Properties vuelve a mostrar lo seleccionado en la grilla.</summary>
            public static void SignalSelectionChanged(StructEditor editor) =>
                Call(s_SignalSelectionChanged, "SignalSelectionChanged", editor);

            static MethodInfo Method(string name, params Type[] parameters) =>
                typeof(StructEditor).GetMethod(name, Members, null, parameters, null);

            static object Call(MethodInfo method, string name, object target, params object[] args)
            {
                try
                {
                    return (method ?? throw Missing(name)).Invoke(target, args);
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
                }
            }

            static Exception Missing(string name) =>
                new NotSupportedException($"La grilla de variables de esta versión de GeneXus no tiene {name} (GxQuickVar está hecho para GeneXus 18 U15).");
        }
    }
}
