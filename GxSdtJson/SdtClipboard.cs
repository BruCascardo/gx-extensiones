using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Artech.Architecture.UI.Framework.Commands;
using Artech.Architecture.UI.Framework.Objects;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Controls.Editors;
using Artech.Common.Controls.Editors.Commands;
using Artech.Common.Framework.Commands;
using Artech.Common.Helpers.Structure;
using Artech.Genexus.Common.Parts;
using Artech.Genexus.Common.Parts.SDT;
using Artech.Packages.Editors.SDT;

namespace GxSdtJson
{
    /// <summary>
    /// Ctrl+C y Ctrl+V en la grilla de un SDT. Ctrl+C hace la copia de GeneXus y además deja como
    /// texto el JSON de lo seleccionado; Ctrl+V, si el portapapeles tiene ese JSON, inserta los ítems
    /// donde está parado el cursor (si no, pega como siempre).
    /// </summary>
    /// <remarks>
    /// Copiar y pegar los atiende el propio editor (va antes que los packages en la cadena de
    /// comandos), así que las teclas se toman antes, con un hook de teclado del hilo de la UI. El
    /// último hook instalado es el primero en recibirlas: se reinstala al activar un SDT para ir
    /// antes que el de las barras de herramientas.
    /// </remarks>
    static class SdtClipboard
    {
        const string Title = "GxSdtJson";

        #region Hook de teclado

        const int WH_KEYBOARD = 2;

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        static readonly HookProc s_HookProc = OnKey;   // referencia viva mientras esté el hook
        static IntPtr s_Hook;
        static int s_HookThread;

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, int dwThreadId);

        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr GetFocus();

        [DllImport("kernel32.dll")]
        static extern int GetCurrentThreadId();

        /// <summary>(Re)instala el hook en el hilo actual, que tiene que ser el de la UI.</summary>
        public static void Install()
        {
            if (s_Hook != IntPtr.Zero)
                UnhookWindowsHookEx(s_Hook);
            s_HookThread = GetCurrentThreadId();
            s_Hook = SetWindowsHookEx(WH_KEYBOARD, s_HookProc, IntPtr.Zero, s_HookThread);
        }

        public static void EnsureInstalled()
        {
            if (s_Hook == IntPtr.Zero || s_HookThread != GetCurrentThreadId())
                Install();
        }

        static IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                long flags = lParam.ToInt64();
                bool pressed = (flags & 0x80000000L) == 0 && (flags & 0x40000000L) == 0;   // sin soltar ni repetir
                var key = (Keys)wParam.ToInt32();
                // Con cualquier código (HC_ACTION o HC_NOREMOVE): el bucle de WinForms espía la cola con
                // PeekMessage(PM_NOREMOVE) y Windows llama al hook una sola vez por tecla, en ese momento.
                // Descartarla ahí (devolver 1) la saca de la cola.
                if (code >= 0 && pressed && (key == Keys.C || key == Keys.V) && Control.ModifierKeys == Keys.Control)
                {
                    SDTEditor editor = FocusedEditor();
                    if (editor != null && !Editor.IsEditing(editor) && (key == Keys.C || !editor.ReadOnly))
                    {
                        // Se hace después de procesar la tecla, fuera del hook
                        editor.BeginInvoke(key == Keys.C ? (Action)(() => Copy(editor)) : () => Paste(editor));
                        return (IntPtr)1;
                    }
                }
            }
            catch (Exception)
            {
                // Ante cualquier problema la tecla sigue su camino normal
            }
            return CallNextHookEx(s_Hook, code, wParam, lParam);
        }

        /// <summary>La grilla de SDT que tiene el foco (no una celda en edición: eso se ve aparte).</summary>
        static SDTEditor FocusedEditor()
        {
            for (Control control = Control.FromChildHandle(GetFocus()); control != null; control = control.Parent)
            {
                if (control is SDTEditor editor)
                    return editor;
            }
            return null;
        }

        #endregion

        static void Copy(SDTEditor editor)
        {
            try
            {
                // La copia de GeneXus deja los atributos y dominios como objetos (para pegarlos en una
                // transacción, por ejemplo); el JSON reemplaza solo el texto.
                bool copied = RunEditorCommand(editor, CommandKeys.Core.Copy);
                string json = SelectionJson(editor, out string what);
                if (copied)
                {
                    UIServices.Clipboard.AddData(DataFormats.Text, true, json);
                    UIServices.Clipboard.AddData(DataFormats.UnicodeText, true, json);
                }
                else
                {
                    UIServices.Clipboard.SetData(DataFormats.UnicodeText, true, json);
                }
                SdtJsonCommands.Status($"{Title}: {what} copiado como JSON");
            }
            catch (Exception ex)
            {
                SdtJsonCommands.ShowError("No se pudo copiar la selección", ex);
            }
        }

        /// <summary>
        /// El JSON de lo seleccionado: con la raíz seleccionada (o nada), el SDT entero como el botón
        /// Copiar; si no, el arreglo de los ítems.
        /// </summary>
        static string SelectionJson(SDTEditor editor, out string what)
        {
            ((IGxView)editor).UpdateData();   // la grilla baja al part lo que no se grabó
            SDTStructurePart structure = Structure(editor);

            // La fila "ítem de colección" representa a su colección
            List<StructItem> selected = Editor.SelectedItems(editor)
                .Select(i => i.Data is IStructureItem ? i : i.Parent)
                .Where(i => i != null)
                .Distinct()
                .ToList();
            if (selected.Count == 0 || selected.Any(i => i.Parent == null))
            {
                SdtNode root = SdtReader.Read(structure.SDT, structure);
                what = $"{structure.SDT.Name} completo ({root.CountMembers()} miembros)";
                return SdtJsonFormat.Write(root);
            }

            // Si se eligió un nivel y también algo de adentro, va el nivel (una vez)
            selected = selected.Where(i => !selected.Any(other => other != i && i.IsDescendandOf(other))).ToList();
            List<SdtNode> nodes = SdtReader.ReadItems(selected.Select(i => (IStructureItem)i.Data));
            what = nodes.Count == 1 ? nodes[0].Name : $"{nodes.Count} ítems";
            return SdtJsonFormat.WriteItems(nodes);
        }

        static void Paste(SDTEditor editor)
        {
            string text = ClipboardText();
            if (!LooksLikeJson(text))
            {
                RunEditorCommand(editor, CommandKeys.Core.Paste);   // objetos de la KB, como siempre
                return;
            }

            SdtNode node;
            try
            {
                node = SdtJsonFormat.Read(text);
            }
            catch (FormatException ex)
            {
                MessageBox.Show("El portapapeles tiene un JSON que no es la estructura de un SDT:" + Environment.NewLine + ex.Message,
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var writer = new SdtWriter(Structure(editor).SDT.Model);
                List<string> renamed = Insert(editor, node.Items, writer);
                if (renamed == null)
                    return;
                string done = $"{writer.Created} ítems pegados. Falta grabar el SDT.";
                if (renamed.Count > 0)
                    done += " Ya existían, quedaron como " + string.Join(", ", renamed) + ".";
                SdtJsonCommands.Report(done, writer.Warnings);
            }
            catch (Exception ex)
            {
                SdtJsonCommands.ShowError("No se pudo pegar", ex);
            }
        }

        /// <summary>
        /// Crea los ítems y los inserta donde insertaría el editor un ítem nuevo (después del miembro
        /// actual, o al final del nivel actual), en una sola operación para deshacer. Los nombres que ya
        /// existen ahí se numeran, como al arrastrar atributos. Devuelve esos renombres, o null si ahí
        /// no se puede pegar.
        /// </summary>
        static List<string> Insert(SDTEditor editor, List<SdtNode> nodes, SdtWriter writer)
        {
            SDTStructurePart structure = Structure(editor);
            StructItem current = Editor.CurrentItem(editor) ?? editor.GetRootItem();
            if (current == null)
                return null;

            // Se arman en un nivel auxiliar con el mismo código que importar y se sueltan: quedan como
            // los ítems que crea la grilla, que pasan al part al grabar.
            var holder = new SDTLevel(structure);
            writer.AddItems(holder, nodes);
            List<IStructureItem> data = holder.Items.Cast<IStructureItem>().ToList();
            SdtWriter.Clear(holder);
            var adapter = new SDTEditorAdapter(editor, structure);
            List<StructItem> items = data.Select(d => Editor.Construct(adapter, d)).ToList();
            if (items.Count == 0)
                return null;

            StructPosition position = current.GetInsertPositionFor(items[0].Kind);
            if (position.RefType == RefTypeEnum.Undefined)
            {
                SdtJsonCommands.Status($"{Title}: no se puede pegar en {current.Text}");
                return null;
            }
            StructItem parent = position.RefType == RefTypeEnum.After || position.RefType == RefTypeEnum.Before
                ? position.RefItem.Parent
                : position.RefItem;

            var renamed = new List<string>();
            var names = new HashSet<string>(parent.Items.Select(i => i.Text), StringComparer.OrdinalIgnoreCase);
            foreach (StructItem item in items)
            {
                string name = ((IStructureItem)item.Data).Name;
                string unique = UniqueName(name, names);
                names.Add(unique);
                if (unique != name)
                {
                    Rename(item.Data, unique);
                    renamed.Add(unique);
                }
            }

            Editor.BeginMacro(editor, "Paste JSON");
            try
            {
                foreach (StructItem item in items)
                {
                    editor.ExecuteCommand(new CmdInsertItem(position, item));
                    position = StructPosition.After(item);
                }
            }
            finally
            {
                Editor.EndMacro(editor);
            }
            return renamed;
        }

        static string UniqueName(string name, HashSet<string> names)
        {
            if (!names.Contains(name))
                return name;
            for (int n = 1; ; n++)
            {
                string candidate = name + n;
                if (!names.Contains(candidate))
                    return candidate;
            }
        }

        static void Rename(object data, string name)
        {
            switch (data)
            {
                case SDTItem item:
                    item.Name = name;
                    break;
                case SDTLevel level:
                    level.Name = name;
                    break;
            }
        }

        /// <summary>Ejecuta un comando del editor (copiar o pegar de GeneXus) si está habilitado.</summary>
        static bool RunEditorCommand(SDTEditor editor, CommandKey command)
        {
            var status = new CommandStatus(CommandState.Disabled);
            return editor.QueryState(command, CommandData.Empty, ref status)
                && status.State == CommandState.Enabled
                && editor.Exec(command, CommandData.Empty);
        }

        static SDTStructurePart Structure(SDTEditor editor) =>
            (((IGxView)editor).Viewed as IGxDocumentPart)?.Part as SDTStructurePart
            ?? throw new InvalidOperationException("No encontré la estructura del SDT en el editor.");

        static string ClipboardText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (ExternalException)
            {
                return null;   // portapapeles ocupado por otra aplicación
            }
        }

        static bool LooksLikeJson(string text)
        {
            text = text?.TrimStart();
            return !string.IsNullOrEmpty(text) && (text[0] == '{' || text[0] == '[');
        }

        /// <summary>Lo que el editor no expone públicamente (GeneXus 18 U15).</summary>
        static class Editor
        {
            const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            static readonly PropertyInfo s_CurrentItem = typeof(StructEditor).GetProperty("CurrentItem", Members);
            static readonly MethodInfo s_IsEditing = Method(typeof(StructEditor), "IsEditing");
            static readonly MethodInfo s_GetSelectedItems = Method(typeof(StructEditor), "GetSelectedItems");
            static readonly MethodInfo s_BeginMacro = Method(typeof(StructEditor), "BeginUpdateAndMacroCommand", typeof(string));
            static readonly MethodInfo s_EndMacro = Method(typeof(StructEditor), "EndUpdateAndMacroCommand");
            static readonly MethodInfo s_ConstructItem = Method(typeof(SDTEditorAdapter), "ConstructItem", typeof(SDTItem));
            static readonly MethodInfo s_ConstructLevel = Method(typeof(SDTEditorAdapter), "ConstructLevel", typeof(SDTLevel));

            public static bool IsEditing(StructEditor editor) => (bool)Call(s_IsEditing, "IsEditing", editor);

            public static StructItem CurrentItem(StructEditor editor) =>
                (StructItem)(s_CurrentItem ?? throw Missing("CurrentItem")).GetValue(editor);

            /// <summary>En el orden de la grilla.</summary>
            public static List<StructItem> SelectedItems(StructEditor editor) =>
                (List<StructItem>)Call(s_GetSelectedItems, "GetSelectedItems", editor);

            public static void BeginMacro(StructEditor editor, string description) =>
                Call(s_BeginMacro, "BeginUpdateAndMacroCommand", editor, description);

            public static void EndMacro(StructEditor editor) => Call(s_EndMacro, "EndUpdateAndMacroCommand", editor);

            /// <summary>El ítem de la grilla para un miembro o nivel (con sus hijos y su fila de colección).</summary>
            public static StructItem Construct(SDTEditorAdapter adapter, IStructureItem data) =>
                data is SDTLevel level
                    ? (StructItem)Call(s_ConstructLevel, "ConstructLevel", adapter, level)
                    : (StructItem)Call(s_ConstructItem, "ConstructItem", adapter, (SDTItem)data);

            static MethodInfo Method(Type type, string name, params Type[] parameters) =>
                type.GetMethod(name, Members, null, parameters, null);

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
                new NotSupportedException($"El editor de SDT de esta versión de GeneXus no tiene {name} (GxSdtJson está hecho para GeneXus 18 U15).");
        }
    }
}
