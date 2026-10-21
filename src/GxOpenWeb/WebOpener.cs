using System;
using System.Diagnostics;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Controls;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;
using Artech.Genexus.Common;
using Artech.Genexus.Common.Entities;

namespace GxOpenWeb
{
    enum WebEnvironment
    {
        Local,
        Ci,
        Preprod,
    }

    /// <summary>
    /// Arma la URL de un Web Panel (base del ambiente + paquete Java + nombre calificado, en minúsculas)
    /// y la abre en el navegador predeterminado.
    /// </summary>
    static class WebOpener
    {
        const string PopupId = "GxOpenWebPopup";
        const string CorePackageGuid = "98121D96-A7D8-468b-9310-B1F468F812AE";
        const string KbPlaceholder = "{kb}";

        static IMenuList s_Popup;
        static bool s_PopupMissing;

        /// <summary>Las opciones (y el submenú Abrir) solo se ven en la pestaña de un Web Panel que es página web.</summary>
        public static bool QueryState(CommandData data, ref CommandStatus status)
        {
            bool visible = IsWebPage(FindObject(data));
            status.State = visible ? CommandState.Enabled : CommandState.Invisible;
            // Un submenú con todos sus ítems ocultos igual se muestra (vacío): se oculta el submenú entero.
            // El menú de la pestaña consulta los ítems antes de mirar si el submenú es visible.
            IMenuList popup = FindPopup();
            if (popup != null)
                popup.Visible = visible;
            return true;
        }

        public static void Open(CommandData data, WebEnvironment environment)
        {
            KBObject obj = FindObject(data);
            if (!IsWebPage(obj))
                return;

            string url = BuildUrl(obj, environment);
            if (url == null)
                return;

            Log.Write($"{environment}: {obj.QualifiedName} -> {url}");
            try
            {
                Process.Start(url);
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddErrorLine($"GxOpenWeb: no se pudo abrir {url}: {ex.Message}");
            }
        }

        static KBObject FindObject(CommandData data)
        {
            // El menú de la pestaña pasa el objeto como contexto; si no, la pestaña activa (la del clic derecho)
            return data?.Context as KBObject ?? UIServices.Environment?.ActiveDocument?.Object;
        }

        static bool IsWebPage(KBObject obj)
        {
            return obj != null
                && obj.Type == ObjClass.WebPanel
                && Properties.WBP.GetType(obj) == Properties.WBP.Type_Values.WebPage;
        }

        static string BuildUrl(KBObject obj, WebEnvironment environment)
        {
            GxGenerator java = FindJavaGenerator(obj);
            string baseUrl;
            switch (environment)
            {
                case WebEnvironment.Local:
                    baseUrl = string.IsNullOrWhiteSpace(Settings.LocalUrl)
                        ? java?.Properties.GetPropertyValue<string>(Properties.JAVA.WebRoot)
                        : Settings.LocalUrl;
                    break;
                case WebEnvironment.Ci:
                    baseUrl = Settings.CiUrl;
                    break;
                default:
                    baseUrl = Settings.PreprodUrl;
                    break;
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                CommonServices.Output.AddErrorLine($"GxOpenWeb: no hay URL base para {environment}. Configurala en Tools > Options > GxOpenWeb.");
                return null;
            }

            baseUrl = baseUrl.Trim().Replace(KbPlaceholder, obj.KB.Name.ToLowerInvariant());
            if (!baseUrl.EndsWith("/"))
                baseUrl += "/";

            string javaPackage = java?.Properties.GetPropertyValue<string>(Properties.JAVA.JavaPackageName);
            string objectPath = string.IsNullOrWhiteSpace(javaPackage)
                ? obj.QualifiedName.ToString()
                : javaPackage.Trim() + "." + obj.QualifiedName;
            return baseUrl + objectPath.ToLowerInvariant();
        }

        /// <summary>El environment por defecto si es Java web; si no, el primer Java web de la KB.</summary>
        static GxGenerator FindJavaGenerator(KBObject obj)
        {
            try
            {
                GxModel model = obj.KB.DesignModel.Environment.TargetModel.GetAs<GxModel>();
                GxGenerator generator = model.Generator;
                if (generator?.GeneratorType == GeneratorType.JavaWeb)
                    return generator;
                return model.GetGenerator(GeneratorType.JavaWeb);
            }
            catch (Exception ex)
            {
                Log.Write("No se pudo leer el environment Java: " + ex);
                return null;
            }
        }

        static IMenuList FindPopup()
        {
            if (s_Popup == null && !s_PopupMissing)
            {
                s_Popup = UIServices.Menu.GetMenuItem(new Guid(OpenWebPackage.PackageGuid), PopupId) as IMenuList
                    ?? UIServices.Menu.GetMenuItem(new Guid(CorePackageGuid), PopupId) as IMenuList;
                s_PopupMissing = s_Popup == null;
                if (s_PopupMissing)
                    Log.Write("No encontré el submenú " + PopupId + " para ocultarlo");
            }
            return s_Popup;
        }
    }
}
