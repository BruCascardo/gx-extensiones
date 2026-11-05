using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Artech.Architecture.Common.Events;
using Artech.Architecture.Common.Packages;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Packages;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;
using Artech.Genexus.Common;
using Artech.Genexus.UI.Common.Editor;
using Microsoft.Practices.CompositeUI.EventBroker;

[assembly: Package(typeof(GxQuickVar.QuickVarPackage))]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("GxQuickVarPreview")]

namespace GxQuickVar
{
    [Guid(PackageGuid)]
    public class QuickVarPackage : AbstractPackageUI
    {
        public const string PackageGuid = "3E8F6A42-9C1D-4B7E-A5F0-6D2C81B4E937";

        public static readonly CommandKey DeclareCommand = new CommandKey(new Guid(PackageGuid), "GxQuickVarDeclare");
        public static readonly CommandKey DeclareAllCommand = new CommandKey(new Guid(PackageGuid), "GxQuickVarDeclareAll");
        public static readonly CommandKey OptionsCommand = new CommandKey(new Guid(PackageGuid), "GxQuickVarOptions");

        KeyBindingFilter m_KeyFilter;

        public override string Name => "GxQuickVar";

        public override void Initialize(IGxServiceProvider services)
        {
            base.Initialize(services);
            AddCommand(DeclareCommand, ExecDeclare, QueryDeclare);
            AddCommand(DeclareAllCommand, data => { VariableDeclarer.RunAll(data); return true; }, QueryEditorCommand);
            AddCommand(OptionsCommand, data => { UIServices.ToolsOptions.ShowOptions("GxQuickVar"); return true; }, QueryAlwaysEnabled);
        }

        public override void PostInitialize()
        {
            base.PostInitialize();

            // Los atajos se leen de la configuración en cada tecla, así un cambio en
            // Tools > Options se aplica sin reiniciar GeneXus.
            m_KeyFilter = new KeyBindingFilter();
            m_KeyFilter.Add(() => Settings.DeclareKey, DeclareFor);
            m_KeyFilter.Add(() => Settings.DeclareAllKey, focused => IsEditableCode(focused) ? () => VariableDeclarer.RunAll(null) : (Action)null);
            Application.AddMessageFilter(m_KeyFilter);

            try
            {
                UIServices.ToolsOptions.RegisterCategory(new OptionsCategory());
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxQuickVar: no se pudo agregar la página de opciones: " + ex.Message);
            }
        }

        public override void Close()
        {
            if (m_KeyFilter != null)
                Application.RemoveMessageFilter(m_KeyFilter);
            base.Close();
        }

        // Mismos eventos que usa GeneXus para refrescar el combo de tipos de la grilla de variables
        [EventSubscription("event://KnowledgeBase/AfterSaveKBObject")]
        [EventSubscription("event://KnowledgeBase/AfterDeleteKBObject")]
        public void OnKBObjectChanged(object sender, KBObjectEventArgs args)
        {
            Guid type = args.KBObject.Type;
            if (type == ObjClass.Domain || type == ObjClass.SDT || type == ObjClass.Transaction || type == ObjClass.ExternalObject)
                TypeCatalog.Clear();
            else if (type == ObjClass.Attribute)
                TypeCatalog.ClearAttributes();
        }

        [EventSubscription("event://KnowledgeBase/AfterCloseKB")]
        public void OnKBClosed(object sender, EventArgs args)
        {
            TypeCatalog.Clear();
        }

        static bool IsEditableCode(Control focused)
        {
            var editor = EditorLocator.From(focused);
            return editor != null && !editor.ReadOnly;
        }

        /// <summary>
        /// Declarar según dónde esté el foco: la &amp;variable bajo el cursor en un editor de código, la fila
        /// de la pestaña Variables, la variable que muestra Properties o la variable de un pattern.
        /// </summary>
        static Action DeclareFor(Control focused)
        {
            if (EditorLocator.From(focused) != null)
                return IsEditableCode(focused) ? () => VariableDeclarer.Run(null) : (Action)null;
            return VariablesGrid.ForFocus(focused) ?? PatternVariables.ForFocus(focused);
        }

        /// <summary>Desde un menú: el control que lo pidió (el menú contextual de la grilla) o el que tiene el foco.</summary>
        static Action DeclareFor(CommandData data)
        {
            if (data?.Context is GxTextEditor editor)
                return editor.ReadOnly ? null : (Action)(() => VariableDeclarer.Run(data));
            Control focused = EditorLocator.FocusedControl();
            return (data?.Context is Control context ? DeclareFor(context) : null)
                ?? (focused != null ? DeclareFor(focused) : null);
        }

        static bool ExecDeclare(CommandData data)
        {
            Action action = DeclareFor(data);
            action?.Invoke();
            return action != null;
        }

        static bool QueryDeclare(CommandData data, ref CommandStatus status)
        {
            status.State = DeclareFor(data) != null ? CommandState.Enabled : CommandState.Disabled;
            return true;
        }

        static bool QueryEditorCommand(CommandData data, ref CommandStatus status)
        {
            var editor = EditorLocator.Find(data);
            status.State = editor != null && !editor.ReadOnly ? CommandState.Enabled : CommandState.Disabled;
            return true;
        }

        static bool QueryAlwaysEnabled(CommandData data, ref CommandStatus status)
        {
            status.State = CommandState.Enabled;
            return true;
        }
    }
}
