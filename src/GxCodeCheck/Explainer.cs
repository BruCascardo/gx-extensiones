using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GxCodeCheck
{
    /// <summary>
    /// Traduce los mensajes de GeneXus (src: parser, spc: especificador) a una explicación de qué pasa y qué
    /// revisar. Los que no conoce quedan con el texto original.
    /// </summary>
    static class Explainer
    {
        // GeneXus duplica las comillas de los nombres que ya las tienen: ''LeerCupon''
        static readonly Regex QuotedArg = new Regex("'+(?<arg>[^']*?)'+");

        // Funciones y métodos más usados, para sugerir el nombre bien escrito
        static readonly string[] KnownFunctions =
        {
            "Trim", "LTrim", "RTrim", "Upper", "Lower", "Len", "Length", "Substr", "Substring", "Str", "Val", "ToString",
            "ToNumeric", "Format", "Replace", "StrReplace", "Concat", "PadL", "PadR", "Space", "Round", "Int", "Abs",
            "Mod", "Today", "Now", "ServerDate", "ServerNow", "Year", "Month", "Day", "Hour", "Minute", "Second",
            "YMDtoD", "YMDHMStoT", "CtoD", "DtoC", "CtoT", "TtoC", "AddDays", "AddMonths", "AddYears", "AddSeconds",
            "Null", "IsNull", "IsEmpty", "SetEmpty", "IsNullOrEmpty", "Add", "AddRange", "Remove", "RemoveItem", "Clear",
            "Count", "Item", "Sort", "Clone", "IndexOf", "Contains", "StartsWith", "EndsWith", "FromJson", "ToJson",
            "FromXml", "ToXml", "Load", "Save", "Insert", "Update", "Delete", "Check", "Success", "Fail",
            "GetMessages", "Fromstring", "CharIndex", "Asc", "Chr", "NewLine", "Random", "Dow", "CDow", "CMonth",
            "Age", "EoM", "TAdd", "TDiff", "DAdd", "DDiff", "Call", "Udp", "Submit", "Link", "Msg", "Iif", "Nullvalue",
            "RGB", "ToFormattedString", "SetContentType", "Matches", "IsMatch", "ReplaceRegEx", "SplitRegEx",
        };

        /// <summary>La explicación del mensaje, o null si no hay una mejor que el original.</summary>
        public static string Explain(string code, string text)
        {
            string a = Arg(text, 0);
            Match m;
            switch (code)
            {
                // ---- Parser (src)
                case "src0216":
                    return $"'{a}' no es una propiedad ni un método de lo que está antes del punto. Revisá cómo está escrito y de qué tipo es la variable.";
                case "src0054":
                    return a == "{0}" || a.Length == 0
                        ? "Hay algo que sobra o que falta antes de esta posición."
                        : $"'{a}' no se esperaba acá: puede que sobre, o que falte algo antes (una coma, un operador, un paréntesis).";
                case "src0051":
                    return "Hay algo que sobra o que falta en esta parte de la línea.";
                case "src0052":
                    return IsBlockWord(a)
                        ? $"'{a}' sobra o está fuera de lugar: no hay un bloque abierto que cierre (puede que otro End lo haya cerrado antes)."
                        : $"El comando '{a}' no va en este lugar.";
                case "src0294":
                    return $"No existe la función '{a}'." + SuggestionText(a);
                case "src0299":
                    m = Regex.Match(text, @"at least (?<n>\d+)");
                    return m.Success ? $"'{a}' necesita al menos {m.Groups["n"].Value} parámetro(s)." : null;
                case "src0301":
                    m = Regex.Match(text, @"supports (?<n>\d+)");
                    return m.Success ? $"'{a}' recibe {m.Groups["n"].Value} parámetro(s)." : null;
                case "src0300":
                    return $"A '{a}' le pasaste todos los parámetros: el último lo devuelve, así que se usa como '&Resultado = {a}(...)'.";
                case "src0265":
                case "src0210":
                    return $"'{a}' no es un atributo, una variable ni un objeto conocido. Si es una variable, le falta el '&'.";
                case "src0256":
                case "src0257":
                case "src0258":
                    return "Se esperaba una asignación, por ejemplo '&Variable = valor'.";
                case "src0057": return MissingEnd("If", "EndIf");
                case "src0058": return MissingEnd("Do While", "EndDo");
                case "src0059": return MissingEnd("For", "EndFor");
                case "src0060": return MissingEnd("Do Case", "EndCase");
                case "src0062": return MissingEnd("Sub", "EndSub");
                case "src0063": return MissingEnd("XNew", "XEndNew");
                case "src0064": return MissingEnd("New", "EndNew");
                case "src0255": return MissingEnd("Event", "EndEvent");
                case "src0080": return "Falta el 'End' que cierra este bloque.";
                case "src0061": return "Después de 'Do Case' tiene que venir un 'Case'.";
                case "src0102":
                    return "Falta una coma entre los parámetros, o el ')' que cierra la llamada.";
                case "src0085":
                    return "Después de 'To valor' hay algo de más. ¿Faltó la palabra 'Step'?";
                case "src0119":
                    return "Sobra el ';': en GeneXus no se usa para separar instrucciones.";
                case "src0053":
                    return a == "\"" || a == "'" ? "Faltan las comillas de cierre del texto." : $"'{a}' es un carácter que no va en el código.";
                case "src0287":
                    return $"No existe el programa '{a}', o no se puede usar desde acá (¿está en otro módulo y no es público?).";
                case "src0246":
                    return $"Se llama a '{a}', que no existe en la KB.";
                case "src0231":
                case "src0304":
                case "src0305":
                    return $"La subrutina {a} no está definida: revisá el nombre en el 'Sub'.";
                case "src0302":
                case "src0303":
                    return $"La subrutina {a} está definida dos veces.";
                case "src0306":
                case "src0307":
                    return $"La subrutina {a} está definida pero nadie la llama.";
                case "src0089":
                    return "Después de las subrutinas no puede haber código suelto: subí esta línea antes del primer 'Sub', o metela dentro de uno.";
                case "src0086":
                    return "Este código tiene que estar dentro de un 'Event' o de un 'Sub'.";
                case "src0069":
                    return "Después del punto falta la propiedad o el método.";
                case "src0072":
                case "src0079":
                    return "Falta el valor después del '='.";
                case "src0084":
                    return $"Lo que sigue a la variable {a} no se entiende: ¿falta el '=' o sobra algo?";
                case "src0105":
                    return $"Lo que sigue a '{a}' no es válido.";
                case "src0114":
                    return "Una variable sola no es una instrucción: falta asignarle un valor ('&Variable = ...').";
                case "src0233":
                case "src0241":
                    return $"'{a}' no es un evento válido.";
                case "src0208":
                    return $"El evento '{a}' ya está definido más arriba.";
                case "src0207":
                case "src0201":
                    return $"Un parámetro no es del tipo que espera '{a}'.";
                case "src0244":
                    return $"El array '{a}' no está definido.";
                case "src0234":
                    return $"'{a}' no es un array: no lleva índice entre paréntesis.";
                case "src0254":
                    return "'Exit' solo se puede usar dentro de un 'For' o un 'Do While'.";
                case "src0262":
                    return $"No existe la imagen '{a}'.";
                case "src0212":
                    return $"'{a}' no es un control del formulario.";
                case "src0211":
                    return $"No existe la clase '{a}'.";
                case "src0110":
                case "src0112":
                case "src0078":
                    return "Falta una llave '{' o '}'.";
                case "src0113":
                    return "Falta el ']' que cierra el grupo de propiedades.";
                case "src0108":
                    return "Falta el '*/' que cierra el comentario.";
                case "src0109":
                    return "Hay un '*/' sin el '/*' que abre el comentario.";

                // ---- Especificador (spc)
                case "spc0010":
                    m = Regex.Match(text, @"Type mismatch in assignment: (?<expr>.+?)\s*\((?<left>[^()=]+)=(?<right>[^()]+)\)\.?\s*$");
                    return m.Success
                        ? $"Los tipos no coinciden en '{m.Groups["expr"].Value.Trim()}': a la izquierda es {m.Groups["left"].Value.Trim()} y el valor es {m.Groups["right"].Value.Trim()}."
                        : "Los tipos no coinciden en esta asignación.";
                case "spc0023":
                    m = Regex.Match(text, @"Parameter (?<p>.+?) \((?<t>[^)]*)\) calling (?<obj>\S+?) has wrong type \(expecting (?<exp>.+)\)");
                    return m.Success
                        ? $"El parámetro {m.Groups["p"].Value} es {m.Groups["t"].Value}, pero {m.Groups["obj"].Value} espera {m.Groups["exp"].Value}."
                        : "Un parámetro no es del tipo que espera el objeto llamado.";
                case "spc0024":
                    m = Regex.Match(text, @"Not enough parameters (calling|linking to|linking|creating) (?<obj>\S+?)\.?\s*$");
                    return $"Faltan parámetros: revisá el parm() de {(m.Success ? m.Groups["obj"].Value : "el objeto llamado")}.";
                case "spc0025":
                    m = Regex.Match(text, @"Too many parameters (calling|linking to|linking|creating) (?<obj>\S+?)\.?\s*$");
                    return $"Sobran parámetros: {(m.Success ? m.Groups["obj"].Value : "el objeto llamado")} recibe menos (revisá su parm()).";
                case "spc0022":
                    m = Regex.Match(text, @"Input-only parameter (?<p>\S+) should not be assigned");
                    return m.Success
                        ? $"{m.Groups["p"].Value} es un parámetro de entrada (in:) y no se debería asignar. Si tiene que devolver un valor, declaralo inout: u out:."
                        : null;
                case "spc0058":
                    return $"'{a}' tiene que ser una colección (o un array) para usarlo así. ¿A la variable le falta marcar Collection?";
                case "spc0047":
                    m = Regex.Match(text, @"Variable (?<v>\S+) not defined; (?<t>.+?) assumed");
                    return m.Success
                        ? $"La variable &{m.Groups["v"].Value} no está definida y GeneXus la toma como {m.Groups["t"].Value}. ¿Está mal escrito el nombre?"
                        : null;
                case "spc0046":
                    m = Regex.Match(text, @"Variable (?<v>\S+) not defined\. Same as (?<a>\S+?) assumed");
                    return m.Success
                        ? $"La variable &{m.Groups["v"].Value} no está definida y GeneXus la toma igual que el atributo {m.Groups["a"].Value}."
                        : null;
                case "spc0038":
                    m = Regex.Match(text, @"no index for order (?<o>.+?);");
                    return m.Success ? $"No hay un índice para ordenar por {m.Groups["o"].Value}: la consulta puede andar lenta." : null;
                case "spc0187":
                    return $"Llamar a '{a}' así no tiene efecto: el resultado no se usa.";
                case "spc0156":
                    return $"El evento '{a}' no está definido para ese control.";
            }
            return null;
        }

        /// <summary>Variable que nombra un mensaje de variable no definida (spc0046, spc0047), sin el &amp;.</summary>
        public static string UndefinedVariable(string code, string text)
        {
            if (code != "spc0046" && code != "spc0047")
                return null;
            var m = Regex.Match(text, @"Variable (?<v>\w+) not defined");
            return m.Success ? m.Groups["v"].Value : null;
        }

        /// <summary>
        /// Qué parte de la línea marcar en un error de la especificación, que solo trae la línea: el parámetro,
        /// la llamada o la colección de la que habla, y si no se encuentra la instrucción entera.
        /// </summary>
        public static (int Column, int Length) SpecTarget(string code, string text, SourceText source, int line)
        {
            var extent = source.CodeExtent(line);
            if (extent == null)
                return (0, Math.Max(1, source.Line(line).Length));
            int end = extent.Value.End + 1;

            string word = null;
            bool toEnd = false;
            Match m;
            switch (code)
            {
                case "spc0022":
                    m = Regex.Match(text, @"parameter (?<p>\S+)");
                    word = m.Success ? m.Groups["p"].Value : null;
                    break;
                case "spc0023":
                case "spc0024":
                case "spc0025":
                    m = Regex.Match(text, @"(calling|linking to|linking|creating) (?<obj>[\w.]+?)(?= has |\.?\s*$)");
                    if (m.Success)
                    {
                        string name = m.Groups["obj"].Value;
                        word = name.Substring(name.LastIndexOf('.') + 1);
                        toEnd = true;
                    }
                    break;
                case "spc0058":
                    word = Arg(text, 0);
                    break;
            }
            if (!string.IsNullOrEmpty(word))
            {
                int at = source.FindWord(line, word);
                if (at >= 0)
                    return (at, toEnd ? end - at : word.Length);
            }
            return (extent.Value.Start, end - extent.Value.Start);
        }

        /// <summary>El nombre conocido más parecido (hasta dos letras de diferencia), o null.</summary>
        public static string Suggest(string word)
        {
            if (string.IsNullOrEmpty(word) || word.Length < 3)
                return null;
            int limit = word.Length <= 4 ? 1 : 2;
            string best = null;
            int bestDistance = int.MaxValue;
            foreach (string known in KnownFunctions.Distinct())
            {
                if (string.Equals(known, word, StringComparison.OrdinalIgnoreCase))
                    return null;
                int distance = Distance(word.ToLowerInvariant(), known.ToLowerInvariant());
                if (distance <= limit && distance < bestDistance)
                {
                    best = known;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>El nombre de <paramref name="candidates"/> más parecido a <paramref name="word"/> (un error de tipeo), o null si es uno de ellos o no hay ninguno cerca.</summary>
        public static string Closest(string word, IEnumerable<string> candidates)
        {
            if (string.IsNullOrEmpty(word) || word.Length < 3 || candidates == null)
                return null;
            int limit = word.Length <= 4 ? 1 : 2;
            string best = null;
            int bestDistance = int.MaxValue;
            foreach (string known in candidates.Distinct())
            {
                if (string.Equals(known, word, StringComparison.OrdinalIgnoreCase))
                    return null;
                int distance = Distance(word.ToLowerInvariant(), known.ToLowerInvariant());
                if (distance <= limit && distance < bestDistance)
                {
                    best = known;
                    bestDistance = distance;
                }
            }
            return best;
        }

        static string SuggestionText(string word)
        {
            string suggestion = Suggest(word);
            return suggestion == null ? "" : $" ¿Quisiste decir '{suggestion}'?";
        }

        static string MissingEnd(string opener, string closer) =>
            $"Este '{opener}' no tiene su '{closer}', o hay otro End antes que lo cierra (por ejemplo un EndFor dentro del If).";

        static bool IsBlockWord(string word)
        {
            string lower = word.ToLowerInvariant();
            return lower.StartsWith("end") || lower == "else" || lower == "otherwise" || lower == "case" || lower == "when";
        }

        /// <summary>El argumento entre comillas simples número <paramref name="index"/> del mensaje, o "".</summary>
        public static string Arg(string text, int index)
        {
            var matches = QuotedArg.Matches(text ?? "");
            return index < matches.Count ? matches[index].Groups["arg"].Value : "";
        }

        /// <summary>Distancia de edición (inserciones, borrados, cambios y letras cambiadas de lugar).</summary>
        static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
            return d[a.Length, b.Length];
        }
    }
}
