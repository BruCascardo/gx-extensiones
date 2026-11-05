using System;
using System.Collections.Generic;
using System.Linq;
using Artech.Architecture.Common.Objects;

namespace GxCallParms
{
    /// <summary>Lo predefinido para un parámetro de un objeto.</summary>
    sealed class ParameterPreset
    {
        /// <summary>Se pregunta igual, con <see cref="Values"/> arriba (el primero es el propuesto).</summary>
        public bool Ask;
        /// <summary>Las opciones (si se pregunta) o el valor fijo, que puede ser "" (queda vacío para escribirlo).</summary>
        public List<string> Values = new List<string>();
    }

    /// <summary>
    /// Valores predefinidos por objeto, una línea por objeto con un lugar por parámetro separado por ";":
    ///   prcLog: ?LogTipo.Debug | LogTipo.Error; &amp;Pgmname;
    /// "?a | b" pregunta proponiendo a (y b como otra opción); un valor sin "?" va fijo sin preguntar; vacío
    /// deja el lugar vacío con el cursor ahí; "*" (o faltar) es lo normal para ese parámetro.
    /// </summary>
    static class Presets
    {
        public const char Separator = ';';
        public const char AskMark = '?';
        public const char AlternativeSeparator = '|';
        public const string Normal = "*";

        public static readonly string[] Defaults = { "prcLog: ?LogTipo.Debug | LogTipo.Error; &Pgmname;" };

        /// <summary>Lo predefinido para cada parámetro del objeto (null = lo normal), o null si no tiene.</summary>
        public static List<ParameterPreset> For(KBObject target)
        {
            string qualified = target.QualifiedName.ToString();
            foreach (string line in Settings.Presets)
            {
                if (TryParse(line, out string name, out List<ParameterPreset> parameters, out _)
                    && (string.Equals(name, target.Name, StringComparison.OrdinalIgnoreCase) || string.Equals(name, qualified, StringComparison.OrdinalIgnoreCase)))
                    return parameters;
            }
            return null;
        }

        /// <summary>"Objeto: lugar; lugar; ..." Devuelve false (con el porqué) si la línea no se entiende.</summary>
        public static bool TryParse(string line, out string objectName, out List<ParameterPreset> parameters, out string error)
        {
            objectName = null;
            parameters = null;
            error = null;
            int colon = (line ?? string.Empty).IndexOf(':');
            if (colon <= 0)
            {
                error = "falta \"Objeto:\" al principio";
                return false;
            }
            objectName = line.Substring(0, colon).Trim();
            if (objectName.Length == 0 || objectName.Any(char.IsWhiteSpace))
            {
                error = "el nombre del objeto no puede tener espacios";
                return false;
            }

            parameters = new List<ParameterPreset>();
            string[] slots = line.Substring(colon + 1).Split(Separator);
            // "a; b;" termina en un lugar vacío a propósito; "a; b" no tiene tercero
            for (int i = 0; i < slots.Length; i++)
            {
                string slot = slots[i].Trim();
                if (slot == Normal)
                {
                    parameters.Add(null);
                }
                else if (slot.Length > 0 && slot[0] == AskMark)
                {
                    var alternatives = slot.Substring(1).Split(AlternativeSeparator).Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
                    parameters.Add(new ParameterPreset { Ask = true, Values = alternatives });
                }
                else
                {
                    parameters.Add(new ParameterPreset { Values = { slot } });
                }
            }
            return true;
        }
    }
}
