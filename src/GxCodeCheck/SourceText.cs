using System;
using System.Collections.Generic;
using System.Text;

namespace GxCodeCheck
{
    /// <summary>
    /// El código de una parte separado en líneas, con una copia donde los comentarios y el contenido de los
    /// strings están tapados con espacios (las comillas quedan). Sobre esa copia se buscan paréntesis,
    /// operadores y nombres sin confundirse con lo que hay adentro de un texto o un comentario.
    /// </summary>
    sealed class SourceText
    {
        public readonly string[] Lines;
        public readonly string[] Code;

        /// <summary>Strings que no se cierran en su línea: línea y columna de la comilla que abre.</summary>
        public readonly List<(int Line, int Column, char Quote)> UnclosedStrings = new List<(int, int, char)>();

        /// <summary>Línea y columna donde empieza un comentario /* que no se cierra, o null.</summary>
        public (int Line, int Column)? UnclosedComment;

        public SourceText(string text)
        {
            Lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            Code = new string[Lines.Length];

            bool inBlockComment = false;
            for (int i = 0; i < Lines.Length; i++)
            {
                string line = Lines[i];
                var code = new StringBuilder(line);
                char quote = '\0';
                int quoteColumn = 0;
                for (int c = 0; c < line.Length; c++)
                {
                    char ch = line[c];
                    char next = c + 1 < line.Length ? line[c + 1] : '\0';
                    if (inBlockComment)
                    {
                        code[c] = ' ';
                        if (ch == '*' && next == '/')
                        {
                            code[c + 1] = ' ';
                            c++;
                            inBlockComment = false;
                        }
                    }
                    else if (quote != '\0')
                    {
                        if (ch == quote)
                            quote = '\0';
                        else
                            code[c] = ' ';
                    }
                    else if (ch == '"' || ch == '\'')
                    {
                        quote = ch;
                        quoteColumn = c;
                    }
                    else if (ch == '/' && next == '/')
                    {
                        for (int k = c; k < line.Length; k++)
                            code[k] = ' ';
                        break;
                    }
                    else if (ch == '/' && next == '*')
                    {
                        code[c] = ' ';
                        code[c + 1] = ' ';
                        c++;
                        inBlockComment = true;
                        UnclosedComment = (i, c - 1);
                    }
                }
                // Los strings de GeneXus no siguen en la línea siguiente
                if (quote != '\0')
                    UnclosedStrings.Add((i, quoteColumn, quote));
                Code[i] = code.ToString();
            }
            if (!inBlockComment)
                UnclosedComment = null;
        }

        public int LineCount => Lines.Length;

        public string Line(int index) => index >= 0 && index < Lines.Length ? Lines[index] : "";

        public string CodeLine(int index) => index >= 0 && index < Code.Length ? Code[index] : "";

        /// <summary>Si la línea no tiene código (vacía, solo comentario o espacios).</summary>
        public bool IsBlank(int index) => CodeLine(index).Trim().Length == 0;

        /// <summary>
        /// Largo de lo que hay en la columna: un nombre (&amp;variable, atributo, número), un string entero o un
        /// símbolo. Al final de la línea, o sobre un espacio, marca el carácter anterior que no sea espacio.
        /// </summary>
        public int TokenLength(int line, int column)
        {
            string text = Line(line);
            if (text.Length == 0)
                return 0;
            if (column >= text.Length || char.IsWhiteSpace(text[column]))
                return 0;
            char ch = text[column];
            if (ch == '"' || ch == '\'')
            {
                int close = text.IndexOf(ch, column + 1);
                return close < 0 ? text.Length - column : close - column + 1;
            }
            if (IsNameChar(ch) || ch == '&')
            {
                int end = column + 1;
                while (end < text.Length && IsNameChar(text[end]))
                    end++;
                return end - column;
            }
            return 1;
        }

        /// <summary>Primera y última columna con código de la línea (sin sangría ni comentario final), o null.</summary>
        public (int Start, int End)? CodeExtent(int line)
        {
            string code = CodeLine(line);
            int start = 0;
            while (start < code.Length && char.IsWhiteSpace(code[start]))
                start++;
            int end = code.Length - 1;
            while (end >= start && char.IsWhiteSpace(code[end]))
                end--;
            return start > end ? ((int, int)?)null : (start, end);
        }

        /// <summary>Columna donde aparece la palabra en el código de la línea (sin distinguir mayúsculas), o -1.</summary>
        public int FindWord(int line, string word, int from = 0)
        {
            if (string.IsNullOrEmpty(word))
                return -1;
            string code = CodeLine(line);
            for (int at = code.IndexOf(word, Math.Max(0, from), StringComparison.OrdinalIgnoreCase); at >= 0;
                 at = code.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase))
            {
                bool startOk = at == 0 || !IsNameChar(code[at - 1]) || word[0] == '&';
                int after = at + word.Length;
                bool endOk = after >= code.Length || !IsNameChar(code[after]);
                if (startOk && endOk)
                    return at;
            }
            return -1;
        }

        public static bool IsNameChar(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
    }
}
