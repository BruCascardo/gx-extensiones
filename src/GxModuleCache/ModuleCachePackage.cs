using System;
using System.Runtime.InteropServices;
using Artech.Architecture.Common.Packages;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Packages;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;

[assembly: Package(typeof(GxModuleCache.ModuleCachePackage))]

namespace GxModuleCache
{
    [Guid(PackageGuid)]
    public class ModuleCachePackage : AbstractPackageUI
    {
        public const string PackageGuid = "2CEDD3C4-6C5F-4292-AFB0-71E1BC0A19B0";

        public static readonly CommandKey OptionsCommand = new CommandKey(new Guid(PackageGuid), "GxModuleCacheOptions");

        ToolWindowWatcher m_Watcher;

        public override string Name => "GxModuleCache";

        public override void Initialize(IGxServiceProvider services)
        {
            base.Initialize(services);
            AddCommand(OptionsCommand, data => { UIServices.ToolsOptions.ShowOptions("GxModuleCache"); return true; }, QueryAlwaysEnabled);
        }

        public override void PostInitialize()
        {
            base.PostInitialize();

            try
            {
                m_Watcher = new ToolWindowWatcher();
                Log.Write("GxModuleCache " + typeof(ModuleCachePackage).Assembly.GetName().Version + " cargado");
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxModuleCache: no se pudo instalar: " + ex.Message);
            }

            try
            {
                UIServices.ToolsOptions.RegisterCategory(new OptionsCategory());
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxModuleCache: no se pudo agregar la página de opciones: " + ex.Message);
            }
        }

        public override void Close()
        {
            m_Watcher?.Dispose();
            base.Close();
        }

        static bool QueryAlwaysEnabled(CommandData data, ref CommandStatus status)
        {
            status.State = CommandState.Enabled;
            return true;
        }
    }
}
