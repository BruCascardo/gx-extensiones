using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GxQuickVar
{
    /// <summary>
    /// Lo que se escribió en el desplegable (sin parámetros ni sufijo de colección), separado en términos:
    ///   palabra          nombre (letras en orden: "sdtcli" → sdtCliente); también vale el módulo o la categoría
    ///   sdt ventas       si una palabra es parte del nombre de un módulo, primero los tipos de ese módulo
    ///   sdt inter.reg    el módulo también puede ir calificado (abreviado o entero: Interfases.Registros)
    ///   ven,com          cualquiera de las dos (coma pegada; "sdt, ventas" separa igual que el espacio)
    ///   @ventas          solo el módulo (@/ = la raíz)
    ///   #sdt             solo esa categoría (#bc, #dom, #eo, #num...)
    ///   sdt, bc, dom...  la abreviatura suelta también filtra (ver <see cref="Codes"/>)
    ///   ven.sdtcli       módulo(s) y nombre juntos, como el nombre calificado
    ///   sdtCli, Ventas   como lo escribe GeneXus: nombre y módulo
    /// Es la misma búsqueda que GxQuickOpen (Ctrl+O); también se usa para los atributos ("att cli nom").
    /// </summary>
    sealed class TypeQuery
    {
        /// <summary>Cada término con sus alternativas ("ven,com" → ["ven", "com"]).</summary>
        public readonly List<string[]> Terms = new List<string[]>();
        public readonly List<string[]> ModuleTerms = new List<string[]>();
        /// <summary>"#sdt" o una abreviatura suelta ("sdt,bc" = cualquiera de las dos).</summary>
        public readonly List<string[]> GroupTerms = new List<string[]>();
        public readonly List<KeyValuePair<string[], string>> Qualified = new List<KeyValuePair<string[], string>>();
        public bool RootOnly;
        /// <summary>
        /// Palabras que son comienzo de un segmento de módulo ("ven" → Ventas): se buscan como módulo o
        /// como parte del nombre, pero no por letras sueltas (traería ruido).
        /// </summary>
        public readonly HashSet<string> ModuleWords = new HashSet<string>(StringComparer.Ordinal);

        public bool IsEmpty => Terms.Count == 0 && ModuleTerms.Count == 0 && GroupTerms.Count == 0 && Qualified.Count == 0 && !RootOnly;

        /// <summary>Con más de un término, una palabra que coincide con un módulo cuenta casi como el nombre.</summary>
        public bool MultiTerm => Terms.Count + ModuleTerms.Count + GroupTerms.Count + Qualified.Count > 1;

        const int MinModuleWordLength = 3;

        /// <summary>Abreviaturas que, escritas como palabra suelta, filtran por categoría igual que "#sdt".</summary>
        static readonly HashSet<string> Codes = new HashSet<string>(StringComparer.Ordinal) { "sdt", "bc", "trn", "dom", "eo" };

        // "sdtOutput, Sistema.Global": el formato con que GeneXus muestra los tipos de un módulo
        static readonly Regex GeneXusForm = new Regex(@"^\s*([^\s,]+)\s*,\s+([^\s,]*)\s*$", RegexOptions.CultureInvariant);

        static string[] Alternatives(string term) =>
            term.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim('.'))
                .Where(a => a.Length > 0)
                .Distinct()
                .ToArray();

        /// <param name="moduleSegments">Segmentos de los módulos en minúsculas (<see cref="TypeMatcher.ModuleSegments"/>).</param>
        /// <param name="groupCodes">Si "sdt", "bc"... sueltos filtran por categoría (no para los atributos).</param>
        public static TypeQuery Parse(string text, IReadOnlyCollection<string> moduleSegments, bool groupCodes = true)
        {
            var query = new TypeQuery();
            text = (text ?? string.Empty).ToLowerInvariant();

            System.Text.RegularExpressions.Match form = GeneXusForm.Match(text);
            if (form.Success && form.Groups[2].Value.Length > 0)
            {
                string[] modules = form.Groups[2].Value.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                query.Qualified.Add(new KeyValuePair<string[], string>(modules, form.Groups[1].Value));
                return query;
            }

            foreach (string raw in text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // Una coma pegada a un espacio separa palabras ("sdtcli, ventas" = "sdtcli ventas");
                // solo entre dos palabras sin espacio es "cualquiera de las dos" ("ventas,compras")
                string term = raw.Trim(',', '|');
                if (term.Length == 0)
                    continue;
                if (term[0] == '@')
                {
                    if (term == "@/" || term == "@.")
                    {
                        query.RootOnly = true;
                        continue;
                    }
                    string[] modules = Alternatives(term.Substring(1));
                    if (modules.Length > 0)
                        query.ModuleTerms.Add(modules);
                }
                else if (term[0] == '#')
                {
                    string[] groups = Alternatives(term.Substring(1));
                    if (groups.Length > 0)
                        query.GroupTerms.Add(groups);
                }
                else if (term.IndexOf('.') > 0 && term.IndexOfAny(new[] { ',', '|' }) < 0)
                {
                    string[] parts = term.Split('.');
                    string name = parts[parts.Length - 1];
                    string[] modules = parts.Take(parts.Length - 1).Where(p => p.Length > 0).ToArray();
                    query.Qualified.Add(new KeyValuePair<string[], string>(modules, name));
                }
                else
                {
                    string[] alternatives = Alternatives(term);
                    if (alternatives.Length == 0)
                        continue;
                    if (groupCodes && alternatives.All(Codes.Contains))
                    {
                        query.GroupTerms.Add(alternatives);
                        continue;
                    }
                    query.Terms.Add(alternatives);
                    foreach (string word in alternatives)
                        if (word.Length >= MinModuleWordLength && moduleSegments.Any(s => s.StartsWith(word, StringComparison.Ordinal)))
                            query.ModuleWords.Add(word);
                }
            }
            return query;
        }
    }

    sealed class TypeScore
    {
        public int Score;
        /// <summary>Posiciones del nombre que coinciden (para resaltarlas), o null.</summary>
        public int[] Highlights;
        /// <summary>Posiciones del módulo que coinciden, o null.</summary>
        public int[] ModuleHighlights;
        /// <summary>Sin coincidencias por letras sueltas: vale para las secciones de arriba (recientes, módulo).</summary>
        public bool Strong;
    }

    /// <summary>Búsqueda y puntaje de tipos (sin dependencias de GeneXus), con las mismas reglas que GxQuickOpen.</summary>
    static class TypeMatcher
    {
        // Escalones del puntaje por nombre: cada uno le gana siempre al siguiente
        const int ExactScore = 1000;
        const int PrefixScore = 900;
        const int WordSubstringScore = 750;
        const int SubstringScore = 600;
        const int FuzzyMaxScore = 500;
        const int FuzzyMinScore = 100;
        // Una palabra que no está en el nombre pero sí en el módulo o es la categoría
        const int ModuleTermScore = 300;
        const int GroupTermScore = 250;
        // Con más de una palabra ("sdt ventas"), la que es comienzo de un segmento del módulo pesa como el nombre
        const int ModuleWordScore = 800;
        const int ModuleSegmentExactScore = 850;
        // Por debajo de esto el nombre coincidió por letras sueltas (no cuenta como "Strong")
        const int StrongNameScore = SubstringScore - 40;

        static readonly Dictionary<TypeGroup, string[]> GroupAliases = new Dictionary<TypeGroup, string[]>
        {
            [TypeGroup.Text] = new[] { "abc", "text", "texto", "char" },
            [TypeGroup.Number] = new[] { "123", "num", "numero", "number" },
            [TypeGroup.Date] = new[] { "fch", "fecha", "date" },
            [TypeGroup.Bool] = new[] { "bool", "boolean" },
            [TypeGroup.Binary] = new[] { "bin", "binary" },
            [TypeGroup.Geo] = new[] { "geo" },
            [TypeGroup.Sdt] = new[] { "sdt" },
            [TypeGroup.Bc] = new[] { "bc", "trn", "transaction", "transaccion" },
            [TypeGroup.Domain] = new[] { "dom", "dominio", "domain" },
            [TypeGroup.ExternalObject] = new[] { "eo", "externo", "external" },
            [TypeGroup.Attribute] = new[] { "atr" },
        };

        /// <summary>"#do" (empieza con) o una palabra suelta igual a la categoría ("sdt", "bc", "dom").</summary>
        static bool GroupMatches(TypeGroup group, string term, bool prefix) =>
            GroupAliases.TryGetValue(group, out string[] aliases)
            && aliases.Any(a => prefix ? a.StartsWith(term, StringComparison.Ordinal) : a == term);

        /// <summary>El puntaje del tipo para la búsqueda, o null si no coincide.</summary>
        public static TypeScore Evaluate(TypeQuery query, string objectName, string module, TypeGroup group)
        {
            if (query.RootOnly && module.Length > 0)
                return null;
            foreach (string[] groups in query.GroupTerms)
                if (!groups.Any(g => GroupMatches(group, g, prefix: true)))
                    return null;

            string nameLower = objectName.ToLowerInvariant();
            int score = 0;
            bool strong = true;
            List<int> highlights = null;
            List<int> moduleHighlights = null;

            foreach (string[] modules in query.ModuleTerms)
            {
                int[] positions = null;
                if (!modules.Any(m => (positions = ModuleMatch(module, m, out _)) != null))
                    return null;
                AddHighlights(ref moduleHighlights, positions);
            }

            foreach (var qualified in query.Qualified)
            {
                // "ven.sdtcli" = módulo Ven… y nombre sdtcli…; "interfases.registros" también puede ser
                // el módulo completo (todo lo de Interfases.Registros y sus submódulos): gana el mejor
                int best = -1;
                int[] bestName = null, bestModule = null;
                if (ModuleSegmentsMatch(module, qualified.Key))
                {
                    int[] positions = null;
                    int nameScore = qualified.Value.Length == 0 ? 0 : ScoreName(objectName, nameLower, qualified.Value, out positions);
                    if (nameScore >= 0)
                    {
                        best = nameScore;
                        bestName = positions;
                        bestModule = ModuleSegmentsPositions(module, qualified.Key);
                    }
                }
                if (qualified.Value.Length > 0)
                {
                    int[] modulePositions = ModulePathMatch(module, qualified.Key.Concat(new[] { qualified.Value }).ToArray(), out bool exact);
                    int moduleScore = modulePositions == null ? -1 : exact ? ModuleSegmentExactScore : query.MultiTerm ? ModuleWordScore : ModuleTermScore;
                    if (moduleScore > best)
                    {
                        best = moduleScore;
                        bestName = null;
                        bestModule = modulePositions;
                    }
                }
                if (best < 0)
                    return null;
                strong &= best >= StrongNameScore || bestName == null;
                score += best;
                AddHighlights(ref highlights, bestName);
                AddHighlights(ref moduleHighlights, bestModule);
            }

            foreach (string[] alternatives in query.Terms)
            {
                int best = -1;
                int[] bestName = null, bestModule = null;
                foreach (string term in alternatives)
                {
                    int nameScore = ScoreName(objectName, nameLower, term, out int[] positions, allowFuzzy: !query.ModuleWords.Contains(term));
                    if (nameScore > best)
                    {
                        best = nameScore;
                        bestName = positions;
                        bestModule = null;
                    }
                    // Con punto ("ventas,interfases.registros") es un módulo calificado
                    bool segment, exact;
                    int[] modulePositions;
                    if (term.IndexOf('.') >= 0)
                    {
                        modulePositions = ModulePathMatch(module, term.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries), out exact);
                        segment = true;
                    }
                    else
                    {
                        modulePositions = ModuleMatch(module, term, out segment);
                        exact = modulePositions != null && modulePositions.Length == SegmentLength(module, modulePositions[0]);
                    }
                    if (modulePositions != null)
                    {
                        int moduleScore = !segment || !query.MultiTerm ? ModuleTermScore
                            : exact ? ModuleSegmentExactScore
                            : ModuleWordScore;
                        if (moduleScore > best)
                        {
                            best = moduleScore;
                            bestName = null;
                            bestModule = modulePositions;
                        }
                    }
                    if (best < GroupTermScore && GroupMatches(group, term, prefix: false))
                    {
                        best = GroupTermScore;
                        bestName = bestModule = null;
                    }
                }
                if (best < 0)
                    return null;
                strong &= best >= StrongNameScore || bestName == null;
                score += best;
                AddHighlights(ref highlights, bestName);
                AddHighlights(ref moduleHighlights, bestModule);
            }

            return new TypeScore
            {
                Score = score,
                Strong = strong,
                Highlights = highlights?.Distinct().OrderBy(p => p).ToArray(),
                ModuleHighlights = moduleHighlights?.Distinct().OrderBy(p => p).ToArray(),
            };
        }

        /// <summary>Los segmentos de los módulos, en minúsculas ("Sistema.Global" → sistema, global).</summary>
        public static HashSet<string> ModuleSegments(IEnumerable<string> modules)
        {
            var segments = new HashSet<string>(StringComparer.Ordinal);
            foreach (string module in modules.Where(m => m.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (string segment in module.Split('.'))
                    segments.Add(segment.ToLowerInvariant());
            return segments;
        }

        static void AddHighlights(ref List<int> highlights, int[] positions)
        {
            if (positions == null)
                return;
            if (highlights == null)
                highlights = new List<int>();
            highlights.AddRange(positions);
        }

        /// <summary>
        /// Dónde aparece el término en el módulo: preferentemente al comienzo de un segmento
        /// (<paramref name="segment"/>), si no en cualquier parte. Null si no aparece.
        /// </summary>
        static int[] ModuleMatch(string module, string term, out bool segment)
        {
            segment = false;
            if (module.Length == 0 || term.Length == 0)
                return null;
            int index = module.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return null;
            for (int i = index; i >= 0; i = module.IndexOf(term, i + 1, StringComparison.OrdinalIgnoreCase))
            {
                if (i == 0 || module[i - 1] == '.')
                {
                    segment = true;
                    return Range(i, term.Length);
                }
            }
            return Range(index, term.Length);
        }

        static int SegmentLength(string module, int start)
        {
            int dot = module.IndexOf('.', start);
            return (dot < 0 ? module.Length : dot) - start;
        }

        /// <summary>Posiciones de los comienzos de segmento que coinciden con "ven.cli" (para resaltarlas).</summary>
        static int[] ModuleSegmentsPositions(string module, string[] parts)
        {
            if (parts.Length == 0 || module.Length == 0)
                return null;
            var positions = new List<int>();
            int start = 0;
            foreach (string part in parts)
            {
                while (start < module.Length)
                {
                    int end = module.IndexOf('.', start);
                    if (end < 0)
                        end = module.Length;
                    bool matches = end - start >= part.Length && string.Compare(module, start, part, 0, part.Length, StringComparison.OrdinalIgnoreCase) == 0;
                    int segmentStart = start;
                    start = end + 1;
                    if (matches)
                    {
                        positions.AddRange(Range(segmentStart, part.Length));
                        break;
                    }
                }
            }
            return positions.ToArray();
        }

        /// <summary>
        /// "inter.reg" contra "Interfases.Registros.Detalle": cada parte es el comienzo de un segmento,
        /// en orden. Devuelve las posiciones para resaltar (null si no coincide) y si la última parte es
        /// el segmento entero (<paramref name="exact"/>: "interfases.registros", no "inter.reg").
        /// </summary>
        static int[] ModulePathMatch(string module, string[] parts, out bool exact)
        {
            exact = false;
            if (parts.Length == 0 || module.Length == 0)
                return null;
            var positions = new List<int>();
            int start = 0;
            foreach (string part in parts)
            {
                bool found = false;
                while (!found && start < module.Length)
                {
                    int end = module.IndexOf('.', start);
                    if (end < 0)
                        end = module.Length;
                    if (end - start >= part.Length && string.Compare(module, start, part, 0, part.Length, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        positions.AddRange(Range(start, part.Length));
                        exact = end - start == part.Length;
                        found = true;
                    }
                    start = end + 1;
                }
                if (!found)
                    return null;
            }
            return positions.ToArray();
        }

        /// <summary>
        /// "sis.glo" contra "Sistema.Global": cada parte es el comienzo de un segmento del módulo, en
        /// orden (no hace falta escribirlos todos).
        /// </summary>
        static bool ModuleSegmentsMatch(string module, string[] parts)
        {
            if (parts.Length == 0)
                return true;
            string[] segments = module.Length > 0 ? module.Split('.') : new string[0];
            int next = 0;
            foreach (string part in parts)
            {
                while (next < segments.Length && !segments[next].StartsWith(part, StringComparison.OrdinalIgnoreCase))
                    next++;
                if (next == segments.Length)
                    return false;
                next++;
            }
            return true;
        }

        /// <summary>
        /// Puntaje del nombre para un término en minúsculas, o -1. Exacto &gt; empieza con &gt; contiene
        /// desde el comienzo de una palabra (sdt|Cliente) &gt; contiene &gt; las letras en orden
        /// ("sdtcli" → sdtCliente), premiando los comienzos de palabra y las letras seguidas.
        /// </summary>
        static int ScoreName(string name, string nameLower, string term, out int[] positions, bool allowFuzzy = true)
        {
            positions = null;
            if (term.Length == 0)
                return 0;
            if (term.Length > nameLower.Length)
                return -1;

            if (nameLower == term)
            {
                positions = Range(0, term.Length);
                return ExactScore;
            }
            if (nameLower.StartsWith(term, StringComparison.Ordinal))
            {
                positions = Range(0, term.Length);
                // Entre los que empiezan igual, primero los más cortos
                return PrefixScore + 50 * term.Length / nameLower.Length;
            }

            int index = nameLower.IndexOf(term, StringComparison.Ordinal);
            if (index >= 0)
            {
                // Si aparece más de una vez, preferir la que empieza una palabra
                for (int i = index; i >= 0; i = nameLower.IndexOf(term, i + 1, StringComparison.Ordinal))
                {
                    if (IsWordStart(name, i))
                    {
                        positions = Range(i, term.Length);
                        return WordSubstringScore + 50 * term.Length / nameLower.Length;
                    }
                }
                positions = Range(index, term.Length);
                return SubstringScore + 50 * term.Length / nameLower.Length - Math.Min(index, 40);
            }

            if (term.Length < 2 || !allowFuzzy)
                return -1;
            return FuzzyScore(name, nameLower, term, out positions);
        }

        static int[] Range(int start, int count)
        {
            var result = new int[count];
            for (int i = 0; i < count; i++)
                result[i] = start + i;
            return result;
        }

        /// <summary>Comienzo de palabra: inicio, mayúscula después de minúscula (sdtCliente), después de _ o de un número.</summary>
        static bool IsWordStart(string name, int i)
        {
            if (i == 0)
                return true;
            char c = name[i];
            char prev = name[i - 1];
            if (!char.IsLetterOrDigit(prev))
                return char.IsLetterOrDigit(c);
            if (char.IsUpper(c))
                return !char.IsUpper(prev) || (i + 1 < name.Length && char.IsLower(name[i + 1]));   // SDT|Cliente
            if (char.IsDigit(c))
                return !char.IsDigit(prev);
            return char.IsLetter(c) && char.IsDigit(prev);
        }

        /// <summary>
        /// Letras del término en orden dentro del nombre, eligiendo la mejor ubicación (programación
        /// dinámica): cada letra suma, más si empieza una palabra o sigue a la anterior; los saltos restan.
        /// </summary>
        static int FuzzyScore(string name, string nameLower, string term, out int[] positions)
        {
            positions = null;
            int n = nameLower.Length;
            int m = term.Length;

            // Descarte rápido: ¿están todas las letras en orden?
            for (int i = 0, j = 0; i < m; i++, j++)
            {
                j = nameLower.IndexOf(term[i], j);
                if (j < 0)
                    return -1;
            }

            const int Base = 1, WordStartBonus = 8, ConsecutiveBonus = 6, FirstLetterBonus = 4;
            const int Impossible = int.MinValue / 2;
            var best = new int[m, n];
            var from = new int[m, n];

            for (int i = 0; i < m; i++)
            {
                // Mejor (best[i-1][k] + k) para k < j-1, para el salto con penalidad (j - k - 1)
                int runningMax = Impossible, runningArg = -1;
                for (int j = 0; j < n; j++)
                {
                    if (i > 0 && j >= 2 && best[i - 1, j - 2] > Impossible && best[i - 1, j - 2] + (j - 2) > runningMax)
                    {
                        runningMax = best[i - 1, j - 2] + (j - 2);
                        runningArg = j - 2;
                    }

                    best[i, j] = Impossible;
                    if (nameLower[j] != term[i])
                        continue;
                    int letter = Base + (IsWordStart(name, j) ? WordStartBonus : 0);
                    if (i == 0)
                    {
                        best[i, j] = letter + (j == 0 ? FirstLetterBonus : 0) - Math.Min(j, 10) / 2;
                        from[i, j] = -1;
                        continue;
                    }
                    int consecutive = j >= 1 && best[i - 1, j - 1] > Impossible ? best[i - 1, j - 1] + letter + ConsecutiveBonus : Impossible;
                    int jump = runningMax > Impossible ? runningMax - j + 1 + letter : Impossible;
                    if (consecutive >= jump && consecutive > Impossible)
                    {
                        best[i, j] = consecutive;
                        from[i, j] = j - 1;
                    }
                    else if (jump > Impossible)
                    {
                        best[i, j] = jump;
                        from[i, j] = runningArg;
                    }
                }
            }

            int end = -1, top = Impossible;
            for (int j = 0; j < n; j++)
            {
                if (best[m - 1, j] > top)
                {
                    top = best[m - 1, j];
                    end = j;
                }
            }
            if (end < 0)
                return -1;

            positions = new int[m];
            for (int i = m - 1, j = end; i >= 0; j = from[i, j], i--)
                positions[i] = j;

            // Llevar al rango [FuzzyMinScore, FuzzyMaxScore): el ideal es empezar palabra y seguir pegado en cada letra
            int ideal = m * (Base + WordStartBonus + ConsecutiveBonus) + FirstLetterBonus;
            int scaled = FuzzyMinScore + (FuzzyMaxScore - FuzzyMinScore - 1) * Math.Max(0, top) / ideal;
            return Math.Max(FuzzyMinScore, Math.Min(FuzzyMaxScore - 1, scaled));
        }
    }
}
