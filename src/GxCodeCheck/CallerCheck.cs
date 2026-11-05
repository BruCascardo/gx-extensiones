using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Artech.Architecture.Common.Location;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Artech.Common.Diagnostics;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using Artech.Udm.Framework;
using Artech.Udm.Framework.References;

namespace GxCodeCheck
{
    /// <summary>
    /// Cuando se guarda un procedimiento con el parm() cambiado (cantidad, tipos o in:/out:), revisa los objetos
    /// que lo llaman con el chequeo de tipos y avisa cuáles quedaron mal, sin esperar al build: en el Output
    /// (con doble clic se abre el objeto en la línea), en la barra de estado y subrayado en sus editores.
    /// </summary>
    /// <remarks>
    /// Revisa de a un objeto por vez y solo cuando no se usa el teclado ni el mouse (leer un objeto de la KB
    /// puede tardar). Del resultado de cada uno quedan solo los errores de las líneas que nombran al
    /// procedimiento: lo demás del objeto no cambió con este guardado.
    /// </remarks>
    static class CallerCheck
    {
        const int Interval = 40;
        const int IdleTime = 300;

        // Los errores de cada procedimiento que se revisa en los objetos que lo llaman
        static readonly HashSet<string> CallCodes = new HashSet<string>
        {
            TypeChecker.TooMany, TypeChecker.TooFew, TypeChecker.WrongType, TypeChecker.NoReturn,
            TypeChecker.AssignMismatch, TypeChecker.CompareMismatch,
        };

        // El parm() de cada procedimiento como estaba al abrirlo o al guardarlo por última vez
        static readonly Dictionary<string, string> s_Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Los errores encontrados en cada objeto que llama: por objeto (nombre calificado) y tipo de parte
        static readonly Dictionary<string, List<(Guid Part, string Callee, Diagnostic Diagnostic)>> s_Found =
            new Dictionary<string, List<(Guid, string, Diagnostic)>>(StringComparer.OrdinalIgnoreCase);

        static Run s_Running;

        /// <summary>Cambiaron los errores de los objetos que llaman (nombre calificado del objeto).</summary>
        public static event Action<string> Changed;

        /// <summary>El parm() del procedimiento como está ahora, para saber si cambió al guardarlo.</summary>
        public static void Remember(KBObject obj)
        {
            if (!(obj is Procedure))
                return;
            string name = obj.QualifiedName.ToString();
            if (!s_Known.ContainsKey(name))
                s_Known[name] = Fingerprint(new KbTypeContext(obj).Own());
        }

        /// <summary>Se guardó el objeto: si es un procedimiento y cambió su parm(), revisa a los que lo llaman.</summary>
        public static void Saved(KBObject obj)
        {
            string name = obj.QualifiedName.ToString();
            // Lo que se había encontrado en él ya no vale: lo vuelve a revisar el editor
            Forget(name);
            if (!(obj is Procedure) || !Settings.CheckCallers)
                return;
            string now = Fingerprint(new KbTypeContext(obj).Own());
            bool changed = s_Known.TryGetValue(name, out string before) && before != now;
            s_Known[name] = now;
            if (!changed)
                return;
            Log.Write($"Cambió el parm() de {name}: se revisan los objetos que lo llaman");
            s_Running?.Stop();
            s_Running = new Run((Procedure)obj);
        }

        /// <summary>Los errores encontrados en el objeto por haber cambiado un procedimiento que llama, para esa parte.</summary>
        public static IEnumerable<Diagnostic> For(string objectName, Guid part) =>
            s_Found.TryGetValue(objectName, out var found) ? found.Where(f => f.Part == part).Select(f => f.Diagnostic) : Enumerable.Empty<Diagnostic>();

        static void Forget(string objectName)
        {
            if (s_Found.Remove(objectName))
                Changed?.Invoke(objectName);
        }

        public static void Clear()
        {
            s_Running?.Stop();
            s_Running = null;
            s_Known.Clear();
            var names = s_Found.Keys.ToList();
            s_Found.Clear();
            foreach (string name in names)
                Changed?.Invoke(name);
        }

        /// <summary>Lo que importa del parm() a quien llama: cantidad, tipos e in:/out: (no los nombres).</summary>
        static string Fingerprint(CallSignature signature) =>
            signature == null ? "" : string.Join(";", signature.Parameters.Select(p => $"{p.Direction}|{p.Type}"));

        /// <summary>Los objetos que usan al procedimiento, según las referencias de la KB.</summary>
        public static List<EntityKey> Callers(Procedure callee) =>
            callee.Model.GetReferencesTo(callee.Key, LinkType.UsedObject).Select(r => r.From).Distinct()
                .Where(from => !from.Equals(callee.Key)).ToList();

        /// <summary>
        /// Los errores del chequeo de tipos en las llamadas al procedimiento que hace el objeto: en su Source o
        /// sus Events, solo en las líneas que nombran al procedimiento (con o sin módulo).
        /// </summary>
        public static List<(KBObjectPart Part, Diagnostic Diagnostic)> CheckCaller(KBObject caller, Procedure callee)
        {
            var found = new List<(KBObjectPart, Diagnostic)>();
            foreach (var (part, d) in KbCheck.Check(caller, keep: _ => false))
            {
                var source = new SourceText(((ISource)part).Source);
                if (!CallCodes.Contains(d.Code) || source.FindWord(d.Line, callee.Name) < 0)
                    continue;
                d.Note = $"Al cambiar el parm() de {callee.Name} (chequeo rápido: la especificación de este objeto lo confirma en el próximo build).";
                found.Add((part, d));
            }
            return found;
        }

        /// <summary>Una revisión de los objetos que llaman a un procedimiento.</summary>
        sealed class Run
        {
            readonly Procedure m_Callee;
            readonly string m_CalleeName;
            readonly Timer m_Timer = new Timer { Interval = Interval };
            readonly Queue<EntityKey> m_Pending = new Queue<EntityKey>();
            readonly Stopwatch m_Time = new Stopwatch();
            bool m_Listed;
            int m_Checked;
            int m_WithErrors;
            int m_Errors;

            public Run(Procedure callee)
            {
                m_Callee = callee;
                m_CalleeName = callee.QualifiedName.ToString();
                m_Timer.Tick += OnTick;
                m_Timer.Start();
            }

            public void Stop()
            {
                m_Timer.Stop();
                m_Timer.Dispose();
            }

            void OnTick(object sender, EventArgs e)
            {
                if (Idle.MillisecondsSinceInput() < IdleTime)
                    return;
                m_Time.Start();
                try
                {
                    if (!m_Listed)
                        List();
                    else if (m_Pending.Count > 0)
                        Check(m_Pending.Dequeue());
                    else
                        Finish();
                }
                catch (Exception ex)
                {
                    Log.Write($"Error al revisar los objetos que llaman a {m_CalleeName}: {ex}");
                    Stop();
                }
                finally
                {
                    m_Time.Stop();
                }
            }

            void List()
            {
                m_Listed = true;
                foreach (EntityKey from in Callers(m_Callee))
                    m_Pending.Enqueue(from);
                // Lo que se había encontrado por este procedimiento antes ya no vale
                foreach (string name in s_Found.Keys.ToList())
                {
                    s_Found[name].RemoveAll(f => f.Callee == m_CalleeName);
                    if (s_Found[name].Count == 0)
                        s_Found.Remove(name);
                    Changed?.Invoke(name);
                }
                if (m_Pending.Count == 0)
                {
                    Stop();
                    return;
                }
                CodeCheckPackage.Status($"GxCodeCheck: cambió el parm() de {m_Callee.Name}, revisando los {m_Pending.Count} objeto(s) que lo usan...");
            }

            void Check(EntityKey key)
            {
                KBObject caller = KBObject.Get(m_Callee.Model, key);
                // Con Generate Object en False el build no lo genera: no puede romperlo
                if (caller == null || KbCheck.IsNotGenerated(caller))
                    return;
                m_Checked++;
                var found = new List<(Guid, string, Diagnostic)>();
                foreach (var (part, d) in CheckCaller(caller, m_Callee))
                {
                    found.Add((part.Type, m_CalleeName, d));
                    Report(caller, part, d);
                }
                if (found.Count == 0)
                    return;
                m_WithErrors++;
                m_Errors += found.Count;
                string name = caller.QualifiedName.ToString();
                if (!s_Found.TryGetValue(name, out var list))
                    s_Found[name] = list = new List<(Guid, string, Diagnostic)>();
                list.AddRange(found);
                Changed?.Invoke(name);
            }

            /// <summary>En el Output de GeneXus, con la posición: doble clic abre el objeto en esa línea.</summary>
            void Report(KBObject caller, KBObjectPart part, Diagnostic d)
            {
                if (!m_Reported)
                {
                    m_Reported = true;
                    CommonServices.Output.AddLine($"GxCodeCheck: cambió el parm() de {m_CalleeName}. Objetos que lo usan y quedaron mal:");
                }
                var position = new SourcePosition(part, d.Line + 1, d.Column + 1);
                CommonServices.Output.Add(new OutputError(d.Code, $"{caller.QualifiedName}: {d.Message}", MessageLevel.Error, position));
            }

            bool m_Reported;

            void Finish()
            {
                Stop();
                if (s_Running == this)
                    s_Running = null;
                Log.Write($"Objetos que llaman a {m_CalleeName}: {m_Checked} revisado(s) en {m_Time.ElapsedMilliseconds} ms, {m_WithErrors} con errores ({m_Errors})");
                CodeCheckPackage.Status(m_WithErrors == 0
                    ? $"GxCodeCheck: los {m_Checked} objeto(s) que usan {m_Callee.Name} siguen bien con el parm() nuevo"
                    : $"GxCodeCheck: {m_WithErrors} de los {m_Checked} objeto(s) que usan {m_Callee.Name} quedaron mal con el parm() nuevo (ver el Output)");
            }
        }
    }
}
