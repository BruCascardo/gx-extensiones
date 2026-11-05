namespace GxCodeCheck
{
    /// <summary>Qué hace una corrección: cambia el texto, declara la variable (con GxQuickVar) o pone Commit on exit en No.</summary>
    enum FixKind { Text, DeclareVariable, CommitOnExitNo }

    /// <summary>
    /// Una corrección que se ofrece en el cartel. La posición es la del texto que se revisó (al guardar): al
    /// aplicarla se ubica desde donde está ahora el subrayado, y solo se aplica si ahí sigue estando
    /// <see cref="Expected"/> (si se editó ese lugar, no se toca nada).
    /// </summary>
    sealed class Fix
    {
        public FixKind Kind;
        /// <summary>Lo que se muestra: "Cambiar por 'Nombre'", "Borrar el punto"...</summary>
        public string Title = "";
        public int Line;
        public int Column;
        /// <summary>El texto que se reemplaza (vacío para insertar).</summary>
        public string Expected = "";
        /// <summary>Lo que va en su lugar; un "\n" es un salto de línea (el que use el documento).</summary>
        public string Replacement = "";
        /// <summary>Si la corrección va en otra línea que lo marcado, cómo era esa línea: si cambió, no se aplica.</summary>
        public string LineText;

        public override string ToString() => $"{Kind} L{Line + 1} C{Column + 1} '{Expected}' -> '{Replacement}' ({Title})";

        /// <summary>
        /// Dónde aplicar la corrección en <paramref name="text"/>, sabiendo que lo marcado por
        /// <paramref name="diagnostic"/> empieza ahora en <paramref name="anchor"/>. False si el texto cambió ahí.
        /// </summary>
        public bool TryLocate(string text, Diagnostic diagnostic, int anchor, out int offset, out string replacement)
        {
            offset = -1;
            replacement = Replacement.Replace("\n", text.Contains("\r\n") ? "\r\n" : "\n");
            if (anchor < 0 || anchor > text.Length)
                return false;
            if (Line == diagnostic.Line)
            {
                offset = anchor + Column - diagnostic.Column;
            }
            else
            {
                int lineStart = LineStart(text, anchor);
                for (int delta = Line - diagnostic.Line; delta > 0; delta--)
                {
                    int next = text.IndexOf('\n', lineStart);
                    if (next < 0)
                        return false;
                    lineStart = next + 1;
                }
                for (int delta = Line - diagnostic.Line; delta < 0; delta++)
                {
                    if (lineStart == 0)
                        return false;
                    lineStart = LineStart(text, lineStart - 1);
                }
                if (LineText != null && LineAt(text, lineStart) != LineText)
                    return false;
                offset = lineStart + Column;
            }
            if (offset < 0 || offset + Expected.Length > text.Length || string.CompareOrdinal(text, offset, Expected, 0, Expected.Length) != 0)
                return false;
            // Un nombre tiene que estar entero: "Nombr" no es el comienzo de "Nombre" (ya corregido a mano)
            int after = offset + Expected.Length;
            if (Expected.Length > 0 && SourceText.IsNameChar(Expected[Expected.Length - 1]) && after < text.Length && SourceText.IsNameChar(text[after]))
                return false;
            if (Expected.Length > 0 && SourceText.IsNameChar(Expected[0]) && offset > 0 && (SourceText.IsNameChar(text[offset - 1]) || text[offset - 1] == '&'))
                return false;
            return true;
        }

        static int LineStart(string text, int position) => position <= 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;

        static string LineAt(string text, int lineStart)
        {
            int end = text.IndexOf('\n', lineStart);
            string line = end < 0 ? text.Substring(lineStart) : text.Substring(lineStart, end - lineStart);
            return line.TrimEnd('\r');
        }
    }
}
