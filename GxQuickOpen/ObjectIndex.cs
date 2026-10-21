using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Artech.Architecture.Common.Descriptors;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.UI.Framework.Services;

namespace GxQuickOpen
{
    sealed class IndexSnapshot
    {
        public KBModel Model;
        /// <summary>Versión de la caché de objetos de GeneXus con la que se armó.</summary>
        public int CacheVersion;
        public List<ObjectEntry> Entries;
    }

    /// <summary>
    /// Todos los objetos que se pueden abrir, sacados de la misma caché que usa el diálogo Ctrl+O de
    /// GeneXus (UIServices.Objects.Cache). Se arma en segundo plano y se rehace cuando cambia la caché
    /// o se graba/borra un objeto; mientras tanto se busca sobre la versión anterior.
    /// </summary>
    static class ObjectIndex
    {
        static readonly object s_Lock = new object();
        static IndexSnapshot s_Current;
        static Task<IndexSnapshot> s_Pending;
        static KBModel s_PendingModel;
        static volatile bool s_Dirty;

        /// <summary>El último índice armado para el modelo, o null.</summary>
        public static IndexSnapshot Current(KBModel model)
        {
            IndexSnapshot current = s_Current;
            return current != null && current.Model == model ? current : null;
        }

        public static bool IsStale(IndexSnapshot snapshot) =>
            snapshot == null || s_Dirty || snapshot.CacheVersion != CacheVersion;

        static int CacheVersion
        {
            get
            {
                try
                {
                    return UIServices.Objects.Cache.Version;
                }
                catch (Exception)
                {
                    return -1;
                }
            }
        }

        /// <summary>Se grabó o borró algo: rehacer la próxima vez.</summary>
        public static void Invalidate() => s_Dirty = true;

        public static void Clear()
        {
            lock (s_Lock)
            {
                s_Current = null;
                s_Dirty = false;
            }
        }

        /// <summary>Arma el índice en segundo plano (si ya se está armando para el modelo, devuelve esa tarea).</summary>
        public static Task<IndexSnapshot> RefreshAsync(KBModel model)
        {
            lock (s_Lock)
            {
                if (s_Pending != null && s_PendingModel == model)
                    return s_Pending;
                s_Dirty = false;
                int version = CacheVersion;
                s_PendingModel = model;
                Task<IndexSnapshot> task = null;
                task = Task.Run(() => Build(model, version)).ContinueWith(t =>
                {
                    lock (s_Lock)
                    {
                        if (s_Pending == task)
                        {
                            s_Pending = null;
                            s_PendingModel = null;
                        }
                        if (t.Status == TaskStatus.RanToCompletion && UIServices.KB?.CurrentModel == model)
                            s_Current = t.Result;
                        else if (t.Status != TaskStatus.RanToCompletion)
                            s_Dirty = true;
                    }
                    return t.Result;   // si falló, propaga la excepción
                }, TaskScheduler.Default);
                s_Pending = task;
                return task;
            }
        }

        // Propiedad "Type" del Web Panel (Properties.WBP.Type) y sus valores (Properties.WBP.Type_Values)
        const string WebPanelTypeProperty = "WEB_COMP";
        const string ComponentValue = "Yes";
        const string MasterPageValue = "Master";

        /// <summary>
        /// Guid → variante (componente / master page) de los Web Panels que no son páginas comunes, o
        /// null si no se pudo consultar. Se busca por valor de propiedad, sin abrir cada Web Panel.
        /// </summary>
        static Dictionary<Guid, bool> WebPanelVariants(KBModel model)
        {
            try
            {
                var started = DateTime.Now;
                var variants = new Dictionary<Guid, bool>();
                foreach (KBObject obj in model.Objects.GetByPropertyValue(WebPanelTypeProperty, ComponentValue))
                    if (obj != null)
                        variants[obj.Guid] = true;
                foreach (KBObject obj in model.Objects.GetByPropertyValue(WebPanelTypeProperty, MasterPageValue))
                    if (obj != null)
                        variants[obj.Guid] = false;
                Diag.Log($"web panels: {variants.Count(v => v.Value)} componentes, {variants.Count(v => !v.Value)} master pages ({(DateTime.Now - started).TotalMilliseconds:N0} ms)");
                // Toda KB tiene al menos una master page: si no encontró nada, la consulta no sirvió
                return variants.Count > 0 ? variants : null;
            }
            catch (Exception ex)
            {
                Diag.Log("web panels: no se pudo saber cuáles son componentes: " + ex.Message);
                return null;
            }
        }

        static IndexSnapshot Build(KBModel model, int version)
        {
            IObjectsService objects = UIServices.Objects;
            // null = tipo que no se abre en un editor (igual que el filtro del Ctrl+O original)
            var types = new Dictionary<Guid, TypeInfo>();
            var entries = new List<ObjectEntry>();
            Dictionary<Guid, bool> webPanelVariants = WebPanelVariants(model);
            TypeInfo webComponent = null, masterPage = null;

            foreach (IKBObject obj in objects.Cache.GetAll(model))
            {
                try
                {
                    if (obj == null)
                        continue;
                    if (!types.TryGetValue(obj.Type, out TypeInfo type))
                    {
                        type = objects.IsDescriptorSupported(obj.Type) ? MakeType(obj.TypeDescriptor) : null;
                        types[obj.Type] = type;
                        if (type != null && obj.TypeDescriptor.Name == TypeInfo.WebPanelInternalName)
                        {
                            if (webPanelVariants == null)
                            {
                                type.AddWebPanelFallbackAliases();
                            }
                            else
                            {
                                webComponent = type.WebComponentVariant();
                                masterPage = type.MasterPageVariant();
                            }
                        }
                    }
                    if (type == null)
                        continue;
                    if (webPanelVariants != null && webPanelVariants.TryGetValue(obj.Guid, out bool component)
                        && obj.TypeDescriptor.Name == TypeInfo.WebPanelInternalName)
                        type = component ? webComponent : masterPage;

                    entries.Add(new ObjectEntry
                    {
                        Name = obj.Name ?? string.Empty,
                        Module = obj.QualifiedName?.ModuleName ?? string.Empty,
                        Description = obj.Description ?? string.Empty,
                        Type = type,
                        Guid = obj.Guid,
                        Key = obj.Key,
                    });
                }
                catch (Exception)
                {
                    // Un objeto que no se puede leer no debe impedir buscar el resto
                }
            }

            return new IndexSnapshot { Model = model, CacheVersion = version, Entries = entries };
        }

        static TypeInfo MakeType(KBObjectDescriptor descriptor)
        {
            if (descriptor == null || !UIServices.DocumentManager.IsOpenable(descriptor))
                return null;
            return new TypeInfo
            {
                Id = descriptor.Id,
                Name = descriptor.Description ?? descriptor.Name,
                Aliases = TypeInfo.BuildAliases(descriptor.Name, descriptor.Description),
                Descriptor = descriptor,
            };
        }

        /// <summary>Ícono del tipo al tamaño pedido (se calcula una vez, en el hilo de la interfaz).</summary>
        public static Image IconFor(TypeInfo type, int size)
        {
            if (type.Icon != null || !(type.Descriptor is KBObjectDescriptor descriptor))
                return type.Icon;
            Image image = null;
            if (descriptor.Icon != null)
            {
                using (var icon = new Icon(descriptor.Icon, size, size))
                    image = icon.ToBitmap();
            }
            else if (descriptor.Bitmap != null)
            {
                image = new Bitmap(descriptor.Bitmap);
            }
            type.Icon = image ?? new Bitmap(1, 1);
            return type.Icon;
        }
    }
}
