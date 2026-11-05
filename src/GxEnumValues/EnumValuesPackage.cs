using System;
using System.Runtime.InteropServices;
using Artech.Architecture.Common.Packages;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Packages;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;

[assembly: Package(typeof(GxEnumValues.EnumValuesPackage))]

namespace GxEnumValues
{
    [Guid(PackageGuid)]
    public class EnumValuesPackage : AbstractPackageUI
    {
        public const string PackageGuid = "3A0027E9-87B4-42FF-B450-6CC3F72C47DE";

        public static readonly CommandKey OptionsCommand = new CommandKey(new Guid(PackageGuid), "GxEnumValuesOptions");

        DialogHook m_Hook;

        public override string Name => "GxEnumValues";

        public override void Initialize(IGxServiceProvider services)
        {
            base.Initialize(services);
            AddCommand(OptionsCommand, data => { UIServices.ToolsOptions.ShowOptions("GxEnumValues"); return true; }, QueryAlwaysEnabled);
        }

        public override void PostInitialize()
        {
            base.PostInitialize();

            try
            {
                m_Hook = new DialogHook();
                Log.Write("GxEnumValues " + typeof(EnumValuesPackage).Assembly.GetName().Version + " cargado");
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxEnumValues: no se pudo instalar: " + ex.Message);
            }

            try
            {
                UIServices.ToolsOptions.RegisterCategory(new OptionsCategory());
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxEnumValues: no se pudo agregar la página de opciones: " + ex.Message);
            }
        }

        public override void Close()
        {
            m_Hook?.Dispose();
            base.Close();
        }

        static bool QueryAlwaysEnabled(CommandData data, ref CommandStatus status)
        {
            status.State = CommandState.Enabled;
            return true;
        }
    }
}
