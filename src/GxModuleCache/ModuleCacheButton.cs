using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Services;

namespace GxModuleCache
{
    /// <summary>
    /// Botón "Limpiar caché" a la derecha de la barra "Module Information:" de Manage Module References (la de
    /// "Servers: Add" es angosta y lo mandaría al desborde). Borra la caché de NuGet con <see cref="NuGetCache"/>
    /// y vuelve a cargar la lista de módulos del servidor elegido.
    /// </summary>
    static class ModuleCacheButton
    {
        const string ButtonName = "GxModuleCacheClear";
        const string ButtonText = "Limpiar caché";

        public static void Attach(Control window)
        {
            // toolStrip3 es la barra "Module Information:" (el diseñador le deja ese nombre)
            Control[] found = window.Controls.Find("toolStrip3", true);
            if (found.Length == 0 || !(found[0] is ToolStrip toolStrip))
            {
                Log.Write("No encontré la barra Module Information en " + window.GetType().FullName);
                return;
            }
            if (toolStrip.Items.ContainsKey(ButtonName))
                return;

            var button = new ToolStripButton(ButtonText)
            {
                Name = ButtonName,
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = "Borra la caché local de NuGet (nuget locals all -clear) y vuelve a cargar los módulos.\n" +
                              "Sirve cuando se entró antes de que un módulo terminara de subirse al servidor.",
            };
            button.Click += async (s, e) => await ClearAsync(window, button);
            toolStrip.Items.Add(button);
        }

        static async Task ClearAsync(Control window, ToolStripButton button)
        {
            button.Enabled = false;
            button.Text = "Limpiando caché...";
            UIServices.StatusBar.DisplayText("GxModuleCache: limpiando la caché de módulos...");
            try
            {
                NuGetCache.Result result = await Task.Run(() => NuGetCache.Clear());
                if (window.IsDisposed)
                    return;
                if (result.Success)
                {
                    UIServices.StatusBar.DisplayText("GxModuleCache: caché de módulos limpia");
                    Reload(window);
                }
                else
                {
                    CommonServices.Output.AddErrorLine("GxModuleCache: no se pudo limpiar la caché de módulos. " + result.Output);
                    UIServices.StatusBar.DisplayText("GxModuleCache: no se pudo limpiar la caché (detalle en Output)");
                }
                Log.Write("nuget locals all -clear: " + (result.Success ? "ok" : "error") + ". " + result.Output);
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddErrorLine("GxModuleCache: no se pudo limpiar la caché de módulos: " + ex.Message);
                Log.Write("Error al limpiar la caché: " + ex);
            }
            finally
            {
                if (!button.IsDisposed)
                {
                    button.Text = ButtonText;
                    button.Enabled = true;
                }
            }
        }

        /// <summary>
        /// Vuelve a pedir los módulos del servidor elegido con el filtro actual, como al cambiar de servidor. La
        /// clase de la pantalla es internal: StartLoading y FilterPattern son públicos pero se llaman por reflexión.
        /// </summary>
        static void Reload(Control window)
        {
            Type type = window.GetType();
            MethodInfo startLoading = type.GetMethod("StartLoading", new[] { typeof(string) });
            PropertyInfo filter = type.GetProperty("FilterPattern");
            if (startLoading == null)
            {
                Log.Write("No encontré StartLoading en " + type.FullName + ": la lista se actualiza al cambiar de servidor");
                return;
            }
            startLoading.Invoke(window, new[] { filter?.GetValue(window) });
        }
    }
}
