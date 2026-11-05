using System;
using System.Collections.Generic;
using System.Linq;

namespace GxCodeCheck
{
    /// <summary>
    /// Que cada bloque (For, If, Do While, Do Case, Sub, Event, New) tenga su End. GeneXus avisa que falta un
    /// EndFor en cualquier lado (donde se dio cuenta, a veces al final del Sub) y además da errores de
    /// subrutinas "no definidas" que en realidad quedaron adentro del bloque abierto. Acá se marca el bloque
    /// que no se cierra; cuando hay varios anidados, la sangría dice cuál es.
    /// </summary>
    static class BlockChecks
    {
        public const string UnclosedBlock = "gxc007";
        public const string ExtraEnd = "gxc008";

        sealed class Kind
        {
            public string Opener;
            public string Closer;
        }

        static readonly Kind For = new Kind { Opener = "For", Closer = "EndFor" };
        static readonly Kind If = new Kind { Opener = "If", Closer = "EndIf" };
        static readonly Kind DoWhile = new Kind { Opener = "Do While", Closer = "EndDo" };
        static readonly Kind DoCase = new Kind { Opener = "Do Case", Closer = "EndCase" };
        static readonly Kind Sub = new Kind { Opener = "Sub", Closer = "EndSub" };
        static readonly Kind Event = new Kind { Opener = "Event", Closer = "EndEvent" };
        static readonly Kind New = new Kind { Opener = "New", Closer = "EndNew" };

        static readonly Dictionary<string, Kind> Closers = new Dictionary<string, Kind>(StringComparer.OrdinalIgnoreCase)
        {
            ["endfor"] = For, ["endif"] = If, ["enddo"] = DoWhile, ["endcase"] = DoCase,
            ["endsub"] = Sub, ["endevent"] = Event, ["endnew"] = New,
        };

        sealed class Open
        {
            public Kind Kind;
            public int Line;
            public int Column;
            public int Length;
            public int Indent;
        }

        public static void Run(SourceText source, List<Diagnostic> result)
        {
            var stack = new List<Open>();
            for (int i = 0; i < source.LineCount; i++)
            {
                var extent = source.CodeExtent(i);
                if (extent == null)
                    continue;
                string code = source.CodeLine(i);
                int column = extent.Value.Start;
                string first = Word(code, column, out int firstEnd);
                string second = Word(code, Skip(code, firstEnd), out int secondEnd);
                int indent = Indent(source.Line(i));

                if (Closers.TryGetValue(first, out Kind closed))
                {
                    Close(source, stack, closed, i, column, first.Length, indent, result);
                    continue;
                }
                Kind opened = Opener(first, second, out bool twoWords);
                if (opened == null)
                    continue;
                // Un Sub o un Event nuevo: lo que quedó abierto del anterior no se cerró
                if (opened == Sub || opened == Event)
                {
                    foreach (Open open in stack.AsEnumerable().Reverse())
                        result.Add(Unclosed(source, open));
                    stack.Clear();
                }
                stack.Add(new Open
                {
                    Kind = opened,
                    Line = i,
                    Column = column,
                    Length = (twoWords ? secondEnd : firstEnd) - column,
                    Indent = indent,
                });
            }
            foreach (Open open in stack.AsEnumerable().Reverse())
                result.Add(Unclosed(source, open));
        }

        static void Close(SourceText source, List<Open> stack, Kind kind, int line, int column, int length, int indent, List<Diagnostic> result)
        {
            int match = stack.FindLastIndex(o => o.Kind == kind);
            if (match < 0)
            {
                result.Add(Make(ExtraEnd, line, column, length, $"Sobra este '{kind.Closer}': no hay un '{kind.Opener}' abierto que cierre."));
                return;
            }
            // Si la sangría no coincide con el más cercano pero sí con uno de más afuera, cierra ese:
            // al de adentro es al que le falta el End
            if (stack[match].Indent != indent)
            {
                int byIndent = stack.FindLastIndex(match, o => o.Kind == kind && o.Indent == indent);
                if (byIndent >= 0)
                    match = byIndent;
            }
            for (int k = stack.Count - 1; k > match; k--)
                result.Add(Unclosed(source, stack[k]));
            stack.RemoveRange(match, stack.Count - match);
        }

        static Kind Opener(string first, string second, out bool twoWords)
        {
            twoWords = false;
            switch (first.ToLowerInvariant())
            {
                case "for":
                    twoWords = second.Equals("each", StringComparison.OrdinalIgnoreCase);
                    return For;
                case "if":
                    return If;
                case "do":
                    // Do 'Subrutina' es una llamada, no un bloque
                    twoWords = true;
                    return second.Equals("while", StringComparison.OrdinalIgnoreCase) ? DoWhile
                        : second.Equals("case", StringComparison.OrdinalIgnoreCase) ? DoCase : null;
                case "sub":
                    return Sub;
                case "event":
                    return Event;
                case "new":
                    return New;
            }
            return null;
        }

        static Diagnostic Unclosed(SourceText source, Open open)
        {
            string message = $"Este '{open.Kind.Opener}' no tiene su '{open.Kind.Closer}'.";
            int hint = WhereEndGoes(source, open);
            if (hint >= 0)
                message += $" Por la sangría, el '{open.Kind.Closer}' iría antes de la línea {hint + 1}.";
            Diagnostic d = Make(UnclosedBlock, open.Line, open.Column, open.Length, message);
            if (hint >= 0)
            {
                // Una línea nueva con el End, con la sangría del bloque
                string opener = source.Line(open.Line);
                string indent = opener.Substring(0, opener.Length - opener.TrimStart(' ', '\t').Length);
                d.Fixes.Add(new Fix
                {
                    Title = $"Agregar '{open.Kind.Closer}' antes de la línea {hint + 1}",
                    Line = hint,
                    Column = 0,
                    Replacement = indent + open.Kind.Closer + "\n",
                    LineText = source.Line(hint),
                });
            }
            return d;
        }

        /// <summary>La primera línea con código que vuelve a la sangría del bloque (sin ser su Else o su Case), o -1.</summary>
        static int WhereEndGoes(SourceText source, Open open)
        {
            for (int i = open.Line + 1; i < source.LineCount; i++)
            {
                var extent = source.CodeExtent(i);
                if (extent == null)
                    continue;
                if (Indent(source.Line(i)) > open.Indent)
                    continue;
                string first = Word(source.CodeLine(i), extent.Value.Start, out _).ToLowerInvariant();
                bool sameBlock = (open.Kind == If && first == "else")
                    || (open.Kind == DoCase && (first == "case" || first == "otherwise"))
                    || (open.Kind == For && (first == "where" || first == "order" || first == "when" || first == "defined" || first == "unique"));
                if (!sameBlock)
                    return i;
            }
            return -1;
        }

        static string Word(string code, int start, out int end)
        {
            end = start;
            while (end < code.Length && SourceText.IsNameChar(code[end]))
                end++;
            return code.Substring(start, end - start);
        }

        static int Skip(string code, int at)
        {
            while (at < code.Length && char.IsWhiteSpace(code[at]))
                at++;
            return at;
        }

        /// <summary>Sangría en columnas, con el tab de 4.</summary>
        static int Indent(string line)
        {
            int indent = 0;
            foreach (char ch in line)
            {
                if (ch == '\t')
                    indent += 4 - indent % 4;
                else if (ch == ' ')
                    indent++;
                else
                    break;
            }
            return indent;
        }

        static Diagnostic Make(string code, int line, int column, int length, string message) => new Diagnostic
        {
            Severity = Severity.Error,
            Origin = Origin.Check,
            Code = code,
            Message = message,
            Line = line,
            Column = column,
            Length = Math.Max(1, length),
        };
    }
}
