using System;
using System.Runtime.InteropServices;
using Artech.Architecture.Common.Packages;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Packages;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;

[assembly: Package(typeof(GxOpenWeb.OpenWebPackage))]

namespace GxOpenWeb
{
    [Guid(PackageGuid)]
    public class OpenWebPackage : AbstractPackageUI
    {
        public const string PackageGuid = "F05C67CA-B75E-4774-AA28-9B8A11EDFD54";

        public static readonly CommandKey LocalCommand = new CommandKey(new Guid(PackageGuid), "GxOpenWebLocal");
        public static readonly CommandKey CiCommand = new CommandKey(new Guid(PackageGuid), "GxOpenWebCi");
        public static readonly CommandKey PreprodCommand = new CommandKey(new Guid(PackageGuid), "GxOpenWebPreprod");
        public static readonly CommandKey OptionsCommand = new CommandKey(new Guid(PackageGuid), "GxOpenWebOptions");

        public override string Name => "GxOpenWeb";

        public override void Initialize(IGxServiceProvider services)
        {
            base.Initialize(services);
            AddCommand(LocalCommand, data => { WebOpener.Open(data, WebEnvironment.Local); return true; }, WebOpener.QueryState);
            AddCommand(CiCommand, data => { WebOpener.Open(data, WebEnvironment.Ci); return true; }, WebOpener.QueryState);
            AddCommand(PreprodCommand, data => { WebOpener.Open(data, WebEnvironment.Preprod); return true; }, WebOpener.QueryState);
            AddCommand(OptionsCommand, data => { UIServices.ToolsOptions.ShowOptions("GxOpenWeb"); return true; }, QueryAlwaysEnabled);
        }

        public override void PostInitialize()
        {
            base.PostInitialize();
            Log.Write("GxOpenWeb " + typeof(OpenWebPackage).Assembly.GetName().Version + " cargado");

            try
            {
                UIServices.ToolsOptions.RegisterCategory(new OptionsCategory());
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxOpenWeb: no se pudo agregar la página de opciones: " + ex.Message);
            }
        }

        static bool QueryAlwaysEnabled(CommandData data, ref CommandStatus status)
        {
            status.State = CommandState.Enabled;
            return true;
        }
    }
}
