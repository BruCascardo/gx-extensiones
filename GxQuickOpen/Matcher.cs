using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GxQuickOpen
{
    /// <summary>Tipo de objeto (Procedure, Web Panel...): nombre, abreviaturas para "#tipo" e ícono.</summary>
    sealed class TypeInfo
    {
        public Guid Id;
        /// <summary>Como lo muestra GeneXus: "Procedure", "Web Panel"...</summary>
        public string Name;
        /// <summary>En minúsculas: "procedure", "proc", "prc", "webpanel", "wp"...</summary>
        public string[] Aliases;
        /// <summary>El KBObjectDescriptor (no se referencia acá para poder probar sin GeneXus).</summary>
        public object Descriptor;
        /// <summary>Ícono 16x16, se completa la primera vez que se dibuja.</summary>
        public Image Icon;

        public bool Matches(string term) => Aliases.Any(a => a.StartsWith(term, StringComparison.Ordinal));

        /// <summary>
        /// Abreviaturas que, escritas como palabra suelta, filtran por tipo igual que "#tipo":
        /// "prc inget ventas" = los procedimientos inGet de Ventas.
        /// </summary>
        public static readonly HashSet<string> Codes = new HashSet<string>(StringComparer.Ordinal)
        {
            "prc", "proc", "dp", "wp", "wc", "mp", "sdt", "trn", "dom", "eo", "ds", "att", "tbl", "img", "rpt", "sdp", "ww",
        };

        /// <summary>
        /// Un Web Panel puede ser página, componente o master page (propiedad Type): cada uno se muestra
        /// y se filtra como un tipo aparte ("wp", "wc", "mp"); "#webpanel" o "#web" los traen a todos.
        /// </summary>
        public TypeInfo WebPanelVariant(string name, params string[] aliases) => new TypeInfo
        {
            Id = Id,
            Name = name,
            Aliases = aliases.Concat(new[] { "webpanel", "web" }).ToArray(),
            Descriptor = Descriptor,
        };

        public const string WebPanelInternalName = "WebPanel";

        public TypeInfo WebComponentVariant() => WebPanelVariant("Web Component", "wc", "webcomponent", "web component", "component", "componente");

        public TypeInfo MasterPageVariant() => WebPanelVariant("Master Page", "mp", "masterpage", "master page", "master");

        /// <summary>Si no se pudo saber cuáles son componentes: "wc" y "mp" traen todos los Web Panels.</summary>
        public void AddWebPanelFallbackAliases() => Aliases = Aliases.Concat(new[] { "wc", "mp" }).Distinct().ToArray();

        static readonly Dictionary<string, string[]> KnownAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Procedure"] = new[] { "prc", "proc" },
            ["Transaction"] = new[] { "trn", "trx" },
            ["WebPanel"] = new[] { "wp", "wbp" },
            ["DataProvider"] = new[] { "dp" },
            ["SDT"] = new[] { "sdt" },
            ["Domain"] = new[] { "dom" },
            ["Attribute"] = new[] { "att", "atr" },
            ["ExternalObject"] = new[] { "eo", "ext" },
            ["DataSelector"] = new[] { "ds" },
            ["WorkWithForWeb"] = new[] { "ww" },
            ["SDPanel"] = new[] { "sdp", "panel" },
            ["Panel"] = new[] { "panel" },
            ["Table"] = new[] { "tbl", "tabla" },
            ["Image"] = new[] { "img" },
            ["File"] = new[] { "file", "archivo" },
            ["Module"] = new[] { "mod", "modulo", "módulo" },
            ["Folder"] = new[] { "fld", "carpeta" },
            ["Menu"] = new[] { "menu" },
            ["Report"] = new[] { "rpt" },
            ["API"] = new[] { "api" },
        };

        /// <summary>
        /// Abreviaturas de un tipo: el nombre interno y el visible (con y sin espacios), las iniciales
        /// ("Web Panel" → "wp", "Structured Data Type" → "sdt") y las conocidas (Procedure → "prc").
        /// </summary>
        public static string[] BuildAliases(string internalName, string displayName)
        {
            var aliases = new List<string>();
            void Add(string alias)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                    aliases.Add(alias.ToLowerInvariant());
            }

            Add(internalName);
            Add(displayName);
            Add(displayName?.Replace(" ", string.Empty));
            foreach (string word in (displayName ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                Add(word);
            Add(Initials(displayName));
            Add(Initials(internalName));
            if (internalName != null && KnownAliases.TryGetValue(internalName, out string[] known))
                foreach (string alias in known)
                    Add(alias);
            return aliases.Distinct().ToArray();
        }

        /// <summary>"Web Panel" → "wp"; "WebPanel" → "wp"; "SDT" → null (no aporta).</summary>
        static string Initials(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            var initials = new List<char>();
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsLetter(text[i]) && (i == 0 || text[i - 1] == ' ' || (char.IsUpper(text[i]) && char.IsLower(text[i - 1]))))
                    initials.Add(text[i]);
            }
            return initials.Count >= 2 ? new string(initials.ToArray()) : null;
        }
    }

    /// <summary>Un objeto de la KB, con lo necesario para buscar y mostrar.</summary>
    sealed class ObjectEntry
    {
        public string Name;
        /// <summary>Módulo calificado ("Ventas.Clientes"), vacío en la raíz.</summary>
        public string Module;
        public string Description;
        public TypeInfo Type;
        public Guid Guid;
        /// <summary>EntityKey del objeto (object para no depender de GeneXus acá).</summary>
        public object Key;

        string m_NameLower;
        public string NameLower => m_NameLower ?? (m_NameLower = Name.ToLowerInvariant());

        public override string ToString() => Module.Length > 0 ? Module + "." + Name : Name;
    }

    /// <summary>
    /// Lo que se escribió, separado en términos:
    ///   palabra       nombre (también vale el módulo o el tipo)
    ///   inget ventas  si una palabra es parte del nombre de un módulo, primero los objetos de ese módulo
    ///   ven,com       cualquiera de las dos (en una palabra o con @)
    ///   @ventas       solo el módulo (@/ = la raíz)
    ///   #proc         solo el tipo
    ///   prc, wp, sdt  la abreviatura suelta también filtra por tipo (ver <see cref="TypeInfo.Codes"/>)
    ///   ven.inget     módulo(s) y nombre en una sola palabra, como el nombre calificado
    /// </summary>
    sealed class Query
    {
        /// <summary>Cada término con sus alternativas ("ven,com" → ["ven", "com"]).</summary>
        public readonly List<string[]> Terms = new List<string[]>();
        public readonly List<string[]> ModuleTerms = new List<string[]>();
        /// <summary>"#proc" o una abreviatura suelta ("prc", "wp,wc" = cualquiera de los dos).</summary>
        public readonly List<string[]> TypeTerms = new List<string[]>();
        public readonly List<KeyValuePair<string[], string>> Qualified = new List<KeyValuePair<string[], string>>();
        public bool RootOnly;

        /// <summary>
        /// Palabras que son el comienzo de un segmento de algún módulo de la KB ("ven" → Ventas): se
        /// buscan como módulo o como parte del nombre, pero no por letras sueltas (traería ruido).
        /// </summary>
        public readonly HashSet<string> ModuleWords = new HashSet<string>(StringComparer.Ordinal);

        public bool IsEmpty => Terms.Count == 0 && ModuleTerms.Count == 0 && TypeTerms.Count == 0 && Qualified.Count == 0 && !RootOnly;

        /// <summary>Si hay algo que busque por nombre (si no, el orden es por recientes y alfabético).</summary>
        public bool HasNameTerms => Terms.Count > 0 || Qualified.Any(q => q.Value.Length > 0);

        /// <summary>Con más de un término, una palabra que coincide con un módulo cuenta casi como el nombre.</summary>
        public bool MultiTerm => Terms.Count + Qualified.Count + ModuleTerms.Count + TypeTerms.Count > 1;

        const int MinModuleWordLength = 3;

        /// <summary>Marca las palabras que son comienzo de un segmento de módulo (en minúsculas).</summary>
        public void ResolveModuleWords(IReadOnlyCollection<string> moduleSegments)
        {
            ModuleWords.Clear();
            foreach (string word in Terms.SelectMany(t => t))
                if (word.Length >= MinModuleWordLength && moduleSegments.Any(s => s.StartsWith(word, StringComparison.Ordinal)))
                    ModuleWords.Add(word);
        }

        static string[] Alternatives(string term) =>
            term.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim('.'))
                .Where(a => a.Length > 0)
                .Distinct()
                .ToArray();

        public static Query Parse(string text)
        {
            var query = new Query();
            foreach (string raw in (text ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // Una coma pegada a un espacio separa palabras ("inget, ventas" = "inget ventas");
                // solo entre dos palabras sin espacio es "cualquiera de los dos" ("ventas,compras")
                string term = raw.Trim(',', '|').ToLowerInvariant();
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
                    string[] types = Alternatives(term.Substring(1));
                    if (types.Length > 0)
                        query.TypeTerms.Add(types);
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
                    if (alternatives.Length > 0 && alternatives.All(TypeInfo.Codes.Contains))
                        query.TypeTerms.Add(alternatives);
                    else if (alternatives.Length > 0)
                        query.Terms.Add(alternatives);
                }
            }
            return query;
        }
    }

    sealed class Match
    {
        public ObjectEntry Entry;
        public int Score;
        /// <summary>Posiciones del nombre que coinciden (para resaltarlas), o null.</summary>
        public int[] Highlights;
        /// <summary>Posiciones del módulo que coinciden, o null.</summary>
        public int[] ModuleHighlights;
    }

    static class Matcher
    {
        // Escalones del puntaje por nombre: cada uno le gana siempre al siguiente
        const int ExactScore = 1000;
        const int PrefixScore = 900;
        const int WordSubstringScore = 750;
        const int SubstringScore = 600;
        const int FuzzyMaxScore = 500;
        const int FuzzyMinScore = 100;
        // Una palabra que no está en el nombre pero sí en el módulo o en el tipo
        const int ModuleTermScore = 300;
        const int TypeTermScore = 250;
        // Con más de una palabra ("inget ventas"), la que es comienzo de un segmento del módulo pesa
        // como el nombre: los inGet de Ventas quedan antes que un inGetVentasX de otro módulo
        const int ModuleWordScore = 800;
        const int ModuleSegmentExactScore = 850;

        /// <summary>El puntaje del objeto para la búsqueda, o null si no coincide.</summary>
        public static Match Evaluate(Query query, ObjectEntry entry)
        {
            if (query.RootOnly && entry.Module.Length > 0)
                return null;
            foreach (string[] types in query.TypeTerms)
                if (!types.Any(entry.Type.Matches))
                    return null;

            int score = 0;
            List<int> highlights = null;
            List<int> moduleHighlights = null;

            foreach (string[] modules in query.ModuleTerms)
            {
                int[] positions = null;
                if (!modules.Any(m => (positions = ModuleMatch(entry.Module, m, out _)) != null))
                    return null;
                AddHighlights(ref moduleHighlights, positions);
            }

            foreach (var qualified in query.Qualified)
            {
                // "ven.inget" = módulo Ven… y nombre inget…; "interfases.registros" también puede ser
                // el módulo completo (todo lo de Interfases.Registros y sus submódulos): gana el mejor
                int best = -1;
                int[] bestName = null, bestModule = null;
                if (ModuleSegmentsMatch(entry.Module, qualified.Key))
                {
                    int[] positions = null;
                    int nameScore = qualified.Value.Length == 0 ? 0 : ScoreName(entry.Name, entry.NameLower, qualified.Value, out positions);
                    if (nameScore >= 0)
                    {
                        best = nameScore;
                        bestName = positions;
                        bestModule = ModuleSegmentsPositions(entry.Module, qualified.Key);
                    }
                }
                if (qualified.Value.Length > 0)
                {
                    int[] modulePositions = ModulePathMatch(entry.Module, qualified.Key.Concat(new[] { qualified.Value }).ToArray(), out bool exact);
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
                    int nameScore = ScoreName(entry.Name, entry.NameLower, term, out int[] positions, allowFuzzy: !query.ModuleWords.Contains(term));
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
                        modulePositions = ModulePathMatch(entry.Module, term.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries), out exact);
                        segment = true;
                    }
                    else
                    {
                        modulePositions = ModuleMatch(entry.Module, term, out segment);
                        exact = modulePositions != null && modulePositions.Length == SegmentLength(entry.Module, modulePositions[0]);
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
                    if (best < TypeTermScore && entry.Type.Matches(term))
                    {
                        best = TypeTermScore;
                        bestName = bestModule = null;
                    }
                }
                if (best < 0)
                    return null;
                AddHighlights(ref highlights, bestName);
                AddHighlights(ref moduleHighlights, bestModule);
                score += best;
            }

            return new Match
            {
                Entry = entry,
                Score = score,
                Highlights = highlights?.Distinct().OrderBy(p => p).ToArray(),
                ModuleHighlights = moduleHighlights?.Distinct().OrderBy(p => p).ToArray(),
            };
        }

        /// <summary>
        /// Dónde aparece el término en el módulo: preferentemente al comienzo de un segmento
        /// (<paramref name="segment"/>), si no en cualquier parte. Null si no aparece.
        /// </summary>
        public static int[] ModuleMatch(string module, string term, out bool segment)
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
                    bool matches = string.Compare(module, start, part, 0, part.Length, StringComparison.OrdinalIgnoreCase) == 0 && end - start >= part.Length;
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
        public static int[] ModulePathMatch(string module, string[] parts, out bool exact)
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

        static void AddHighlights(ref List<int> highlights, int[] positions)
        {
            if (positions == null)
                return;
            if (highlights == null)
                highlights = new List<int>();
            highlights.AddRange(positions);
        }

        /// <summary>
        /// "ven.cli" contra "Ventas.Clientes": cada parte tiene que ser el comienzo de un segmento del
        /// módulo, en orden (no hace falta escribirlos todos).
        /// </summary>
        public static bool ModuleSegmentsMatch(string module, string[] parts)
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
        /// desde el comienzo de una palabra (inGet|Cliente) &gt; contiene &gt; las letras en orden
        /// ("igc" → inGetCliente), premiando los comienzos de palabra y las letras seguidas.
        /// </summary>
        public static int ScoreName(string name, string nameLower, string term, out int[] positions, bool allowFuzzy = true)
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

        /// <summary>Comienzo de palabra: inicio, mayúscula después de minúscula (inGet), después de _ o de un número.</summary>
        public static bool IsWordStart(string name, int i)
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

    /// <summary>Contexto para ordenar: objetos recientes y módulo del documento activo.</summary>
    sealed class RankContext
    {
        /// <summary>Guid → posición en recientes (0 = el último abierto).</summary>
        public Dictionary<Guid, int> RecentRank = new Dictionary<Guid, int>();
        public int RecentCount;
        /// <summary>Módulo del objeto que se está editando, o null.</summary>
        public string CurrentModule;

        const int RecentMaxBonus = 150;
        const int SameModuleBonus = 60;
        const int NearModuleBonus = 25;

        public int Bonus(ObjectEntry entry)
        {
            int bonus = 0;
            if (RecentRank.TryGetValue(entry.Guid, out int rank))
                bonus += RecentMaxBonus - RecentMaxBonus * rank / Math.Max(1, RecentCount);
            if (CurrentModule != null && CurrentModule.Length > 0 && entry.Module.Length > 0)
            {
                if (string.Equals(entry.Module, CurrentModule, StringComparison.OrdinalIgnoreCase))
                    bonus += SameModuleBonus;
                else if (CurrentModule.StartsWith(entry.Module + ".", StringComparison.OrdinalIgnoreCase)
                         || entry.Module.StartsWith(CurrentModule + ".", StringComparison.OrdinalIgnoreCase))
                    bonus += NearModuleBonus;
            }
            return bonus;
        }
    }

    /// <summary>Módulo con la cantidad de resultados que tiene ("chip" de filtro).</summary>
    sealed class ModuleFacet
    {
        /// <summary>null = todos; "" = la raíz.</summary>
        public string Module;
        public int Count;

        public string Label => Module == null ? "Todos" : Module.Length == 0 ? "(raíz)" : Module;
    }

    sealed class SearchResult
    {
        public List<Match> Matches = new List<Match>();
        /// <summary>Total antes de recortar a lo que se muestra.</summary>
        public int Total;
        /// <summary>Por módulo, de los que coinciden con el texto (antes de filtrar por chip). El primero es "Todos".</summary>
        public List<ModuleFacet> Facets = new List<ModuleFacet>();
    }

    static class Search
    {
        /// <summary>
        /// Busca en todos los objetos. <paramref name="facet"/> filtra por un módulo exacto (null = todos).
        /// Sin texto, devuelve los recientes.
        /// </summary>
        public static SearchResult Run(IReadOnlyList<ObjectEntry> entries, Query query, RankContext context, string facet, int maxShown)
        {
            var matches = new List<Match>();
            query.ResolveModuleWords(ModuleSegments(entries));
            if (query.IsEmpty)
            {
                foreach (ObjectEntry entry in entries)
                    if (context.RecentRank.ContainsKey(entry.Guid))
                        matches.Add(new Match { Entry = entry, Score = -context.RecentRank[entry.Guid] });
            }
            else
            {
                foreach (ObjectEntry entry in entries)
                {
                    Match match = Matcher.Evaluate(query, entry);
                    if (match == null)
                        continue;
                    match.Score += context.Bonus(entry);
                    matches.Add(match);
                }
            }

            var result = new SearchResult();
            result.Facets.Add(new ModuleFacet { Module = null, Count = matches.Count });
            result.Facets.AddRange(matches
                .GroupBy(m => m.Entry.Module, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ModuleFacet { Module = g.First().Entry.Module, Count = g.Count() })
                .OrderByDescending(f => f.Count)
                .ThenBy(f => f.Module, StringComparer.OrdinalIgnoreCase));

            IEnumerable<Match> filtered = facet == null
                ? matches
                : matches.Where(m => string.Equals(m.Entry.Module, facet, StringComparison.OrdinalIgnoreCase));
            var list = filtered.ToList();
            result.Total = list.Count;
            list.Sort(Compare);
            result.Matches = list.Count > maxShown ? list.GetRange(0, maxShown) : list;
            return result;
        }

        static IReadOnlyList<ObjectEntry> s_SegmentsSource;
        static HashSet<string> s_Segments;

        /// <summary>Los segmentos de módulo de la KB, en minúsculas ("Ventas.Clientes" → ventas, clientes).</summary>
        static HashSet<string> ModuleSegments(IReadOnlyList<ObjectEntry> entries)
        {
            if (!ReferenceEquals(entries, s_SegmentsSource))
            {
                var segments = new HashSet<string>(StringComparer.Ordinal);
                string last = null;
                foreach (ObjectEntry entry in entries)
                {
                    if (entry.Module.Length == 0 || entry.Module == last)
                        continue;
                    last = entry.Module;
                    foreach (string segment in entry.Module.Split('.'))
                        segments.Add(segment.ToLowerInvariant());
                }
                s_Segments = segments;
                s_SegmentsSource = entries;
            }
            return s_Segments;
        }

        static int Compare(Match a, Match b)
        {
            int byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0)
                return byScore;
            int byLength = a.Entry.Name.Length.CompareTo(b.Entry.Name.Length);
            if (byLength != 0)
                return byLength;
            int byName = string.Compare(a.Entry.Name, b.Entry.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.Compare(a.Entry.Module, b.Entry.Module, StringComparison.OrdinalIgnoreCase);
        }
    }
}
