using System;
using System.Collections.Generic;
using System.Linq;

namespace GxCodeCheck
{
    /// <summary>
    /// Las reglas del estándar del equipo: lo que GeneXus acepta pero el estándar no (un literal donde va un
    /// valor de dominio, .Clone() en vez de new(), Commit en vez de prcCommit.Call()...). Son advertencias y
    /// cada una se puede apagar en Tools &gt; Options &gt; GxCodeCheck.
    /// </summary>
    static partial class TypeChecker
    {
        public const string DomainLiteral = "gxs001";
        public const string CloneInstead = "gxs002";
        public const string DirectCommit = "gxs003";
        public const string DirectSubmit = "gxs004";
        public const string WhereFunction = "gxs005";
        public const string WhereAnd = "gxs006";
        public const string DomainTrim = "gxs007";
        public const string ElseIf = "gxs008";
        public const string CommitOnExit = "gxs009";

        public const string StandardNote = "Estándar del equipo: esta regla se puede apagar en Tools > Options > GxCodeCheck.";

        /// <summary>Las reglas, con cómo se muestran en las opciones.</summary>
        public static readonly (string Code, string Title)[] StandardRules =
        {
            (DomainLiteral, "Un literal donde va un valor de un dominio enumerado (\"AC\" en vez de domEstado.ACTIVO)"),
            (CloneInstead, ".Clone() en vez de instanciar con new()"),
            (DirectCommit, "Commit o Rollback en vez de prcCommit.Call() o prcRollback.Call()"),
            (DirectSubmit, "Submit directo en vez del procedimiento _submit"),
            (WhereFunction, "Funciones o métodos dentro de un Where"),
            (WhereAnd, "'and' dentro de un Where en vez de otra cláusula Where"),
            (DomainTrim, ".Trim() sobre un dominio enumerado"),
            (ElseIf, "If dentro del Else en vez de Do Case"),
            (CommitOnExit, "Procedimiento con Commit on exit en Yes"),
        };

        public const string CommitProcedure = "prcCommit";
        public const string RollbackProcedure = "prcRollback";
        public const string SubmitSuffix = "_submit";

        /// <summary>La advertencia de Commit on exit en Yes, en la primera línea con código.</summary>
        public static Diagnostic CommitOnExitWarning(SourceText source)
        {
            int line = 0;
            while (line < source.LineCount - 1 && source.CodeExtent(line) == null)
                line++;
            var extent = source.CodeExtent(line);
            var diagnostic = new Diagnostic
            {
                Severity = Severity.Warning,
                Origin = Origin.Standard,
                Code = CommitOnExit,
                Message = $"La propiedad Commit on exit de este procedimiento está en Yes y va en No: el commit lo hace quien llama, con {CommitProcedure}.Call().",
                Note = StandardNote,
                Line = line,
                Column = extent?.Start ?? 0,
                Length = extent == null ? 1 : extent.Value.End - extent.Value.Start + 1,
            };
            diagnostic.Fixes.Add(new Fix { Kind = FixKind.CommitOnExitNo, Title = "Poner Commit on exit en No", Line = line });
            return diagnostic;
        }

        sealed partial class Checker
        {
            // Los If que son parte de una cadena de If dentro del Else, ya informada
            readonly HashSet<int> m_ElseIfs = new HashSet<int>();

            void CheckStandards()
            {
                for (int k = 0; k < m_Tokens.Count; k++)
                {
                    Token token = m_Tokens[k];
                    if (token.Kind != Kind.Name || At(k - 1)?.Is(".") == true)
                        continue;
                    if (IsStatementStart(k) && (token.IsWord("Commit") || token.IsWord("Rollback")) && At(k + 1)?.Is(".") != true)
                    {
                        string procedure = token.IsWord("Commit") ? CommitProcedure : RollbackProcedure;
                        AddStandard(DirectCommit, $"El {token.Text.ToLowerInvariant()} se hace con {procedure}.Call(), no con el comando {token.Text}.", token, token)
                            .Replacing($"Cambiar por {procedure}.Call()", token.Text, procedure + ".Call()");
                    }
                    else if (IsStatementStart(k) && token.IsWord("Where"))
                        CheckWhere(k);
                    else if (IsStatementStart(k) && token.IsWord("If") && !m_ElseIfs.Contains(k))
                        CheckElseIf(k);
                    else
                        CheckSubmit(k);
                }
            }

            /// <summary>Un literal comparado con algo de un dominio enumerado, o asignado: va el valor del dominio.</summary>
            void CheckDomainLiteral(int k)
            {
                if (!Operands(k, out int start, out int end) || At(start - 1)?.IsWord("For") == true)
                    return;
                int literal, first, last;
                if (IsLiteral(k + 1, end))
                    (literal, first, last) = (k + 1, start, k - 1);
                else if (IsLiteral(start, k - 1))
                    (literal, first, last) = (start, k + 1, end);
                else
                    return;
                TypeInfo type = TermType(first, last);
                if (type.Enum == null || type.IsCollection)
                    return;
                string what = Quote(m_Tokens.GetRange(first, last - first + 1));
                ReportLiteral(what, type.Enum, m_Tokens.GetRange(literal, k + 1 == literal ? end - literal + 1 : k - literal));
            }

            void CheckArgumentLiteral(string callee, int index, string name, TypeInfo type, List<Token> arg)
            {
                if (type?.Enum == null || type.IsCollection || !IsLiteral(arg[0].Index, arg[arg.Count - 1].Index))
                    return;
                ReportLiteral($"el parámetro {index + 1} de {callee} ({name})", type.Enum, arg);
            }

            void ReportLiteral(string what, EnumDomain domain, List<Token> literal)
            {
                string text = string.Concat(literal.Select(t => t.Text));
                string name = domain.NameOf(text);
                // Vacío o cero sin ser un valor del dominio: es "sin valor", no un valor escrito a mano
                if (name == null && (EnumDomain.Unquote(text).Length == 0 || text == "0"))
                    return;
                string message = name != null
                    ? $"{Capitalize(what)} es del dominio {domain.Name}: usá {domain.Name}.{name} en vez de {text}."
                    : $"{Capitalize(what)} es del dominio {domain.Name} y {text} no es uno de sus valores" +
                      (domain.Values?.Count > 0 ? $" ({string.Join(", ", domain.Values.Take(6))})." : ".");
                Diagnostic d = AddStandard(DomainLiteral, message, literal[0], literal[literal.Count - 1]);
                string written = Span(literal[0], literal[literal.Count - 1]);
                if (name != null && written != null)
                    d.Replacing($"Cambiar por {domain.Name}.{name}", written, $"{domain.Name}.{name}");
            }

            /// <summary>Un texto o un número solo (o un número negativo).</summary>
            bool IsLiteral(int first, int last)
            {
                if (first > last || first < 0)
                    return false;
                Token token = m_Tokens[first];
                if (first == last)
                    return token.Kind == Kind.String || token.Kind == Kind.Number;
                return first + 1 == last && token.Is("-") && m_Tokens[last].Kind == Kind.Number;
            }

            /// <summary>.Clone() (va new()) y .Trim() sobre un dominio enumerado.</summary>
            void CheckMethodStandard(string owner, TypeInfo type, Token method, Token dot, Token close)
            {
                if (method.IsWord("Clone"))
                {
                    AddStandard(CloneInstead, "Para tener uno nuevo, instanciá con new() (&x = new()) antes de cargarlo, en vez de .Clone().", method, method);
                }
                else if (method.IsWord("Trim") && type.Enum != null && !type.IsCollection)
                {
                    Diagnostic d = AddStandard(DomainTrim, $"{owner} es del dominio {type.Enum.Name}: no lleva .Trim() (al asignarlo de vuelta compara un dominio con un Character y da error).", method, method);
                    string call = Span(dot, close);
                    if (call != null && dot.Line == method.Line)
                        d.Fixes.Add(new Fix { Title = "Sacar el .Trim()", Line = dot.Line, Column = dot.Column, Expected = call });
                }
            }

            /// <summary>"Proc.Submit(...)": el submit va por el procedimiento Proc_submit, que maneja el commit, el log y el aviso.</summary>
            void CheckSubmit(int k)
            {
                List<string> parts = DottedName(k, out int last);
                if (parts.Count < 2 || !m_Tokens[last].IsWord("Submit") || At(last + 1)?.Is("(") != true)
                    return;
                string procedure = string.Join(".", parts.Take(parts.Count - 1));
                if (procedure.EndsWith(SubmitSuffix, StringComparison.OrdinalIgnoreCase))
                    return;
                AddStandard(DirectSubmit,
                    $"No se hace submit directo de {procedure}: creá {procedure}{SubmitSuffix} (con los mismos parámetros: lo ejecuta, hace {CommitProcedure} o {RollbackProcedure}, escribe el log y avisa el resultado) y llamá {procedure}{SubmitSuffix}.Submit(...).",
                    m_Tokens[last], m_Tokens[last]);
            }

            /// <summary>
            /// Una cláusula Where (hasta su When): sin funciones ni métodos (el valor se calcula antes en una
            /// variable) y una condición por Where, en vez de unirlas con 'and'.
            /// </summary>
            void CheckWhere(int k)
            {
                List<Token> clause = Statement(k + 1);
                int when = clause.FindIndex(t => t.Depth == m_Tokens[k].Depth && t.IsWord("When"));
                if (when >= 0)
                    clause = clause.Take(when).ToList();
                int depth = m_Tokens[k].Depth;
                bool or = clause.Any(t => t.Depth == depth && t.IsWord("Or"));
                for (int i = 0; i < clause.Count; i++)
                {
                    Token token = clause[i];
                    Token next = i + 1 < clause.Count ? clause[i + 1] : null;
                    if (token.Kind == Kind.Name && next?.Is("(") == true && !Keywords.Contains(token.Text))
                    {
                        bool method = i > 0 && clause[i - 1].Is(".");
                        AddStandard(WhereFunction,
                            $"No van {(method ? "métodos" : "funciones")} dentro de un Where ({token.Text}()): calculá el valor antes en una variable (&x = ...) y usá la variable.",
                            token, token);
                        // Lo de adentro ya queda en la variable
                        int close = Matching(next.Index);
                        while (i + 1 < clause.Count && clause[i + 1].Index <= close)
                            i++;
                    }
                    else if (!or && token.Depth == depth && token.IsWord("And"))
                    {
                        Diagnostic d = AddStandard(WhereAnd, "Una condición por Where: en vez de 'and', poné la otra condición en otra cláusula Where.", token, token);
                        // " and " pasa a ser un salto de línea y otro Where, con la sangría del primero
                        Token previous = i > 0 ? clause[i - 1] : null;
                        // Con un When, que vale para toda la cláusula, separarla cambiaría lo que hace
                        if (when < 0 && previous != null && next != null && previous.Line == token.Line && next.Line == token.Line)
                        {
                            string line = m_Source.Line(token.Line);
                            int from = previous.Column + previous.Length;
                            string where = m_Source.Line(m_Tokens[k].Line);
                            string indent = where.Substring(0, where.Length - where.TrimStart(' ', '\t').Length);
                            d.Fixes.Add(new Fix
                            {
                                Title = "Pasar la condición a otra cláusula Where",
                                Line = token.Line,
                                Column = from,
                                Expected = line.Substring(from, next.Column - from),
                                Replacement = "\n" + indent + "Where ",
                            });
                        }
                    }
                }
            }

            /// <summary>If ... Else If ... EndIf EndIf: una cadena de condiciones, que va con Do Case.</summary>
            void CheckElseIf(int k)
            {
                int count = 0;
                int current = k;
                while (true)
                {
                    int end = BlockEnd(current, out int elseAt);
                    if (end < 0 || elseAt < 0 || At(elseAt + 1)?.IsWord("If") != true || !IsStatementStart(elseAt + 1))
                        break;
                    int inner = elseAt + 1;
                    // El If tiene que ser lo único dentro del Else
                    if (BlockEnd(inner, out _) + 1 != end)
                        break;
                    m_ElseIfs.Add(inner);
                    count++;
                    current = inner;
                }
                if (count > 0)
                    AddStandard(ElseIf,
                        $"Este If sigue con {(count == 1 ? "otro If" : count + " If")} dentro del Else: una cadena de condiciones va con Do Case (Case ... Otherwise ... EndCase).",
                        m_Tokens[k], m_Tokens[k]);
            }

            /// <summary>El EndIf del If de <paramref name="k"/> (o -1) y su Else (o -1).</summary>
            int BlockEnd(int k, out int elseAt)
            {
                elseAt = -1;
                int depth = 1;
                for (int j = k + 1; j < m_Tokens.Count; j++)
                {
                    Token token = m_Tokens[j];
                    if (!IsStatementStart(j))
                        continue;
                    if (token.IsWord("If"))
                        depth++;
                    else if (token.IsWord("EndIf") && --depth == 0)
                        return j;
                    else if (token.IsWord("Else") && depth == 1)
                        elseAt = j;
                }
                return -1;
            }

            static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0]) + text.Substring(1);

            /// <summary>El texto de <paramref name="from"/> a <paramref name="to"/> si están en la misma línea, o null.</summary>
            string Span(Token from, Token to) =>
                from.Line == to.Line ? m_Source.Line(from.Line).Substring(from.Column, to.Column + to.Length - from.Column) : null;

            Diagnostic AddStandard(string code, string message, Token from, Token to)
            {
                int length = to != null && to.Line == from.Line ? to.Column + to.Length - from.Column : from.Length;
                var diagnostic = new Diagnostic
                {
                    Severity = Severity.Warning,
                    Origin = Origin.Standard,
                    Code = code,
                    Message = message,
                    Note = StandardNote,
                    Line = from.Line,
                    Column = from.Column,
                    Length = Math.Max(1, length),
                };
                Result.Add(diagnostic);
                return diagnostic;
            }
        }
    }
}
