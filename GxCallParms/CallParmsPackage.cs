using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Artech.Architecture.Common.Packages;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Packages;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;

[assembly: Package(typeof(GxCallParms.CallParmsPackage))]

namespace GxCallParms
{
    [Guid(PackageGuid)]
    public class CallParmsPackage : AbstractPackageUI
    {
        public const string PackageGuid = "1063769E-C254-4F9B-BABB-DBAD176DD5F7";

        public static readonly CommandKey FillCommand = new CommandKey(new Guid(PackageGuid), "GxCallParmsFill");
        public static readonly CommandKey OptionsCommand = new CommandKey(new Guid(PackageGuid), "GxCallParmsOptions");

        KeyFilter m_KeyFilter;

        public override string Name => "GxCallParms";

        public override void Initialize(IGxServiceProvider services)
        {
            base.Initialize(services);
            AddCommand(FillCommand, data => { CallFiller.Run(data); return true; }, QueryEditorCommand);
            AddCommand(OptionsCommand, data => { UIServices.ToolsOptions.ShowOptions("GxCallParms"); return true; }, QueryAlwaysEnabled);
        }

        public override void PostInitialize()
        {
            base.PostInitialize();

            m_KeyFilter = new KeyFilter();
            Application.AddMessageFilter(m_KeyFilter);
            Log.Write("GxCallParms " + typeof(CallParmsPackage).Assembly.GetName().Version + " cargado");

            try
            {
                UIServices.ToolsOptions.RegisterCategory(new OptionsCategory());
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxCallParms: no se pudo agregar la página de opciones: " + ex.Message);
            }
        }

        public override void Close()
        {
            if (m_KeyFilter != null)
                Application.RemoveMessageFilter(m_KeyFilter);
            base.Close();
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
