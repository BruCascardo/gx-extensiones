using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Artech.Architecture.Common.Location;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Diagnostics;

namespace GxCodeCheck
{
    /// <summary>
    /// Escucha todas las ventanas del Output (General, Build, Background Build...) y guarda en
    /// <see cref="SpecStore"/> los errores con su objeto, parte y línea: los del parser cuando GeneXus valida
    /// al guardar, y los de la especificación, que GeneXus hace en segundo plano después de cada guardado
    /// (Background Build) y en cada build. Cada objeto especificado empieza con una línea
    /// "Specifying Modulo.Objeto (1 of 1) ...", que reemplaza lo que había de su especificación anterior.
    /// </summary>
    /// <remarks>
    /// Como <see cref="IMultipleOutputTarget"/> el servicio de Output avisa de cada ventana (también las que
    /// se crean después) y se le devuelve un <see cref="IOutputTarget"/> por ventana con <see cref="OutputAdded"/>.
    /// Los mensajes pueden llegar desde otros hilos: se pasan al de la interfaz.
    /// </remarks>
    sealed class OutputWatcher : IMultipleOutputTarget, IDisposable
    {
        static readonly Regex SpecifyingLine = new Regex(@"^\s*Specifying (?<name>[\w.]+)", RegexOptions.IgnoreCase);
        // "Procedure 'Modulo.Objeto' Source (by WorkWithPlus), Line: -12, Char: 1": el nombre de la parte
        static readonly Regex PartInPosition = new Regex(@"'\s+(?<part>[A-Za-z][A-Za-z ]*?)\s*(\(by [^)]*\))?\s*(,|$)");
        static readonly Regex SpecificationEnd = new Regex(@"^\s*(Success|Failed): Specification", RegexOptions.IgnoreCase);

        readonly Dictionary<string, Target> m_Targets = new Dictionary<string, Target>();
        readonly ConditionalWeakTable<OutputError, object> m_Seen = new ConditionalWeakTable<OutputError, object>();
        readonly Control m_Ui;

        public event EventHandler<OutputTargetEventArgs> OutputAdded;

        public OutputWatcher()
        {
            // Un control con handle creado en el hilo de la interfaz, para pasarle los mensajes
            m_Ui = new Control();
            m_Ui.CreateControl();
            IntPtr unused = m_Ui.Handle;
            CommonServices.Output.AddListener(this);
        }

        public IEnumerable<string> OutputIds
        {
            get { lock (m_Targets) return m_Targets.Keys.ToList(); }
        }

        public bool AddOutput(string outputId)
        {
            Target target;
            lock (m_Targets)
            {
                // GeneXus vuelve a avisar de una ventana que ya tiene: registrarla otra vez duplica los mensajes
                if (m_Targets.ContainsKey(outputId))
                    return true;
                m_Targets[outputId] = target = new Target(this);
            }
            OutputAdded?.Invoke(this, new OutputTargetEventArgs(outputId, target));
            return true;
        }

        public IOutputTarget GetOutput(string outputId)
        {
            lock (m_Targets)
                return m_Targets.TryGetValue(outputId, out Target target) ? target : null;
        }

        void OnError(OutputError error)
        {
            if (!Settings.Enabled)
                return;
            lock (m_Seen)
            {
                if (m_Seen.TryGetValue(error, out _))
                    return;
                m_Seen.Add(error, null);
            }
            RawMessage raw = GxMessages.ToRaw(error, out IKBObjectPosition position);
            // Sin objeto no hay dónde marcarlo
            if (raw == null || position?.Object == null)
                return;
            // Los que escribe esta extensión (los objetos que llaman a un procedimiento que cambió)
            if (raw.Code.StartsWith("gx"))
                return;
            if (raw.Code.StartsWith("spc") && !Settings.ShowSpecifier)
                return;
            Guid part = position.Part;
            string where = PositionText(position);
            Match partName = PartInPosition.Match(where);
            Post(() =>
            {
                string name = ObjectName(position.Object);
                Log.Write($"Output: {raw} | {name} | parte {part} | {where}");
                if (name != null)
                    SpecStore.Add(name, raw.Line > 0 ? part : Guid.Empty, partName.Success ? partName.Groups["part"].Value : null, raw);
            });
        }

        void OnLine(Target target, string line)
        {
            if (string.IsNullOrEmpty(line))
                return;
            Match specifying = SpecifyingLine.Match(line);
            if (specifying.Success)
            {
                string name = specifying.Groups["name"].Value;
                target.Specifying = name;
                Post(() =>
                {
                    Log.Write($"Empieza la especificación de {name} ({SpecStore.SinceSave(name)})");
                    SpecStore.SpecificationStarted(name);
                });
                return;
            }
            Match end = SpecificationEnd.Match(line);
            if (end.Success && target.Specifying != null)
            {
                string name = target.Specifying;
                target.Specifying = null;
                Post(() =>
                {
                    if (!SpecStore.IsCurrentSpecification(name))
                        return;
                    SpecStore.SpecificationFinished(name);
                    int errors = SpecStore.ErrorCount(name);
                    Log.Write($"Termina la especificación de {name}: {errors} error(es) ({SpecStore.SinceSave(name)})");
                    CodeCheckPackage.Status(errors == 0
                        ? $"GxCodeCheck: {name} especificado sin errores"
                        : $"GxCodeCheck: {name} tiene {errors} error(es) de especificación, subrayados en el código");
                });
            }
        }

        static string PositionText(IKBObjectPosition position)
        {
            try
            {
                return position.ToString() ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        static string ObjectName(Artech.Udm.Framework.EntityKey key)
        {
            KBModel model = UIServices.KB.CurrentModel;
            if (model == null)
                return null;
            KBObject obj = KBObject.Get(model, key);
            return obj?.QualifiedName.ToString();
        }

        void Post(Action action)
        {
            if (m_Ui.IsDisposed)
                return;
            try
            {
                m_Ui.BeginInvoke((MethodInvoker)(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Error al guardar un mensaje del Output: " + ex);
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                // GeneXus se está cerrando
            }
        }

        public void Dispose()
        {
            try
            {
                CommonServices.Output.RemoveListener(this);
            }
            catch (Exception)
            {
                // GeneXus se está cerrando
            }
            m_Ui.Dispose();
        }

        /// <summary>Una ventana del Output: solo interesan los errores y las líneas "Specifying ...".</summary>
        sealed class Target : IOutputTarget
        {
            readonly OutputWatcher m_Owner;

            /// <summary>El objeto que se está especificando en esta ventana del Output.</summary>
            public string Specifying;

            public Target(OutputWatcher owner)
            {
                m_Owner = owner;
            }

            public void Add(OutputError value) => Safe(() => m_Owner.OnError(value));
            public void AddAll(IOutputMessages output, bool textOnly)
            {
                if (!textOnly && output is IEnumerable<BaseMessage> messages)
                    foreach (OutputError error in messages.OfType<OutputError>())
                        Add(error);
            }
            public void AddText(string value) => Safe(() => m_Owner.OnLine(this, value));
            public void AddLine(string value) => Safe(() => m_Owner.OnLine(this, value));
            public void AddWarningLine(string value) { }
            public void AddErrorLine(string value) { }
            public void StartSection(string sectionName) { }
            public void StartSection(string sectionId, string sectionName) { }
            public void StartSection(string sectionName, bool reuseSection) { }
            public void StartSection(string sectionId, string sectionName, bool reuseSection) { }
            public void EndSection(string sectionName, bool success) { }
            public void EndSection(string sectionId, string sectionName, bool success) { }
            public void Clear() { }
            public void Show() { }

            // Nada de lo que pase acá puede romper el Output de GeneXus
            static void Safe(Action action)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Log.Write("Error al leer el Output: " + ex);
                }
            }
        }
    }
}
