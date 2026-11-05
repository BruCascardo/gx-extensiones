using System;
using System.Collections.Generic;
using System.Linq;
using Artech.Architecture.BL.Framework.Services;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using Artech.Udm.Framework;
using Artech.Udm.Framework.References;

namespace GxCodeCheck
{
    /// <summary>
    /// Revisa muchos objetos sin build: los cambios propios pendientes de commit, los modificados desde una
    /// fecha, los de un módulo o los de la KB (con los que usan a los cambiados, que son los que un cambio puede
    /// romper). Corre el mismo chequeo que al guardar sobre el
    /// Source de los procedimientos y los Events de los Web Panels y las transacciones: milisegundos por objeto.
    /// </summary>
    /// <remarks>
    /// Lo encontrado queda por objeto (<see cref="For"/>) para subrayarlo cuando se abre, hasta que se guarda.
    /// No usa la interfaz: también se puede correr desde una tarea de MSBuild.
    /// </remarks>
    static class KbCheck
    {
        public enum ScopeKind { Kb, Module, ChangedSince, Pending }

        /// <summary>Qué revisar.</summary>
        public sealed class Scope
        {
            public ScopeKind Kind;
            /// <summary>El módulo (nombre calificado), con sus submódulos.</summary>
            public string Module;
            public DateTime Since;
            /// <summary>Con los objetos que usan a los modificados (los que un cambio puede romper).</summary>
            public bool WithUsers = true;

            public override string ToString() =>
                Kind == ScopeKind.Kb ? "la KB"
                : Kind == ScopeKind.Module ? $"el módulo {Module}"
                : (Kind == ScopeKind.Pending ? "los cambios pendientes de commit" : $"lo modificado desde el {Since:dd/MM/yyyy HH:mm}")
                  + (WithUsers ? " y lo que los usa" : "");
        }

        /// <summary>Los tipos de objeto que se revisan: los que tienen Source o Events.</summary>
        static readonly Guid[] CheckedTypes = { ObjClass.Procedure, ObjClass.WebPanel, ObjClass.Transaction };

        public static bool IsChecked(Guid type) => CheckedTypes.Contains(type);

        /// <summary>
        /// Los tipos cuyo cambio puede romper a quien los usa (no un módulo, una carpeta o el modelo: sus
        /// referencias serían media KB).
        /// </summary>
        static readonly Guid[] UsedTypes =
        {
            ObjClass.Procedure, ObjClass.SDT, ObjClass.Domain, ObjClass.Attribute, ObjClass.Transaction,
            ObjClass.DataProvider, ObjClass.WebPanel, ObjClass.ExternalObject,
        };

        /// <summary>Los objetos a revisar, sin cargarlos. En <paramref name="changed"/>, cuántos objetos se modificaron (para ChangedSince).</summary>
        public static List<EntityKey> List(KBModel model, Scope scope, out int changed)
        {
            changed = 0;
            var keys = new List<EntityKey>();
            var seen = new HashSet<EntityKey>();
            void AddKey(EntityKey key)
            {
                if (IsChecked(key.Type) && seen.Add(key))
                    keys.Add(key);
            }

            switch (scope.Kind)
            {
                case ScopeKind.Kb:
                    AddModule(Artech.Architecture.Common.Objects.Module.GetRoot(model), AddKey);
                    break;
                case ScopeKind.Module:
                    Module module = FindModule(model, scope.Module);
                    if (module != null)
                        AddModule(module, AddKey);
                    break;
                case ScopeKind.ChangedSince:
                case ScopeKind.Pending:
                    // Pendiente de commit: lo editado en esta KB y no subido (lo que muestra el diálogo de Commit). Lo
                    // que trajo un Update no está, y no depende del usuario (que suele ser compartido)
                    var modified = (scope.Kind == ScopeKind.Pending
                            ? BLServices.TeamDevClient.GetLocalChanges(model).Where(h => h.Operation != Artech.Common.Mode.Deleted).Select(h => h.Key)
                            : model.Objects.GetKeys(scope.Since))
                        .Distinct().ToList();
                    changed = modified.Count(k => UsedTypes.Contains(k.Type) || IsChecked(k.Type));
                    foreach (EntityKey key in modified)
                        AddKey(key);
                    if (scope.WithUsers)
                    {
                        // Un SDT, un dominio, un procedimiento o un atributo que cambió: lo que lo usa puede haber quedado mal
                        foreach (EntityKey key in modified.Where(k => UsedTypes.Contains(k.Type)))
                            foreach (EntityReference reference in model.GetReferencesTo(key, LinkType.UsedObject))
                                AddKey(reference.From);
                    }
                    break;
            }
            return keys;
        }

        /// <summary>Los objetos del módulo y de sus submódulos, sin los de los módulos de solo lectura (los de GeneXus y los instalados).</summary>
        static void AddModule(Module module, Action<EntityKey> add)
        {
            if (module.IsReadOnly)
                return;
            foreach (KBObject obj in module.GetMembers(CheckedTypes))
                add(obj.Key);
            foreach (Module submodule in module.Submodules)
                AddModule(submodule, add);
        }

        /// <summary>El módulo por su nombre calificado; vacío es el módulo raíz.</summary>
        public static Module FindModule(KBModel model, string name) =>
            string.IsNullOrEmpty(name) ? Artech.Architecture.Common.Objects.Module.GetRoot(model)
            : Artech.Architecture.Common.Objects.Module.Get(model, new QualifiedName(name));

        /// <summary>Los módulos que se pueden revisar (no los de solo lectura), por nombre.</summary>
        public static List<string> Modules(KBModel model) =>
            Artech.Architecture.Common.Objects.Module.GetAll(model).Where(m => !m.IsReadOnly && !m.IsRoot)
                .Select(m => m.QualifiedName.ToString()).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Un objeto que no se revisa aunque esté en la lista: el de un módulo de solo lectura.</summary>
        public static bool Skip(KBObject obj) => IsNotGenerated(obj) || IsReadOnly(obj);

        static bool IsReadOnly(KBObject obj)
        {
            try
            {
                return obj.Module?.IsReadOnly == true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Un objeto con Generate Object en False: el build no lo genera, así que no lo puede hacer fallar.</summary>
        public static bool IsNotGenerated(KBObject obj)
        {
            try
            {
                object value = obj.GetPropertyValue<object>(GenerateObjectProperty);
                return value is bool generate ? !generate : string.Equals(value?.ToString(), bool.FalseString, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        const string GenerateObjectProperty = "GenerateObject";

        /// <summary>
        /// Lo que encuentra el chequeo en el objeto, por parte: los errores y, con <paramref name="keep"/>, lo
        /// demás que acepte (advertencias, reglas del estándar).
        /// </summary>
        public static List<(KBObjectPart Part, Diagnostic Diagnostic)> Check(KBObject obj, Func<Diagnostic, bool> keep)
        {
            var found = new List<(KBObjectPart, Diagnostic)>();
            var context = new KbTypeContext(obj);
            foreach (KBObjectPart part in new KBObjectPart[] { obj.Parts.Get<ProcedurePart>(), obj.Parts.Get<EventsPart>() })
            {
                if (!(part is ISource code) || string.IsNullOrEmpty(code.Source))
                    continue;
                var source = new SourceText(code.Source);
                List<Diagnostic> diagnostics = TypeChecker.Run(source, context);
                if (part is ProcedurePart && IsCommitOnExit(obj))
                    diagnostics.Add(TypeChecker.CommitOnExitWarning(source));
                foreach (Diagnostic d in diagnostics)
                {
                    if (d.Severity == Severity.Error || keep(d))
                        found.Add((part, d));
                }
            }
            return found;
        }

        /// <summary>Un procedimiento con Commit on exit en Yes (el estándar es No: el commit lo hace quien llama).</summary>
        public static bool IsCommitOnExit(KBObject obj)
        {
            try
            {
                return obj is Procedure
                    && string.Equals(obj.GetPropertyValue<object>(StandardProperty.CommitOnExit)?.ToString(), StandardProperty.Yes, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---- Lo encontrado, por objeto, para subrayarlo cuando se abre ----

        static readonly Dictionary<string, List<(Guid Part, Diagnostic Diagnostic)>> s_Found =
            new Dictionary<string, List<(Guid, Diagnostic)>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Cambió lo encontrado en un objeto (nombre calificado).</summary>
        public static event Action<string> Changed;

        /// <summary>Lo encontrado en el objeto en la última revisión, para esa parte.</summary>
        public static IEnumerable<Diagnostic> For(string objectName, Guid part) =>
            s_Found.TryGetValue(objectName, out var found) ? found.Where(f => f.Part == part).Select(f => f.Diagnostic) : Enumerable.Empty<Diagnostic>();

        /// <summary>Lo encontrado en un objeto (reemplaza lo de la revisión anterior).</summary>
        public static void Remember(string objectName, List<(KBObjectPart Part, Diagnostic Diagnostic)> found)
        {
            bool had = s_Found.Remove(objectName);
            if (found.Count > 0)
                s_Found[objectName] = found.Select(f => (f.Part.Type, f.Diagnostic)).ToList();
            if (had || found.Count > 0)
                Changed?.Invoke(objectName);
        }

        /// <summary>Se guardó el objeto: lo que se había encontrado ya no vale (lo revisa el editor).</summary>
        public static void Forget(string objectName)
        {
            if (s_Found.Remove(objectName))
                Changed?.Invoke(objectName);
        }

        public static void Clear()
        {
            var names = s_Found.Keys.ToList();
            s_Found.Clear();
            foreach (string name in names)
                Changed?.Invoke(name);
        }
    }

    /// <summary>La propiedad Commit on exit de los procedimientos y sus valores.</summary>
    static class StandardProperty
    {
        public const string CommitOnExit = "CommitOnExit";
        public const string Yes = "Yes";
        public const string No = "No";
    }
}
