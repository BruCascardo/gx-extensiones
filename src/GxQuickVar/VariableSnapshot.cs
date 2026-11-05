using System;
using System.Collections.Generic;
using System.Linq;
using Artech.Common.Properties;
using Artech.Genexus.Common;

namespace GxQuickVar
{
    /// <summary>
    /// Las propiedades de una variable en un momento dado, para volver a dejarla así: deshacer y rehacer en
    /// la grilla de variables, o si asignar el tipo falla a mitad de camino. Se guardan como texto, igual que
    /// en la KB (SerializedProperties): así no se comparten objetos con la variable (el tipo, AttCustomType,
    /// se modifica en el lugar) y quedan afuera los valores calculados, que no se guardan.
    /// </summary>
    sealed class VariableSnapshot
    {
        /// <summary>Una propiedad puede no aceptar su valor hasta que se restaure otra, u otra pisarla: se repite.</summary>
        const int MaxPasses = 3;

        /// <summary>Lo que identifica a la variable o fija su constructor: nunca se restaura.</summary>
        static readonly HashSet<string> Fixed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Name", "IsStandardVariable", "GeneXusObject", "OBJ_TYPE", "Model",
        };

        /// <summary>Las propiedades que no tienen el valor por defecto, con su valor como se guarda en la KB.</summary>
        readonly Dictionary<string, string> m_Values;

        VariableSnapshot(Dictionary<string, string> values)
        {
            m_Values = values;
        }

        public static VariableSnapshot Take(Variable variable) => new VariableSnapshot(Values(variable));

        /// <summary>
        /// Deja las propiedades de <paramref name="variable"/> como estaban al tomar la copia. Si alguna no se
        /// puede (no debería pasar), lo informa con una excepción que dice cuáles.
        /// </summary>
        public void RestoreInto(Variable variable)
        {
            Exception failure = null;
            for (int pass = 1; ; pass++)
            {
                List<string> pending = Differences(Values(variable));
                if (pending.Count == 0)
                {
                    ForgetStaleDomain(variable);
                    return;
                }
                if (pass > MaxPasses)
                    throw new InvalidOperationException(
                        $"No se pudo dejar &{variable.Name} como estaba (propiedades: {string.Join(", ", pending)})", failure);

                // Basarla en un atributo, por ejemplo, le copia la descripción: la pasada siguiente la corrige
                foreach (string name in pending)
                {
                    try
                    {
                        if (m_Values.TryGetValue(name, out string value))
                            variable.SetPropertyValue(name, variable.ConvertPropertyValueFromString<StringStorage>(name, value));
                        else
                            variable.ResetProperty(name);
                    }
                    catch (Exception ex)
                    {
                        failure = failure ?? ex;
                    }
                }
            }
        }

        List<string> Differences(Dictionary<string, string> current)
        {
            return m_Values.Keys.Union(current.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(name => !Fixed.Contains(name))
                .Where(name => !(m_Values.TryGetValue(name, out string saved)
                    && current.TryGetValue(name, out string now)
                    && string.Equals(saved, now, StringComparison.Ordinal)))
                .ToList();
        }

        /// <summary>
        /// La variable guarda aparte el dominio en el que se basa ("Domain", que no es una propiedad que se
        /// guarde) y DomainBasedOn lo sigue devolviendo aunque ya no esté basada en él: se olvida.
        /// </summary>
        internal static void ForgetStaleDomain(Variable variable)
        {
            if (variable.DomainKey == null && variable.GetPropertyValue("Domain") != null)
                variable.SilentSetPropertyValue("Domain", null);
        }

        static Dictionary<string, string> Values(Variable variable)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (StringProperty property in variable.SerializedProperties())
            {
                // Algunas se guardan con otro nombre (PropertyBind): se usa el de la definición, como al leerlas
                string name = variable.GetPropertyDefinition(property.Name)?.Name ?? property.Name;
                values[name] = property.Value;
            }
            return values;
        }
    }
}
