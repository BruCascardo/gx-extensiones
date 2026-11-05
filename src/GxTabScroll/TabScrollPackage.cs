using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Artech.Architecture.Common.Packages;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Packages;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;

[assembly: Package(typeof(GxTabScroll.TabScrollPackage))]

namespace GxTabScroll
{
    [Guid(PackageGuid)]
    public class TabScrollPackage : AbstractPackageUI
    {
        public const string PackageGuid = "5DE9FEF7-9006-48F2-8351-C0F00B83E303";

        public static readonly CommandKey OptionsCommand = new CommandKey(new Guid(PackageGuid), "GxTabScrollOptions");

        WheelFilter m_WheelFilter;
        KeyFilter m_KeyFilter;

        public override string Name => "GxTabScroll";

        public override void Initialize(IGxServiceProvider services)
        {
            base.Initialize(services);
            AddCommand(OptionsCommand, data => { UIServices.ToolsOptions.ShowOptions("GxTabScroll"); return true; }, QueryAlwaysEnabled);
        }

        public override void PostInitialize()
        {
            base.PostInitialize();

            m_WheelFilter = new WheelFilter();
            Application.AddMessageFilter(m_WheelFilter);
            m_KeyFilter = new KeyFilter();
            Application.AddMessageFilter(m_KeyFilter);
            Log.Write("GxTabScroll " + typeof(TabScrollPackage).Assembly.GetName().Version + " cargado");

            try
            {
                UIServices.ToolsOptions.RegisterCategory(new OptionsCategory());
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxTabScroll: no se pudo agregar la página de opciones: " + ex.Message);
            }
        }

        public override void Close()
        {
            if (m_WheelFilter != null)
                Application.RemoveMessageFilter(m_WheelFilter);
            if (m_KeyFilter != null)
                Application.RemoveMessageFilter(m_KeyFilter);
            base.Close();
        }

        static bool QueryAlwaysEnabled(CommandData data, ref CommandStatus status)
        {
            status.State = CommandState.Enabled;
            return true;
        }
    }
}
