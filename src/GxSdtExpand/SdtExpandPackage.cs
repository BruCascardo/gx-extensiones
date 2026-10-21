using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Artech.Architecture.Common.Packages;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Packages;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;

[assembly: Package(typeof(GxSdtExpand.SdtExpandPackage))]

namespace GxSdtExpand
{
    [Guid(PackageGuid)]
    public class SdtExpandPackage : AbstractPackageUI
    {
        public const string PackageGuid = "DDA0FE6A-7D5C-4DC6-BB01-ADB22B3F04CD";

        public static readonly CommandKey ExpandCommand = new CommandKey(new Guid(PackageGuid), "GxSdtExpandRun");
        public static readonly CommandKey ExpandAllCommand = new CommandKey(new Guid(PackageGuid), "GxSdtExpandAll");
        public static readonly CommandKey OptionsCommand = new CommandKey(new Guid(PackageGuid), "GxSdtExpandOptions");

        KeyFilter m_KeyFilter;

        public override string Name => "GxSdtExpand";

        public override void Initialize(IGxServiceProvider services)
        {
            base.Initialize(services);
            AddCommand(ExpandCommand, data => { SdtExpander.Run(data, full: false); return true; }, QueryEditorCommand);
            AddCommand(ExpandAllCommand, data => { SdtExpander.Run(data, full: true); return true; }, QueryEditorCommand);
            AddCommand(OptionsCommand, data => { UIServices.ToolsOptions.ShowOptions("GxSdtExpand"); return true; }, QueryAlwaysEnabled);
        }

        public override void PostInitialize()
        {
            base.PostInitialize();

            m_KeyFilter = new KeyFilter();
            Application.AddMessageFilter(m_KeyFilter);
            Log.Write("GxSdtExpand " + typeof(SdtExpandPackage).Assembly.GetName().Version + " cargado");

            try
            {
                UIServices.ToolsOptions.RegisterCategory(new OptionsCategory());
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxSdtExpand: no se pudo agregar la página de opciones: " + ex.Message);
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
