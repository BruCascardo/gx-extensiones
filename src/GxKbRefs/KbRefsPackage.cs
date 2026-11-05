using System;
using System.Runtime.InteropServices;
using Artech.Architecture.Common.Packages;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Packages;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;

[assembly: Package(typeof(GxKbRefs.KbRefsPackage))]

namespace GxKbRefs
{
    [Guid(PackageGuid)]
    public class KbRefsPackage : AbstractPackageUI
    {
        public const string PackageGuid = "95204E25-EF94-4DDD-A0BD-9D207E679150";

        public static readonly CommandKey OptionsCommand = new CommandKey(new Guid(PackageGuid), "GxKbRefsOptions");

        ReferencesWatcher m_Watcher;

        public override string Name => "GxKbRefs";

        public override void Initialize(IGxServiceProvider services)
        {
            base.Initialize(services);
            AddCommand(OptionsCommand, data => { UIServices.ToolsOptions.ShowOptions("GxKbRefs"); return true; }, QueryAlwaysEnabled);
        }

        public override void PostInitialize()
        {
            base.PostInitialize();

            try
            {
                m_Watcher = new ReferencesWatcher();
                Log.Write("GxKbRefs " + typeof(KbRefsPackage).Assembly.GetName().Version + " cargado");
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxKbRefs: no se pudo instalar: " + ex.Message);
            }

            try
            {
                UIServices.ToolsOptions.RegisterCategory(new OptionsCategory());
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxKbRefs: no se pudo agregar la página de opciones: " + ex.Message);
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
