using System;
using System.Runtime.InteropServices;
using Artech.Architecture.Common.Packages;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Objects;
using Artech.Architecture.UI.Framework.Packages;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;
using Artech.Packages.Genexus.UI.Services;
using Microsoft.Practices.CompositeUI.EventBroker;

[assembly: Package(typeof(GxCodeCheck.CodeCheckPackage))]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("GxCodeCheckPreview")]

namespace GxCodeCheck
{
    [Guid(PackageGuid)]
    public class CodeCheckPackage : AbstractPackageUI
    {
        public const string PackageGuid = "CEC6ED1B-174C-48A7-9161-E814A7F8B695";

        public static readonly CommandKey OptionsCommand = new CommandKey(new Guid(PackageGuid), "GxCodeCheckOptions");
        public static readonly CommandKey ReviewCommand = new CommandKey(new Guid(PackageGuid), "GxCodeCheckReview");

        static EditorWatcher s_Editors;
        OutputWatcher m_Output;
        KeyFilter m_Keys;

        public override string Name => "GxCodeCheck";

        public override void Initialize(IGxServiceProvider services)
        {
            base.Initialize(services);
            AddCommand(OptionsCommand, data => { UIServices.ToolsOptions.ShowOptions("GxCodeCheck"); return true; }, QueryAlwaysEnabled);
            AddCommand(ReviewCommand, data => { ShowReview(); return true; }, QueryKbOpen);
        }

        public override void PostInitialize()
        {
            base.PostInitialize();

            try
            {
                s_Editors = new EditorWatcher();
                m_Output = new OutputWatcher();
                m_Keys = new KeyFilter(control => s_Editors?.SessionFor(control));
                System.Windows.Forms.Application.AddMessageFilter(m_Keys);
                UIServices.DocumentManager.BeforeSaveDocument += OnBeforeSaveDocument;
                UIServices.DocumentManager.AfterSaveDocument += OnAfterSaveDocument;
                ApplySpecifierDelay();
                Log.Write($"GxCodeCheck {typeof(CodeCheckPackage).Assembly.GetName().Version} cargado; Background Build a los {BackgroundBuildService.BACKGROUND_DELAY} ms de guardar");
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxCodeCheck: no se pudo instalar: " + ex.Message);
            }

            try
            {
                UIServices.ToolsOptions.RegisterCategory(new OptionsCategory());
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxCodeCheck: no se pudo agregar la página de opciones: " + ex.Message);
            }
        }

        public override void Close()
        {
            UIServices.DocumentManager.BeforeSaveDocument -= OnBeforeSaveDocument;
            UIServices.DocumentManager.AfterSaveDocument -= OnAfterSaveDocument;
            m_Output?.Dispose();
            if (m_Keys != null)
                System.Windows.Forms.Application.RemoveMessageFilter(m_Keys);
            s_Editors?.Dispose();
            base.Close();
        }

        /// <summary>Aplica las opciones: la espera del Background Build y los editores abiertos.</summary>
        internal static void RefreshAll()
        {
            ApplySpecifierDelay();
            if (s_Editors == null)
                return;
            foreach (EditorSession session in s_Editors.Sessions)
                session.Refresh();
        }

        internal static void Status(string message)
        {
            if (UIServices.IsStatusBarAvailable)
                UIServices.StatusBar.DisplayText(message);
        }

        /// <summary>
        /// GeneXus espera 3 segundos después de guardar para especificar en segundo plano (por si se guardan
        /// varios objetos seguidos). Es un campo público estático que lee antes de cada objeto.
        /// </summary>
        static void ApplySpecifierDelay()
        {
            BackgroundBuildService.BACKGROUND_DELAY = Settings.Enabled && Settings.ShowSpecifier ? Settings.SpecifierDelay : GeneXusSpecifierDelay;
        }

        const int GeneXusSpecifierDelay = 3000;

        // Antes de que GeneXus valide: lo informado del objeto ya no vale y los editores empiezan a revisar el texto
        void OnBeforeSaveDocument(object sender, DocumentCancelEventArgs e)
        {
            try
            {
                string name = e.Document?.Object?.QualifiedName.ToString();
                if (name == null || !Settings.Enabled)
                    return;
                SpecStore.SaveStarted(name);
                Log.Write("Guardando " + name);
                foreach (EditorSession session in s_Editors.Sessions)
                {
                    if (string.Equals(session.ObjectName, name, StringComparison.OrdinalIgnoreCase))
                        session.Saving();
                }
            }
            catch (Exception ex)
            {
                Log.Write("Error al guardar: " + ex);
            }
        }

        // Guardó: el chequeo de tipos y la especificación (si no lo pudo guardar por errores de sintaxis, el editor no lo chequea)
        void OnAfterSaveDocument(object sender, DocumentEventArgs e)
        {
            try
            {
                string name = e.Document?.Object?.QualifiedName.ToString();
                if (name == null)
                    return;
                KbTypeContext.ObjectSaved(e.Document.Object);
                KbCheck.Forget(name);
                if (!Settings.Enabled)
                    return;
                CallerCheck.Saved(e.Document.Object);
                if (Settings.ShowSpecifier)
                    Status($"GxCodeCheck: esperando la especificación de {name}...");
                foreach (EditorSession session in s_Editors.Sessions)
                {
                    if (string.Equals(session.ObjectName, name, StringComparison.OrdinalIgnoreCase))
                        session.Saved();
                }
            }
            catch (Exception ex)
            {
                Log.Write("Error después de guardar: " + ex);
            }
        }

        [EventSubscription("event://KnowledgeBase/AfterCloseKB")]
        public void OnKBClosed(object sender, EventArgs args)
        {
            SpecStore.Clear();
            KbTypeContext.Clear();
            CallerCheck.Clear();
            KbCheck.Clear();
        }

        static bool QueryKbOpen(CommandData data, ref CommandStatus status)
        {
            status.State = UIServices.KB?.CurrentModel != null ? CommandState.Enabled : CommandState.Disabled;
            return true;
        }

        /// <summary>El diálogo de la revisión, con el módulo del objeto que se está editando.</summary>
        static void ShowReview()
        {
            try
            {
                string module = null;
                var focused = System.Windows.Forms.Control.FromChildHandle(GetFocus());
                EditorSession session = focused == null ? null : s_Editors?.SessionFor(focused);
                if (session != null)
                    module = session.ModuleName;
                ReviewForm.Show(UIServices.KB?.CurrentModel, module);
            }
            catch (Exception ex)
            {
                Log.Write("Error en la revisión del código: " + ex);
                CommonServices.Output.AddErrorLine("GxCodeCheck: no se pudo revisar el código: " + ex.Message);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr GetFocus();

        static bool QueryAlwaysEnabled(CommandData data, ref CommandStatus status)
        {
            status.State = CommandState.Enabled;
            return true;
        }
    }
}
