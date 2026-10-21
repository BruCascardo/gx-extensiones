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
            AddCommand(DeclareCommand, data => { VariableDeclarer.Run(data); return true; }, QueryEditorCommand);
            AddCommand(DeclareAllCommand, data => { VariableDeclarer.RunAll(data); return true; }, QueryEditorCommand);
            AddCommand(OptionsCommand, data => { UIServices.ToolsOptions.ShowOptions("GxQuickVar"); return true; }, QueryAlwaysEnabled);
        }

        public override void PostInitialize()
        {
            base.PostInitialize();

            // Los atajos se leen de la configuración en cada tecla, así un cambio en
            // Tools > Options se aplica sin reiniciar GeneXus.
            m_KeyFilter = new KeyBindingFilter();
            m_KeyFilter.Add(() => Settings.DeclareKey, () => VariableDeclarer.Run(null));
            m_KeyFilter.Add(() => Settings.DeclareAllKey, () => VariableDeclarer.RunAll(null));
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
