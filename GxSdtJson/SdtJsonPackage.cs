using System;
using System.Runtime.InteropServices;
using Artech.Architecture.Common.Packages;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Packages;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;

[assembly: Package(typeof(GxSdtJson.SdtJsonPackage))]

namespace GxSdtJson
{
    [Guid(PackageGuid)]
    public class SdtJsonPackage : AbstractPackageUI
    {
        public const string PackageGuid = "59068EEB-297C-49F4-9E94-5505C3B214F2";

        public static readonly CommandKey CopyCommand = new CommandKey(new Guid(PackageGuid), "GxSdtJsonCopy");
        public static readonly CommandKey ImportCommand = new CommandKey(new Guid(PackageGuid), "GxSdtJsonImport");
        public static readonly CommandKey ImportNewCommand = new CommandKey(new Guid(PackageGuid), "GxSdtJsonImportNew");

        public override string Name => "GxSdtJson";

        public override void Initialize(IGxServiceProvider services)
        {
            base.Initialize(services);
            AddCommand(CopyCommand, data => { SdtJsonCommands.Copy(); return true; }, QueryCopy);
            AddCommand(ImportCommand, data => { SdtJsonCommands.Import(); return true; }, QueryImport);
            AddCommand(ImportNewCommand, data => { SdtJsonCommands.Import(); return true; }, QueryImportNew);
            if (UIServices.IsEnvironmentAvailable)
                UIServices.Environment.ActiveDocumentChanged += (sender, args) =>
                {
                    if (SdtDocument.IsActive())
                        SdtClipboard.Install();
                };
        }

        // Sin SDT activo los botones se ocultan: la barra "SDT JSON" aparece al abrir un SDT
        static bool QueryCopy(CommandData data, ref CommandStatus status)
        {
            bool active = SdtDocument.IsActive();
            if (active)
                SdtClipboard.EnsureInstalled();   // Ctrl+C / Ctrl+V en la grilla
            status.State = active ? CommandState.Enabled : CommandState.Invisible;
            status.Image = Icons.Copy;
            return true;
        }

        static bool QueryImport(CommandData data, ref CommandStatus status)
        {
            if (!SdtDocument.IsActive())
                status.State = CommandState.Invisible;
            else
                status.State = UIServices.Environment.ActiveDocument.Object.IsReadOnly ? CommandState.Disabled : CommandState.Enabled;
            status.Image = Icons.Import;
            return true;
        }

        static bool QueryImportNew(CommandData data, ref CommandStatus status)
        {
            status.State = UIServices.IsKBAvailable && UIServices.KB.CurrentKB != null ? CommandState.Enabled : CommandState.Disabled;
            status.Image = Icons.Import;
            return true;
        }
    }
}
