using System;
using System.Runtime.InteropServices;
using Artech.Architecture.Common.Events;
using Artech.Architecture.Common.Packages;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Packages;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;
using Microsoft.Practices.CompositeUI.EventBroker;

[assembly: Package(typeof(GxQuickOpen.QuickOpenPackage))]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("GxQuickOpenPreview")]

namespace GxQuickOpen
{
    [Guid(PackageGuid)]
    public class QuickOpenPackage : AbstractPackageUI
    {
        public const string PackageGuid = "6847CA06-EE55-45C6-BFC3-5CC9F4718CD0";

        public static readonly CommandKey ShowCommand = new CommandKey(new Guid(PackageGuid), "GxQuickOpenShow");
        public static readonly CommandKey OptionsCommand = new CommandKey(new Guid(PackageGuid), "GxQuickOpenOptions");

        KeyboardHook m_KeyboardHook;

        public override string Name => "GxQuickOpen";

        public override void Initialize(IGxServiceProvider services)
        {
            base.Initialize(services);
            AddCommand(ShowCommand, data => { QuickOpen.Show(); return true; }, QueryKbOpen);
            AddCommand(OptionsCommand, data => { UIServices.ToolsOptions.ShowOptions("GxQuickOpen"); return true; }, QueryAlwaysEnabled);
        }

        public override void PostInitialize()
        {
            base.PostInitialize();

            // El atajo se lee de la configuración en cada tecla: un cambio en Tools > Options
            // se aplica sin reiniciar GeneXus. Si no es Ctrl+O, el Ctrl+O de GeneXus queda como siempre.
            try
            {
                m_KeyboardHook = new KeyboardHook(() => Settings.ShowKey, QuickOpen.WhyNotFromKeyboard, QuickOpen.ShowDeferred);
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxQuickOpen: no se pudo instalar el atajo de teclado (" + ex.Message +
                                                     "); queda disponible en File > Abrir objeto (búsqueda rápida).");
            }

            try
            {
                UIServices.ToolsOptions.RegisterCategory(new OptionsCategory());
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxQuickOpen: no se pudo agregar la página de opciones: " + ex.Message);
            }
        }

        public override void Close()
        {
            m_KeyboardHook?.Dispose();
            m_KeyboardHook = null;
            base.Close();
        }

        // Cualquier forma de abrir un objeto (KB Explorer, Ctrl+O, F12, referencias...) cuenta como reciente
        [EventSubscription("event://UI/AfterOpenKBObject")]
        public void OnObjectOpened(object sender, KBObjectEventArgs args)
        {
            try
            {
                if (args?.KBObject != null)
                    Recents.Record(QuickOpen.KbKey, args.KBObject.Guid);
            }
            catch (Exception)
            {
                // Los recientes son un extra: nunca molestar al abrir
            }
        }

        [EventSubscription("event://KnowledgeBase/AfterSaveKBObject")]
        [EventSubscription("event://KnowledgeBase/AfterDeleteKBObject")]
        public void OnKBObjectChanged(object sender, KBObjectEventArgs args)
        {
            ObjectIndex.Invalidate();
        }

        [EventSubscription("event://KnowledgeBase/AfterCloseKB")]
        public void OnKBClosed(object sender, EventArgs args)
        {
            ObjectIndex.Clear();
        }

        static bool QueryKbOpen(CommandData data, ref CommandStatus status)
        {
            status.State = QuickOpen.IsAvailable ? CommandState.Enabled : CommandState.Disabled;
            return true;
        }

        static bool QueryAlwaysEnabled(CommandData data, ref CommandStatus status)
        {
            status.State = CommandState.Enabled;
            return true;
        }
    }
}
