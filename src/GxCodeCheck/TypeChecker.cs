using System;
using System.Collections.Generic;
using System.Linq;

namespace GxCodeCheck
{
    /// <summary>
    /// Chequeo de tipos propio, en milisegundos: lo que la especificación de GeneXus informa unos segundos
    /// después de guardar, para los casos más comunes. Las llamadas a procedimientos (cantidad y tipo de
    /// parámetros contra su parm()), los métodos de textos, fechas y colecciones, los miembros de los SDT,
    /// los valores de los dominios enumerados, las asignaciones y comparaciones entre tipos distintos y las
    /// variables no definidas.
    /// </summary>
    /// <remarks>
    /// Solo juzga lo que entiende seguro (variables, miembros, literales, atributos, llamadas y operaciones
    /// entre ellos): lo que no entiende lo deja pasar. Un error que no existe confunde más que uno que se escapa, y la especificación lo
    /// encuentra igual.
    /// </remarks>
    static partial class TypeChecker
    {
        public const string WrongType = "gxt001";
        public const string TooMany = "gxt002";
        public const string TooFew = "gxt003";
        public const string AssignMismatch = "gxt004";
        public const string Undefined = "gxt005";
        public const string MissingMember = "gxt006";
        public const string InvalidValue = "gxt007";
        public const string CompareMismatch = "gxt008";
        public const string UnknownMethod = "gxt009";
        public const string InputAssigned = "gxt010";
        public const string NoReturn = "gxt011";
        public const string Truncated = "gxt012";
        public const string NotBoolean = "gxt013";
        public const string NotFound = "gxt014";

        public const string QuickNote = "Chequeo rápido al guardar: la especificación de GeneXus lo confirma unos segundos después.";

        enum Kind { Name, Var, Number, String, Symbol }

        sealed class Token
        {
            public Kind Kind;
            public string Text;
            public int Line;
            public int Column;
            public int Length;
            public bool StartsLine;
            /// <summary>Paréntesis abiertos antes del token.</summary>
            public int Depth;
            /// <summary>Posición en la lista de tokens.</summary>
            public int Index;

            public bool Is(string symbol) => Kind == Kind.Symbol && Text == symbol;

            public bool IsWord(string word) => Kind == Kind.Name && string.Equals(Text, word, StringComparison.OrdinalIgnoreCase);
        }

        sealed class Method
        {
            public TypeInfo[] Parameters = Array.Empty<TypeInfo>();
            public int Minimum = -1;
            public TypeInfo Returns = TypeInfo.Unknown;
            /// <summary>Si se conocen bien sus parámetros; si no, solo se usa lo que devuelve.</summary>
            public bool Checked = true;

            public int Min => Minimum < 0 ? Parameters.Length : Minimum;
        }

        static readonly Dictionary<TypeGroup, Dictionary<string, Method>> Methods = new Dictionary<TypeGroup, Dictionary<string, Method>>
        {
            [TypeGroup.Text] = new Dictionary<string, Method>(StringComparer.OrdinalIgnoreCase)
            {
                ["Trim"] = new Method { Returns = TypeInfo.Text },
                ["ToUpper"] = new Method { Returns = TypeInfo.Text },
                ["ToLower"] = new Method { Returns = TypeInfo.Text },
                ["Length"] = new Method { Returns = TypeInfo.Number },
                ["IsEmpty"] = new Method { Returns = TypeInfo.Bool },
                ["CharAt"] = new Method { Parameters = new[] { TypeInfo.Number }, Returns = TypeInfo.Text },
                ["Substring"] = new Method { Parameters = new[] { TypeInfo.Number, TypeInfo.Number }, Minimum = 1, Returns = TypeInfo.Text },
                ["IndexOf"] = new Method { Parameters = new[] { TypeInfo.Text, TypeInfo.Number }, Minimum = 1, Returns = TypeInfo.Number },
                ["LastIndexOf"] = new Method { Parameters = new[] { TypeInfo.Text, TypeInfo.Number }, Minimum = 1, Returns = TypeInfo.Number },
                ["Replace"] = new Method { Parameters = new[] { TypeInfo.Text, TypeInfo.Text }, Returns = TypeInfo.Text },
                ["StartsWith"] = new Method { Parameters = new[] { TypeInfo.Text }, Returns = TypeInfo.Bool },
                ["EndsWith"] = new Method { Parameters = new[] { TypeInfo.Text }, Returns = TypeInfo.Bool },
                ["Contains"] = new Method { Parameters = new[] { TypeInfo.Text }, Returns = TypeInfo.Bool },
                // Existen; no se revisan sus parámetros
                ["SetEmpty"] = new Method { Checked = false },
                ["PadLeft"] = new Method { Returns = TypeInfo.Text, Checked = false },
                ["PadRight"] = new Method { Returns = TypeInfo.Text, Checked = false },
                ["IsMatch"] = new Method { Returns = TypeInfo.Bool, Checked = false },
                ["Matches"] = new Method { Checked = false },
                ["ReplaceRegEx"] = new Method { Returns = TypeInfo.Text, Checked = false },
                ["SplitRegEx"] = new Method { Checked = false },
                ["ToString"] = new Method { Returns = TypeInfo.Text, Checked = false },
            },
            [TypeGroup.Number] = new Dictionary<string, Method>(StringComparer.OrdinalIgnoreCase)
            {
                ["IsEmpty"] = new Method { Returns = TypeInfo.Bool },
                ["ToString"] = new Method { Returns = TypeInfo.Text, Checked = false },
                ["ToFormattedString"] = new Method { Returns = TypeInfo.Text, Checked = false },
                ["Round"] = new Method { Returns = TypeInfo.Number, Checked = false },
                ["Truncate"] = new Method { Returns = TypeInfo.Number, Checked = false },
                ["SetEmpty"] = new Method { Checked = false },
            },
            [TypeGroup.Date] = new Dictionary<string, Method>(StringComparer.OrdinalIgnoreCase)
            {
                ["IsEmpty"] = new Method { Returns = TypeInfo.Bool },
                ["Year"] = new Method { Returns = TypeInfo.Number },
                ["Month"] = new Method { Returns = TypeInfo.Number },
                ["Day"] = new Method { Returns = TypeInfo.Number },
                ["Hour"] = new Method { Returns = TypeInfo.Number },
                ["Minute"] = new Method { Returns = TypeInfo.Number },
                ["Second"] = new Method { Returns = TypeInfo.Number },
                ["AddDays"] = new Method { Parameters = new[] { TypeInfo.Number }, Returns = TypeInfo.Date },
                ["AddMonths"] = new Method { Parameters = new[] { TypeInfo.Number }, Returns = TypeInfo.Date },
                ["AddYears"] = new Method { Parameters = new[] { TypeInfo.Number }, Returns = TypeInfo.Date },
                ["AddHours"] = new Method { Parameters = new[] { TypeInfo.Number }, Returns = TypeInfo.Date },
                ["AddMinutes"] = new Method { Parameters = new[] { TypeInfo.Number }, Returns = TypeInfo.Date },
                ["AddSeconds"] = new Method { Parameters = new[] { TypeInfo.Number }, Returns = TypeInfo.Date },
                ["ToString"] = new Method { Returns = TypeInfo.Text, Checked = false },
                ["ToFormattedString"] = new Method { Returns = TypeInfo.Text, Checked = false },
                ["SetEmpty"] = new Method { Checked = false },
            },
            [TypeGroup.Bool] = new Dictionary<string, Method>(StringComparer.OrdinalIgnoreCase)
            {
                ["ToString"] = new Method { Returns = TypeInfo.Text, Checked = false },
                ["IsEmpty"] = new Method { Returns = TypeInfo.Bool },
                ["SetEmpty"] = new Method { Checked = false },
            },
        };

        // Métodos de los SDT y Business Components: solo lo que devuelven
        static readonly Dictionary<string, Method> StructureMethods = new Dictionary<string, Method>(StringComparer.OrdinalIgnoreCase)
        {
            ["ToJson"] = new Method { Returns = TypeInfo.Text, Checked = false },
            ["ToXml"] = new Method { Returns = TypeInfo.Text, Checked = false },
            ["FromJson"] = new Method { Returns = TypeInfo.Bool, Checked = false },
            ["FromXml"] = new Method { Returns = TypeInfo.Bool, Checked = false },
        };

        static readonly HashSet<string> CollectionMethodNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Add", "AddRange", "Item", "Remove", "Clear", "IndexOf", "Sort", "ToJson", "ToXml", "FromJson", "FromXml",
            "FromJsonFile", "FromXmlFile", "Clone", "RemoveItem", "Insert",
        };

        static readonly HashSet<string> Comparisons = new HashSet<string> { "=", "<>", "<", ">", "<=", ">=" };
        static readonly HashSet<string> Arithmetic = new HashSet<string> { "+", "-", "*", "/" };
        static readonly HashSet<string> Logical = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "And", "Or", "Like" };

        // Funciones del lenguaje: solo lo que devuelven (sus parámetros aceptan más de lo que parece)
        static readonly Dictionary<string, TypeInfo> Functions = new Dictionary<string, TypeInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Trim"] = TypeInfo.Text, ["LTrim"] = TypeInfo.Text, ["RTrim"] = TypeInfo.Text,
            ["Upper"] = TypeInfo.Text, ["Lower"] = TypeInfo.Text, ["Substr"] = TypeInfo.Text,
            ["Str"] = TypeInfo.Text, ["Space"] = TypeInfo.Text, ["Padl"] = TypeInfo.Text, ["Padr"] = TypeInfo.Text,
            ["Format"] = TypeInfo.Text, ["StrReplace"] = TypeInfo.Text, ["Chr"] = TypeInfo.Text,
            ["Len"] = TypeInfo.Number, ["Val"] = TypeInfo.Number, ["Int"] = TypeInfo.Number, ["Round"] = TypeInfo.Number,
            ["Abs"] = TypeInfo.Number, ["Mod"] = TypeInfo.Number, ["StrSearch"] = TypeInfo.Number, ["Asc"] = TypeInfo.Number,
            ["Year"] = TypeInfo.Number, ["Month"] = TypeInfo.Number, ["Day"] = TypeInfo.Number,
            ["Today"] = TypeInfo.Date, ["Now"] = TypeInfo.Date, ["Ymdtod"] = TypeInfo.Date, ["Ctod"] = TypeInfo.Date,
            ["Ctot"] = TypeInfo.Date, ["DtoC"] = TypeInfo.Text, ["TtoC"] = TypeInfo.Text,
            ["Iif"] = TypeInfo.Unknown, ["NullValue"] = TypeInfo.Unknown, ["Msg"] = TypeInfo.Unknown,
        };

        // Palabras que van antes de un paréntesis y no son llamadas
        static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "If", "Else", "While", "Case", "Otherwise", "Where", "And", "Or", "Not", "Like", "Return", "Do", "For",
            "Each", "In", "To", "Step", "Order", "When", "Defined", "By", "Unique", "Using", "Blocking", "Call",
            "Submit", "Print", "Commit", "Rollback", "New", "Event", "Sub", "Parm", "Null", "True", "False", "Link",
        };

        // Variables que GeneXus define solo
        static readonly HashSet<string> Standard = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Pgmname", "Pgmdesc", "Today", "Time", "Mode", "Gxremote",
        };

        static readonly HashSet<string> Continues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ",", "(", "+", "-", "*", "/", "=", "<", ">", "<=", ">=", "<>", "And", "Or", "Not", "Like",
        };

        public static List<Diagnostic> Run(SourceText source, ITypeContext context) => Run(source, context, out _);

        /// <summary>
        /// Además, en <paramref name="unresolved"/>, los miembros de SDT cuyo tipo no se pudo leer (para el log:
        /// lo que pasa por ellos no se revisa).
        /// </summary>
        public static List<Diagnostic> Run(SourceText source, ITypeContext context, out List<string> unresolved)
        {
            var checker = new Checker(source, context);
            checker.Run();
            unresolved = checker.Unresolved.ToList();
            return checker.Result;
        }

        /// <summary>Los objetos que el código llama (como están escritos), para leer sus parm() de antemano.</summary>
        public static List<string> CallNames(SourceText source) => new Checker(source, null).CallNames();

        sealed partial class Checker
        {
            readonly SourceText m_Source;
            readonly ITypeContext m_Context;
            readonly List<Token> m_Tokens;
            public readonly List<Diagnostic> Result = new List<Diagnostic>();
            public readonly HashSet<string> Unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public Checker(SourceText source, ITypeContext context)
            {
                m_Source = source;
                m_Context = context;
                m_Tokens = Tokenize(source);
            }

            Token At(int index) => index >= 0 && index < m_Tokens.Count ? m_Tokens[index] : null;

            public List<string> CallNames()
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int k = 0; k < m_Tokens.Count; k++)
                {
                    if (m_Tokens[k].Kind == Kind.Name && At(k - 1)?.Is(".") != true && Callee(k, out string name, out _, out _))
                        names.Add(name);
                }
                return names.ToList();
            }

            public void Run()
            {
                for (int k = 0; k < m_Tokens.Count; k++)
                {
                    Token token = m_Tokens[k];
                    bool member = At(k - 1)?.Is(".") == true;
                    if (token.Kind == Kind.Var && !member)
                        CheckVariable(k);
                    else if (token.Kind == Kind.Name && !member && At(k + 1) != null)
                    {
                        CheckCondition(k);
                        CheckName(k);
                    }
                    else if (token.Kind == Kind.Symbol && Comparisons.Contains(token.Text))
                    {
                        CheckComparison(k);
                        CheckDomainLiteral(k);
                    }
                }
                CheckStandards();
            }

            /// <summary>Una variable: si está definida, sus métodos y, si empieza la instrucción, la asignación.</summary>
            void CheckVariable(int k)
            {
                Token token = m_Tokens[k];
                string name = token.Text.Substring(1);
                TypeInfo type = m_Context.Variable(name);
                if (type == null)
                {
                    if (!Standard.Contains(name))
                        Add(Severity.Warning, Undefined, $"La variable {token.Text} no está definida. ¿Está mal escrito el nombre?", token, token)
                            .Fixes.Add(DeclareFix(token.Text, token.Line, token.Column));
                    return;
                }

                TypeInfo target = Chain(k, type, report: true, out int end);

                // "&Variable = ..." o "&Sdt.Miembro = ..." (no un método: "&x.Metodo() = " no es una asignación)
                if (IsStatementStart(k) && At(end + 1)?.Is("=") == true && !m_Tokens[end].Is(")"))
                {
                    List<Token> value = Statement(end + 2);
                    TypeInfo valueType = TermType(value);
                    if (value.Count > 0 && TypeInfo.Mismatch(target, valueType))
                        Add(Severity.Error, AssignMismatch,
                            $"Los tipos no coinciden: {Quote(m_Tokens.GetRange(k, end - k + 1))} es {target} y {Quote(value)} es {valueType}.", value[0], value[value.Count - 1]);
                    else if (value.Count == 1 && value[0].Kind == Kind.String && target.Group == TypeGroup.Text && target.Length > 0 && !target.IsCollection)
                    {
                        int length = LiteralLength(value[0].Text);
                        if (length > target.Length)
                            Add(Severity.Warning, Truncated,
                                $"El texto tiene {length} caracteres y {Quote(m_Tokens.GetRange(k, end - k + 1))} guarda {target.Length} ({target}): GeneXus lo corta.", value[0], value[0]);
                    }

                    // Un parámetro in: del propio objeto
                    CallParameter own = end == k ? m_Context.Own()?.Parameters.FirstOrDefault(p => string.Equals(p.Name, token.Text, StringComparison.OrdinalIgnoreCase)) : null;
                    if (own != null && own.Direction == ParameterDirection.In)
                        Add(Severity.Warning, InputAssigned,
                            $"{token.Text} es un parámetro de entrada (in:) y no se debería asignar. Si tiene que devolver un valor, declaralo inout: u out: en el parm().", token, token);
                }
            }

            /// <summary>
            /// Recorre ".Metodo(...)" y ".Miembro" desde el token <paramref name="k"/>, con lo que devuelve cada uno.
            /// Devuelve el tipo final y en <paramref name="end"/> el índice del último token de la cadena.
            /// </summary>
            TypeInfo Chain(int k, TypeInfo type, bool report, out int end)
            {
                end = k;
                int j = k + 1;
                while (At(j)?.Is(".") == true && At(j + 1)?.Kind == Kind.Name)
                {
                    Token name = At(j + 1);
                    if (At(j + 2)?.Is("(") == true)
                    {
                        List<List<Token>> args = Arguments(j + 2, out int close);
                        if (args == null)
                            return TypeInfo.Unknown;
                        Method method = FindMethod(type, name.Text);
                        if (report)
                            CheckMethodStandard(Quote(m_Tokens.GetRange(k, j - k)), type, name, At(j), At(close));
                        if (report && method != null && method.Checked)
                            CheckArguments(name.Text + "()", method.Parameters.Select(p => (string)null).ToList(), method.Parameters, method.Min, args, name, At(close), returns: false);
                        else if (report && method == null)
                            CheckMethodName(type, name);
                        type = method?.Returns ?? TypeInfo.Unknown;
                        end = close;
                        j = close + 1;
                    }
                    else
                    {
                        type = MemberType(type, name, report);
                        end = j + 1;
                        j += 2;
                    }
                }
                return type;
            }

            /// <summary>Un miembro: de un SDT, una propiedad de un control, Count de una colección...</summary>
            TypeInfo MemberType(TypeInfo owner, Token name, bool report)
            {
                if (owner.IsCollection)
                    return name.IsWord("Count") ? TypeInfo.Number : TypeInfo.Unknown;
                if (owner.Group != TypeGroup.Sdt && owner.Group != TypeGroup.Bc)
                    return TypeInfo.Unknown;
                TypeInfo member = m_Context.Member(owner, name.Text, out bool missing);
                if (member == null && !missing && owner.Group == TypeGroup.Sdt)
                    Unresolved.Add(owner.Display + "." + name.Text);
                if (missing && report)
                {
                    string suggestion = Explainer.Closest(name.Text, m_Context.Members(owner));
                    Diagnostic d = Add(Severity.Error, MissingMember,
                        $"{owner.Display} no tiene el miembro '{name.Text}'." + (suggestion != null ? $" ¿Quisiste decir '{suggestion}'?" : ""), name, name);
                    if (suggestion != null)
                        d.Replacing($"Cambiar por '{suggestion}'", name.Text, suggestion);
                }
                return member ?? TypeInfo.Unknown;
            }

            static Method FindMethod(TypeInfo type, string name)
            {
                if (type.IsCollection)
                    return CollectionMethod(type, name);
                if (type.Group == TypeGroup.Sdt || type.Group == TypeGroup.Bc)
                    return StructureMethods.TryGetValue(name, out Method structure) ? structure : null;
                if (!type.IsKnown || !Methods.TryGetValue(type.Group, out Dictionary<string, Method> methods))
                    return null;
                return methods.TryGetValue(name, out Method method) ? method : null;
            }

            static Method CollectionMethod(TypeInfo collection, string name)
            {
                TypeInfo item = collection.Element();
                switch (name.ToLowerInvariant())
                {
                    case "add":
                        return new Method { Parameters = new[] { item, TypeInfo.Number }, Minimum = 1 };
                    case "addrange":
                        return new Method { Parameters = new[] { collection, TypeInfo.Number }, Minimum = 1 };
                    case "item":
                        return new Method { Parameters = new[] { TypeInfo.Number }, Returns = item };
                    case "remove":
                        return new Method { Parameters = new[] { TypeInfo.Number } };
                    case "clear":
                        return new Method();
                    case "indexof":
                        return new Method { Parameters = new[] { item }, Returns = TypeInfo.Number };
                    case "sort":
                        return new Method { Parameters = new[] { TypeInfo.Text }, Minimum = 0 };
                    case "tojson":
                    case "toxml":
                        return new Method { Returns = TypeInfo.Text, Checked = false };
                    case "fromjson":
                    case "fromxml":
                        return new Method { Returns = TypeInfo.Bool, Checked = false };
                    default:
                        return CollectionMethodNames.Contains(name) ? new Method { Checked = false } : null;
                }
            }

            /// <summary>Un método que no está en las tablas: si se parece mucho a uno que sí, probablemente está mal escrito.</summary>
            void CheckMethodName(TypeInfo type, Token name)
            {
                IEnumerable<string> known;
                if (type.IsCollection)
                    known = CollectionMethodNames;
                else if (type.IsKnown && Methods.TryGetValue(type.Group, out Dictionary<string, Method> methods))
                    known = methods.Keys;
                else
                    return;
                string suggestion = Explainer.Closest(name.Text, known);
                if (suggestion != null)
                    Add(Severity.Warning, UnknownMethod, $"¿Quisiste decir '{suggestion}'? {type} no tiene el método '{name.Text}'.", name, name)
                        .Replacing($"Cambiar por '{suggestion}'", name.Text, suggestion);
            }

            /// <summary>
            /// Un nombre: seguido de "(", una llamada a un procedimiento (con .Call/.Udp/.Submit o sin); con puntos
            /// y sin paréntesis, puede ser el valor de un dominio enumerado (Dominio.VALOR).
            /// </summary>
            void CheckName(int k)
            {
                if (!Callee(k, out string objectName, out bool udp, out int open))
                {
                    CheckDomainValue(k);
                    return;
                }
                CallSignature signature = m_Context.Procedure(objectName);
                if (signature == null)
                {
                    CheckExists(k, objectName, open);
                    return;
                }
                List<List<Token>> args = Arguments(open, out int close);
                if (args == null)
                    return;
                var parameters = signature.Parameters;
                // Usado como función, el último parámetro es lo que devuelve
                if (udp && parameters.Count == 0)
                    return;
                if (udp && parameters[parameters.Count - 1].Direction == ParameterDirection.In)
                {
                    CallParameter last = parameters[parameters.Count - 1];
                    Add(Severity.Error, NoReturn,
                        $"{signature.Name} usado como función devuelve su último parámetro, pero {last.Name} es de entrada (in:): no devuelve nada. Llamalo con .Call() o declaralo out: en su parm().", m_Tokens[k], m_Tokens[k]);
                    return;
                }
                var expected = udp ? parameters.Take(parameters.Count - 1).ToList() : parameters;
                CheckArguments(signature.Name, expected.Select(p => p.Name).ToList(), expected.Select(p => p.Type).ToArray(), expected.Count, args, m_Tokens[k], At(close), returns: udp);
            }

            /// <summary>
            /// "Objeto.Call(...)", ".Udp(" o ".Submit(" a un objeto que no está en la KB: el build falla (GeneXus no
            /// lo encuentra al especificar). Sin .Call puede ser una función o un método externo: no se juzga.
            /// </summary>
            void CheckExists(int k, string objectName, int open)
            {
                Token method = At(open - 1);
                if (open - k < 3 || At(open - 2)?.Is(".") != true || method == null
                    || !(method.IsWord("Call") || method.IsWord("Udp") || method.IsWord("Submit")))
                    return;
                if (m_Context.ObjectExists(objectName))
                    return;
                Add(Severity.Error, NotFound,
                    $"No existe el objeto '{objectName}' (o no se ve desde este módulo): el build va a fallar. ¿Está mal escrito el nombre?", m_Tokens[k], At(open - 3));
            }

            void CheckDomainValue(int k)
            {
                List<string> parts = DottedName(k, out int last);
                if (parts.Count < 2 || At(last + 1)?.Is("(") == true)
                    return;
                EnumDomain domain = m_Context.Domain(string.Join(".", parts.Take(parts.Count - 1)));
                string value = parts[parts.Count - 1];
                if (domain?.Values == null || domain.Values.Contains(value, StringComparer.OrdinalIgnoreCase))
                    return;
                string suggestion = Explainer.Closest(value, domain.Values);
                Diagnostic d = Add(Severity.Error, InvalidValue,
                    $"El dominio {domain.Name} no tiene el valor '{value}'." + (suggestion != null ? $" ¿Quisiste decir '{suggestion}'?" : ""), At(last), At(last));
                if (suggestion != null)
                    d.Replacing($"Cambiar por '{suggestion}'", value, suggestion);
            }

            /// <summary>If, Do While y Case (de un Do Case): la condición tiene que ser verdadera o falsa.</summary>
            void CheckCondition(int k)
            {
                Token token = m_Tokens[k];
                if (!IsStatementStart(k))
                    return;
                int start;
                if (token.IsWord("If") || (token.IsWord("Case") && At(k - 1)?.IsWord("Do") != true))
                    start = k + 1;
                else if (token.IsWord("Do") && At(k + 1)?.IsWord("While") == true)
                    start = k + 2;
                else
                    return;
                if (At(start)?.IsWord("Case") == true)
                    return;
                List<Token> condition = Statement(start);
                if (condition.Count == 0)
                    return;
                TypeInfo type = TermType(condition);
                // Un número no siempre es error
                if (!type.IsKnown || type.Group == TypeGroup.Bool || (type.Group == TypeGroup.Number && !type.IsCollection))
                    return;
                Add(Severity.Error, NotBoolean,
                    $"La condición tiene que ser verdadera o falsa, y {Quote(condition)} es {type}.", condition[0], condition[condition.Count - 1]);
            }

            /// <summary>Caracteres de un literal: sin las comillas, y una comilla doble ('') cuenta como una.</summary>
            static int LiteralLength(string literal)
            {
                if (literal.Length < 2)
                    return 0;
                char quote = literal[0];
                string content = literal.Substring(1, literal.Length - 2);
                return content.Replace(new string(quote, 2), quote.ToString()).Length;
            }

            /// <summary>"A.B.C" desde <paramref name="k"/>: sus partes y en <paramref name="last"/> el índice de la última.</summary>
            List<string> DottedName(int k, out int last)
            {
                var parts = new List<string> { m_Tokens[k].Text };
                last = k;
                while (At(last + 1)?.Is(".") == true && At(last + 2)?.Kind == Kind.Name)
                {
                    parts.Add(At(last + 2).Text);
                    last += 2;
                }
                return parts;
            }

            /// <summary>
            /// Una comparación (=, &lt;&gt;, &lt;, &gt;...) entre tipos que GeneXus no compara. El "=" que empieza
            /// una instrucción es una asignación (o el de un For).
            /// </summary>
            void CheckComparison(int k)
            {
                if (!Operands(k, out int start, out int end))
                    return;
                Token op = m_Tokens[k];
                if (op.Is("=") && (IsStatementStart(start) || At(start - 1)?.IsWord("For") == true))
                    return;
                TypeInfo left = TermType(start, k - 1);
                TypeInfo right = TermType(k + 1, end);
                if (!TypeInfo.Mismatch(left, right) || left.IsCollection || right.IsCollection)
                    return;
                List<Token> leftTokens = m_Tokens.GetRange(start, k - start);
                List<Token> rightTokens = m_Tokens.GetRange(k + 1, end - k);
                Add(Severity.Error, CompareMismatch,
                    $"No se pueden comparar {Quote(leftTokens)} ({left}) y {Quote(rightTokens)} ({right}): son de tipos distintos.", m_Tokens[start], m_Tokens[end]);
            }

            /// <summary>
            /// Los operandos de la comparación (o asignación) de <paramref name="k"/>: desde <paramref name="start"/>
            /// hasta <paramref name="end"/>. False si le falta uno.
            /// </summary>
            bool Operands(int k, out int start, out int end)
            {
                Token op = m_Tokens[k];
                start = k;
                for (int j = k - 1; j >= 0; j--)
                {
                    Token token = m_Tokens[j];
                    if (token.Depth < op.Depth || (token.Depth == op.Depth && IsBoundary(token)))
                        break;
                    start = j;
                    if (IsStatementStart(j))
                        break;
                }
                end = k;
                for (int j = k + 1; j < m_Tokens.Count; j++)
                {
                    Token token = m_Tokens[j];
                    if (token.Depth < op.Depth || (token.Depth == op.Depth && (IsBoundary(token) || token.Is(")") || IsStatementStart(j))))
                        break;
                    end = j;
                }
                return start < k && end > k;
            }

            /// <summary>Lo que corta un operando de una comparación: una coma, otra comparación, and/or o un comando.</summary>
            static bool IsBoundary(Token token) =>
                token.Is(",") || (token.Kind == Kind.Symbol && Comparisons.Contains(token.Text))
                || (token.Kind == Kind.Name && (Logical.Contains(token.Text) || token.IsWord("Not") || Keywords.Contains(token.Text)));

            /// <summary>
            /// Si en <paramref name="k"/> empieza una llamada a un objeto: su nombre (con módulo), si se usa como
            /// función (el último parámetro es el resultado) y dónde abre el paréntesis.
            /// </summary>
            bool Callee(int k, out string objectName, out bool udp, out int open)
            {
                objectName = null;
                udp = false;
                var parts = new List<string> { m_Tokens[k].Text };
                int j = k + 1;
                while (At(j)?.Is(".") == true && At(j + 1)?.Kind == Kind.Name)
                {
                    parts.Add(At(j + 1).Text);
                    j += 2;
                }
                open = j;
                if (At(j)?.Is("(") != true)
                    return false;

                string last = parts[parts.Count - 1];
                if (parts.Count > 1 && (last.Equals("Call", StringComparison.OrdinalIgnoreCase) || last.Equals("Submit", StringComparison.OrdinalIgnoreCase)))
                    parts.RemoveAt(parts.Count - 1);
                else if (parts.Count > 1 && last.Equals("Udp", StringComparison.OrdinalIgnoreCase))
                {
                    parts.RemoveAt(parts.Count - 1);
                    udp = true;
                }
                else
                {
                    if (parts.Count == 1 && (Keywords.Contains(last) || Functions.ContainsKey(last)))
                        return false;
                    // Sola en la instrucción es un Call; dentro de una expresión, una función
                    udp = !IsStatementStart(k);
                }
                objectName = string.Join(".", parts);
                return true;
            }

            /// <summary>
            /// La cantidad y el tipo de los argumentos. Si la cantidad no coincide, los argumentos quedan corridos
            /// respecto del parm() y sus tipos no dicen nada por sí solos: se informa solo la cantidad, con el
            /// primer argumento que no es del tipo de su parámetro como el que probablemente falta o sobra.
            /// </summary>
            void CheckArguments(string callee, List<string> names, TypeInfo[] types, int minimum, List<List<Token>> args, Token at, Token close, bool returns)
            {
                int maximum = types.Length;
                int firstMismatch = -1;
                for (int i = 0; i < Math.Min(args.Count, maximum) && firstMismatch < 0; i++)
                {
                    if (args[i].Count > 0 && TypeInfo.Mismatch(types[i], TermType(args[i])))
                        firstMismatch = i;
                }
                string Parameter(int i) => names[i] != null ? $"{names[i]} ({types[i]})" : types[i].ToString();

                if (args.Count > maximum)
                {
                    string hint = "";
                    if (args.Count == maximum + 1 && firstMismatch >= 0)
                        hint = $" Probablemente sobra el parámetro {firstMismatch + 1} ({Quote(args[firstMismatch])}).";
                    else if (returns && args.Count == maximum + 1)
                        hint = " Usado como función, el último parámetro del parm() es el resultado: asignalo (&x = ...) o llamalo con .Call().";
                    Add(Severity.Error, TooMany, $"Sobran parámetros: {callee} recibe {Count(minimum, maximum)} y tiene {args.Count}.{hint}", at, close);
                    return;
                }
                if (args.Count < minimum)
                {
                    string hint = "";
                    if (args.Count == minimum - 1 && firstMismatch >= 0)
                        hint = $" Probablemente falta el parámetro {firstMismatch + 1}: {Parameter(firstMismatch)}.";
                    else if (!returns && args.Count == minimum - 1 && names.Count > 0 && names[0] != null)
                        hint = " Si el último es el resultado, usalo como función: &x = " + callee + "(...).";
                    Add(Severity.Error, TooFew, $"Faltan parámetros: {callee} recibe {Count(minimum, maximum)} y tiene {args.Count}.{hint}", at, close);
                    return;
                }

                for (int i = 0; i < Math.Min(args.Count, maximum); i++)
                {
                    List<Token> arg = args[i];
                    if (arg.Count == 0)
                        continue;
                    if (names[i] != null)
                        CheckArgumentLiteral(callee, i, names[i], types[i], arg);
                    TypeInfo type = TermType(arg);
                    if (!TypeInfo.Mismatch(types[i], type))
                        continue;
                    string parameter = names[i] != null ? $" ({names[i]})" : "";
                    Add(Severity.Error, WrongType,
                        $"El parámetro {i + 1} de {callee}{parameter} es {types[i]}, y {Quote(arg)} es {type}.", arg[0], arg[arg.Count - 1]);
                }
            }

            static string Count(int minimum, int maximum)
            {
                string Params(int n) => n == 0 ? "ningún parámetro" : n == 1 ? "1 parámetro" : n + " parámetros";
                return minimum == maximum ? Params(maximum) : $"de {minimum} a {Params(maximum)}";
            }

            /// <summary>El tipo de una expresión simple, o Unknown si tiene operadores o algo que no se entiende.</summary>
            TypeInfo TermType(List<Token> tokens)
            {
                if (tokens.Count == 0)
                    return TypeInfo.Unknown;
                int first = tokens[0].Index;
                int last = first + tokens.Count - 1;
                return TermType(first, last);
            }

            TypeInfo TermType(int first, int last)
            {
                if (first > last)
                    return TypeInfo.Unknown;
                Token token = m_Tokens[first];
                if (token.Is("(") && Matching(first) == last)
                    return last - first < 2 ? TypeInfo.Unknown : TermType(first + 1, last - 1);
                TypeInfo expression = ExpressionType(first, last);
                if (expression != null)
                    return expression;
                if (token.IsWord("Not"))
                    return TypeInfo.Bool;
                if (token.Is("-") && first < last && At(first + 1).Kind == Kind.Number && first + 1 == last)
                    return TypeInfo.Number;
                switch (token.Kind)
                {
                    case Kind.String:
                        return first == last ? TypeInfo.Text : TypeInfo.Unknown;
                    case Kind.Number:
                        return first == last ? TypeInfo.Number : TypeInfo.Unknown;
                    case Kind.Var:
                    {
                        TypeInfo type = m_Context.Variable(token.Text.Substring(1));
                        if (type == null)
                            return TypeInfo.Unknown;
                        type = Chain(first, type, report: false, out int end);
                        return end == last ? type : TypeInfo.Unknown;
                    }
                    case Kind.Name:
                    {
                        if (first == last)
                        {
                            if (token.IsWord("true") || token.IsWord("false"))
                                return TypeInfo.Bool;
                            if (Keywords.Contains(token.Text))
                                return TypeInfo.Unknown;
                            return m_Context.Attribute(token.Text) ?? TypeInfo.Unknown;
                        }
                        if (At(first + 1).Is("(") && Functions.TryGetValue(token.Text, out TypeInfo returns))
                            return Matching(first + 1) == last ? returns : TypeInfo.Unknown;
                        // Dominio.VALOR: el tipo del dominio
                        List<string> parts = DottedName(first, out int lastPart);
                        if (parts.Count >= 2 && lastPart == last)
                        {
                            EnumDomain domain = m_Context.Domain(string.Join(".", parts.Take(parts.Count - 1)));
                            if (domain?.Values != null && domain.Values.Contains(parts[parts.Count - 1], StringComparer.OrdinalIgnoreCase))
                                return domain.Type ?? TypeInfo.Unknown;
                            return TypeInfo.Unknown;
                        }
                        // Un procedimiento usado como función devuelve su último parámetro
                        if (Callee(first, out string objectName, out bool udp, out int open) && udp && Matching(open) == last)
                        {
                            CallSignature signature = m_Context.Procedure(objectName);
                            if (signature != null && signature.Parameters.Count > 0)
                                return signature.Parameters[signature.Parameters.Count - 1].Type ?? TypeInfo.Unknown;
                        }
                        return TypeInfo.Unknown;
                    }
                }
                return TypeInfo.Unknown;
            }

            /// <summary>
            /// El tipo de una operación entre términos (a + b, a = b, a and b), o null si no hay operadores. Solo
            /// lo que es seguro: números entre sí, textos sumados, una fecha más o menos días, una comparación.
            /// </summary>
            TypeInfo ExpressionType(int first, int last)
            {
                var operands = new List<(int Start, int End)>();
                var operators = new List<string>();
                int start = first;
                for (int j = first; j <= last; j++)
                {
                    Token token = m_Tokens[j];
                    if (token.Depth != m_Tokens[first].Depth || j == start)
                        continue;
                    bool binary = (token.Kind == Kind.Symbol && (Arithmetic.Contains(token.Text) || Comparisons.Contains(token.Text)))
                        || (token.Kind == Kind.Name && Logical.Contains(token.Text));
                    if (!binary)
                        continue;
                    operands.Add((start, j - 1));
                    operators.Add(token.Text);
                    start = j + 1;
                }
                if (operators.Count == 0)
                    return null;
                operands.Add((start, last));
                if (operands.Any(o => o.Start > o.End))
                    return TypeInfo.Unknown;
                if (operators.Any(o => Comparisons.Contains(o) || Logical.Contains(o)))
                    return TypeInfo.Bool;
                var types = operands.Select(o => TermType(o.Start, o.End)).ToList();
                if (types.Any(t => !t.IsKnown || t.IsCollection))
                    return TypeInfo.Unknown;
                if (types.All(t => t.Group == TypeGroup.Number))
                    return TypeInfo.Number;
                if (types.All(t => t.Group == TypeGroup.Text) && operators.All(o => o == "+"))
                    return TypeInfo.Text;
                if (types[0].Group == TypeGroup.Date && types.Skip(1).All(t => t.Group == TypeGroup.Number) && operators.All(o => o == "+" || o == "-"))
                    return types[0];
                if (types.Count == 2 && types.All(t => t.Group == TypeGroup.Date) && operators[0] == "-")
                    return TypeInfo.Number;
                return TypeInfo.Unknown;
            }

            /// <summary>Índice del paréntesis que cierra al de <paramref name="open"/>, o -1.</summary>
            int Matching(int open)
            {
                int depth = 0;
                for (int j = open; j < m_Tokens.Count; j++)
                {
                    if (m_Tokens[j].Is("("))
                        depth++;
                    else if (m_Tokens[j].Is(")") && --depth == 0)
                        return j;
                }
                return -1;
            }

            /// <summary>Los argumentos entre el paréntesis de <paramref name="open"/> y el que lo cierra, o null si no cierra.</summary>
            List<List<Token>> Arguments(int open, out int close)
            {
                close = Matching(open);
                if (close < 0)
                    return null;
                var result = new List<List<Token>>();
                if (close == open + 1)
                    return result;
                var current = new List<Token>();
                int depth = 0;
                for (int j = open + 1; j < close; j++)
                {
                    Token token = m_Tokens[j];
                    if (token.Is("("))
                        depth++;
                    else if (token.Is(")"))
                        depth--;
                    if (depth == 0 && token.Is(","))
                    {
                        result.Add(current);
                        current = new List<Token>();
                    }
                    else
                    {
                        current.Add(token);
                    }
                }
                result.Add(current);
                return result;
            }

            /// <summary>Si el token empieza una instrucción: primero en su línea y no es la continuación de la anterior.</summary>
            bool IsStatementStart(int k)
            {
                Token token = m_Tokens[k];
                if (!token.StartsLine || token.Depth != 0)
                    return false;
                Token previous = At(k - 1);
                return previous == null || !Continues.Contains(previous.Text);
            }

            /// <summary>Los tokens de la instrucción desde <paramref name="start"/> hasta el fin de línea (o del paréntesis abierto).</summary>
            List<Token> Statement(int start)
            {
                var result = new List<Token>();
                for (int j = start; j < m_Tokens.Count; j++)
                {
                    if (j > start && IsStatementStart(j))
                        break;
                    result.Add(m_Tokens[j]);
                }
                return result;
            }

            string Quote(List<Token> tokens)
            {
                Token first = tokens[0];
                Token last = tokens[tokens.Count - 1];
                string text = first.Line == last.Line
                    ? m_Source.Line(first.Line).Substring(first.Column, last.Column + last.Length - first.Column)
                    : string.Join(" ", tokens.Select(t => t.Text));
                if (text.Length > 40)
                    text = text.Substring(0, 37) + "...";
                return "'" + text + "'";
            }

            Diagnostic Add(Severity severity, string code, string message, Token from, Token to)
            {
                int length = to != null && to.Line == from.Line ? to.Column + to.Length - from.Column : from.Length;
                var diagnostic = new Diagnostic
                {
                    Severity = severity,
                    Origin = Origin.Types,
                    Code = code,
                    Message = message,
                    Note = QuickNote,
                    Line = from.Line,
                    Column = from.Column,
                    Length = Math.Max(1, length),
                };
                Result.Add(diagnostic);
                return diagnostic;
            }
        }

        /// <summary>Declarar la &amp;variable (con GxQuickVar) que está en esa posición.</summary>
        public static Fix DeclareFix(string variable, int line, int column) => new Fix
        {
            Kind = FixKind.DeclareVariable,
            Title = $"Declarar {variable}",
            Line = line,
            Column = column,
            Expected = variable,
        };

        static List<Token> Tokenize(SourceText source)
        {
            var tokens = new List<Token>();
            int depth = 0;
            for (int i = 0; i < source.LineCount; i++)
            {
                string code = source.CodeLine(i);
                string text = source.Line(i);
                bool first = true;
                int c = 0;
                while (c < code.Length)
                {
                    char ch = code[c];
                    if (char.IsWhiteSpace(ch))
                    {
                        c++;
                        continue;
                    }
                    int start = c;
                    Kind kind;
                    if (ch == '&' && c + 1 < code.Length && SourceText.IsNameChar(code[c + 1]))
                    {
                        c++;
                        while (c < code.Length && SourceText.IsNameChar(code[c]))
                            c++;
                        kind = Kind.Var;
                    }
                    else if (char.IsLetter(ch) || ch == '_')
                    {
                        while (c < code.Length && SourceText.IsNameChar(code[c]))
                            c++;
                        kind = Kind.Name;
                    }
                    else if (char.IsDigit(ch))
                    {
                        while (c < code.Length && (char.IsDigit(code[c]) || (code[c] == '.' && c + 1 < code.Length && char.IsDigit(code[c + 1]))))
                            c++;
                        kind = Kind.Number;
                    }
                    else if (ch == '"' || ch == '\'')
                    {
                        // El contenido está tapado: la próxima comilla igual es la que cierra ('' sigue el mismo texto)
                        do
                        {
                            int close = code.IndexOf(ch, c + 1);
                            c = close < 0 ? code.Length : close + 1;
                        } while (c < code.Length && code[c] == ch);
                        kind = Kind.String;
                    }
                    else
                    {
                        string two = c + 1 < code.Length ? code.Substring(c, 2) : "";
                        c += two == "<=" || two == ">=" || two == "<>" || two == "+=" || two == "-=" || two == "*=" || two == "/=" ? 2 : 1;
                        kind = Kind.Symbol;
                    }
                    var token = new Token
                    {
                        Kind = kind,
                        Text = text.Substring(start, c - start),
                        Line = i,
                        Column = start,
                        Length = c - start,
                        StartsLine = first,
                        Depth = depth,
                        Index = tokens.Count,
                    };
                    if (token.Is("("))
                        depth++;
                    else if (token.Is(")") && depth > 0)
                        depth--;
                    tokens.Add(token);
                    first = false;
                }
            }
            return tokens;
        }
    }
}
