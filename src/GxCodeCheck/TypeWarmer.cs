using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common;
using Artech.Genexus.Common.Parts;

namespace GxCodeCheck
{
    /// <summary>
    /// Al abrir un objeto, lee de antemano lo que el chequeo de tipos va a necesitar de la KB (los SDT de sus
    /// variables, el parm() de lo que llama), para que el primer guardado no pague esas lecturas. Va de a una
    /// consulta por vez y solo cuando no se está usando el teclado ni el mouse: la primera lectura de un SDT
    /// puede tardar, y así nunca cae mientras se escribe.
    /// </summary>
    sealed class TypeWarmer : IDisposable
    {
        const int FirstDelay = 800;
        const int Interval = 40;
        // Sin teclado ni mouse desde hace este tiempo
        const int IdleTime = 500;

        readonly Timer m_Timer = new Timer { Interval = FirstDelay };
        readonly Queue<Action> m_Work = new Queue<Action>();
        readonly Stopwatch m_Time = new Stopwatch();
        readonly string m_Name;
        int m_Done;

        public TypeWarmer(KBObject obj, string text)
        {
            m_Name = obj.QualifiedName.ToString();
            // Al abrir el editor no se hace nada pesado: la lista de lo que hay que leer es el primer paso
            m_Work.Enqueue(() => Plan(obj, text));
            m_Timer.Tick += OnTick;
            m_Timer.Start();
        }

        void Plan(KBObject obj, string text)
        {
            var context = new KbTypeContext(obj);
            VariablesPart variables = obj.Parts.Get<VariablesPart>();
            if (variables != null)
            {
                foreach (Variable variable in variables.Variables.Where(v => !v.IsStandard && IsStructure(v.Type)).ToList())
                {
                    string name = variable.Name;
                    m_Work.Enqueue(() => context.Variable(name));
                }
            }
            foreach (string callee in TypeChecker.CallNames(new SourceText(text)))
                m_Work.Enqueue(() => context.Procedure(callee));
        }

        static bool IsStructure(eDBType type) =>
            type == eDBType.GX_SDT || type == eDBType.GX_BUSCOMP || type == eDBType.GX_BUSCOMP_LEVEL;

        void OnTick(object sender, EventArgs e)
        {
            m_Timer.Interval = Interval;
            if (Idle.MillisecondsSinceInput() < IdleTime)
                return;
            if (m_Work.Count == 0)
            {
                m_Timer.Stop();
                Log.Write($"Chequeo de tipos de {m_Name} preparado: {m_Done} lectura(s) en {m_Time.ElapsedMilliseconds} ms");
                return;
            }
            Action work = m_Work.Dequeue();
            m_Time.Start();
            try
            {
                work();
                m_Done++;
            }
            catch (Exception ex)
            {
                Log.Write($"No se pudo preparar el chequeo de tipos de {m_Name}: {ex.Message}");
            }
            finally
            {
                m_Time.Stop();
            }
        }

        public void Dispose()
        {
            m_Timer.Stop();
            m_Timer.Dispose();
            m_Work.Clear();
        }
    }
}
