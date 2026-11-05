using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GxCodeCheck
{
    /// <summary>
    /// Chequeos propios sobre el texto, para lo que el parser de GeneXus ubica mal: un paréntesis o unas
    /// comillas de más o de menos hacen que el error aparezca líneas más abajo (o que no aparezca), un punto
    /// suelto hace que todo lo que sigue dé error, y una línea que termina en un operador se une con la
    /// siguiente sin dar error.
    /// </summary>
    static class TextChecks
    {
        public const string ExtraClose = "gxc001";
        public const string Unclosed = "gxc002";
        public const string Mismatch = "gxc003";
        public const string UnclosedString = "gxc004";
        public const string UnclosedComment = "gxc005";
        public const string TrailingOperator = "gxc006";
        public const string StrayDot = "gxc009";

        /// <summary>Errores de estructura: los que dejan mal ubicados a los errores que vienen después.</summary>
        public static bool IsStructural(string code) =>
            code == ExtraClose || code == Unclosed || code == Mismatch || code == UnclosedString || code == UnclosedComment
            || code == StrayDot || IsBlock(code);

        /// <summary>Errores después de los cuales GeneXus no se recupera: todo lo que informa más abajo es consecuencia.</summary>
        public static bool IsGlobal(string code) => code == StrayDot;

        public static bool IsBlock(string code) => code == BlockChecks.UnclosedBlock || code == BlockChecks.ExtraEnd;

        // Una instrucción nueva: asignación a una &variable (o a un atributo con "Atributo = ") o un comando
        static readonly Regex StatementStart = new Regex(
            @"^\s*(&[A-Za-z_]\w*(\.\w+)*(\([^)]*\))?\s*=(?!=)|(if|else|endif|for|endfor|do|enddo|case|endcase|otherwise|sub|endsub|event|endevent|msg|return|call|commit|rollback|new|endnew|when|exit|print|delete|where|defined)\b)",
            RegexOptions.IgnoreCase);

        /// <summary>
        /// Los chequeos sobre el texto. Los de estructura (<paramref name="structure"/>) solo cuando GeneXus no
        /// pudo guardar: si guardó, la estructura está bien y no hay que arriesgar un error que no existe. Los
        /// de bloques (<paramref name="blocks"/>) solo en el código de procedimientos y eventos.
        /// </summary>
        public static List<Diagnostic> Run(SourceText source, bool structure, bool blocks)
        {
            var result = new List<Diagnostic>();
            if (structure)
            {
                CheckBrackets(source, result);
                CheckDots(source, result);
                foreach (var (line, column, quote) in source.UnclosedStrings)
                    result.Add(Make(UnclosedString, Severity.Error, line, column, 1,
                        $"Faltan las comillas de cierre: el texto que empieza en esta {quote} no se cierra en la línea."));
                if (source.UnclosedComment.HasValue)
                    result.Add(Make(UnclosedComment, Severity.Error, source.UnclosedComment.Value.Line, source.UnclosedComment.Value.Column, 2,
                        "Este comentario /* no se cierra: falta el */ que lo termina."));
                if (blocks)
                    BlockChecks.Run(source, result);
            }
            CheckTrailingOperators(source, result);
            return result;
        }

        static void CheckBrackets(SourceText source, List<Diagnostic> result)
        {
            var open = new Stack<(char Ch, int Line, int Column)>();
            for (int i = 0; i < source.LineCount; i++)
            {
                string code = source.CodeLine(i);
                for (int c = 0; c < code.Length; c++)
                {
                    char ch = code[c];
                    if (ch == '(' || ch == '[' || ch == '{')
                    {
                        open.Push((ch, i, c));
                    }
                    else if (ch == ')' || ch == ']' || ch == '}')
                    {
                        char expected = OpenerOf(ch);
                        if (open.Count == 0)
                        {
                            result.Add(Make(ExtraClose, Severity.Error, i, c, 1,
                                $"Sobra este '{ch}': no hay ningún '{expected}' abierto antes."));
                        }
                        else if (open.Peek().Ch != expected)
                        {
                            var top = open.Peek();
                            result.Add(Make(Mismatch, Severity.Error, i, c, 1,
                                $"Este '{ch}' cierra el '{top.Ch}' de la línea {top.Line + 1}: falta un '{CloserOf(top.Ch)}' o sobra este '{ch}'."));
                            // Si el de arriba es el que corresponde más abajo, se descarta el que quedó sin cerrar
                            if (HasOpener(open, expected))
                            {
                                while (open.Count > 0 && open.Peek().Ch != expected)
                                    open.Pop();
                                open.Pop();
                            }
                        }
                        else
                        {
                            open.Pop();
                        }
                    }
                }
            }
            foreach (var (ch, line, column) in open)
                result.Add(Make(Unclosed, Severity.Error, line, column, 1,
                    $"Este '{ch}' no se cierra: falta un '{CloserOf(ch)}'."));
        }

        /// <summary>
        /// Un punto que no está pegado entre dos nombres (&amp;Sdt.Campo, Modulo.Objeto, &amp;x.Trim()) ni es
        /// parte de un número. Al final de la línea es el peor caso: GeneXus sigue la instrucción en la línea
        /// siguiente ("&amp;x.If ...") y ya no entiende nada de lo que viene.
        /// </summary>
        static void CheckDots(SourceText source, List<Diagnostic> result)
        {
            for (int i = 0; i < source.LineCount; i++)
            {
                string code = source.CodeLine(i);
                for (int c = 0; c < code.Length; c++)
                {
                    if (code[c] != '.')
                        continue;
                    char previous = c > 0 ? code[c - 1] : ' ';
                    char next = c + 1 < code.Length ? code[c + 1] : ' ';
                    bool joins = SourceText.IsNameChar(previous) || previous == ')' || previous == ']' || previous == '"' || previous == '\'';
                    if ((joins && SourceText.IsNameChar(next)) || char.IsDigit(next))
                        continue;
                    var extent = source.CodeExtent(i);
                    string message;
                    if (extent != null && extent.Value.End == c)
                    {
                        int following = i + 1;
                        while (following < source.LineCount && source.IsBlank(following))
                            following++;
                        var start = source.CodeExtent(following);
                        string word = start == null ? "" : FirstWord(source.CodeLine(following), start.Value.Start);
                        message = word.Length > 0
                            ? $"Sobra el '.' al final de la línea: GeneXus sigue leyendo la línea {following + 1} ('{word}') como parte de esta, y desde ahí no entiende nada de lo que viene. Borralo."
                            : "Sobra el '.' al final de la línea. Borralo.";
                    }
                    else
                    {
                        message = "Sobra este '.': un punto va pegado entre dos nombres, como en &Sdt.Campo o Modulo.Objeto.";
                    }
                    result.Add(Make(StrayDot, Severity.Error, i, c, 1, message).Replacing("Borrar el punto", ".", ""));
                }
            }
        }

        static string FirstWord(string code, int start)
        {
            int end = start;
            while (end < code.Length && (SourceText.IsNameChar(code[end]) || code[end] == '&'))
                end++;
            return end > start ? code.Substring(start, end - start) : code.Substring(start, 1);
        }

        static bool HasOpener(Stack<(char Ch, int Line, int Column)> open, char ch)
        {
            foreach (var item in open)
                if (item.Ch == ch)
                    return true;
            return false;
        }

        static void CheckTrailingOperators(SourceText source, List<Diagnostic> result)
        {
            for (int i = 0; i < source.LineCount; i++)
            {
                var extent = source.CodeExtent(i);
                if (extent == null)
                    continue;
                char last = source.CodeLine(i)[extent.Value.End];
                if (last != '+' && last != '-' && last != '*' && last != '/')
                    continue;
                int next = i + 1;
                while (next < source.LineCount && source.IsBlank(next))
                    next++;
                if (next >= source.LineCount || !StatementStart.IsMatch(source.CodeLine(next)))
                    continue;
                result.Add(Make(TrailingOperator, Severity.Warning, i, extent.Value.End, 1,
                    $"La línea termina en '{last}' y la línea {next + 1} empieza una instrucción nueva: GeneXus las lee como una sola expresión. ¿Sobra el '{last}'?"));
            }
        }

        static char OpenerOf(char close) => close == ')' ? '(' : close == ']' ? '[' : '{';

        static char CloserOf(char open) => open == '(' ? ')' : open == '[' ? ']' : '}';

        static Diagnostic Make(string code, Severity severity, int line, int column, int length, string message) => new Diagnostic
        {
            Severity = severity,
            Origin = Origin.Check,
            Code = code,
            Message = message,
            Line = line,
            Column = column,
            Length = length,
        };
    }
}
