using System;
using ActiproSoftware.SyntaxEditor;

namespace GxCodeKeys
{
    /// <summary>
    /// Acceso al texto del documento en su representación interna. SyntaxEditor guarda los saltos
    /// de línea como "\n" (un solo carácter) y los convierte a "\r\n" al leer Document.Text o
    /// GetSubstring sin parámetros; los offsets se cuentan sobre la representación interna, así que
    /// todo se lee con LineTerminator.Newline y se escribe con "\n". Al guardar, GeneXus vuelve a
    /// usar el salto de línea de siempre.
    /// </summary>
    static class DocumentText
    {
        public const string NewLine = "\n";

        public static string Get(Document document, int start, int end)
        {
            if (end <= start)
                return string.Empty;
            return document.GetSubstring(new TextRange(start, end), LineTerminator.Newline);
        }

        public static string LineText(Document document, DocumentLine line) => Get(document, line.StartOffset, line.EndOffset);

        public static int Clamp(Document document, int offset) => Math.Max(0, Math.Min(offset, document.Length));

        public static int LineIndexOf(Document document, int offset) => document.OffsetToPosition(Clamp(document, offset)).Line;

        public static DocumentLine LineOf(Document document, int offset) => document.Lines[LineIndexOf(document, offset)];

        /// <summary>
        /// Ejecuta todas las modificaciones de <paramref name="action"/> como una sola acción de deshacer.
        /// </summary>
        public static void AsSingleUndo(Document document, Action action)
        {
            document.UndoRedo.StartGroup(DocumentModificationType.Custom);
            try
            {
                action();
            }
            finally
            {
                document.UndoRedo.EndGroup();
            }
        }

        /// <summary>Reemplaza un rango (inserta si <paramref name="length"/> es 0, borra si el texto es vacío).</summary>
        public static bool Replace(Document document, int offset, int length, string text)
        {
            const DocumentModificationOptions options = DocumentModificationOptions.CheckReadOnly;
            if (length == 0)
                return string.IsNullOrEmpty(text) || document.InsertText(DocumentModificationType.Custom, offset, text, options);
            if (string.IsNullOrEmpty(text))
                return document.DeleteText(DocumentModificationType.Custom, offset, length, options);
            return document.ReplaceText(DocumentModificationType.Custom, offset, length, text, options);
        }

        // ---- Columnas visuales: un tab avanza hasta el próximo múltiplo de TabSize ----

        public static int VisualColumn(string lineText, int charIndex, int tabSize)
        {
            int column = 0;
            for (int i = 0; i < charIndex && i < lineText.Length; i++)
                column = lineText[i] == '\t' ? (column / tabSize + 1) * tabSize : column + 1;
            return column;
        }

        public static int CharIndexAtVisualColumn(string lineText, int visualColumn, int tabSize)
        {
            int column = 0;
            for (int i = 0; i < lineText.Length; i++)
            {
                int next = lineText[i] == '\t' ? (column / tabSize + 1) * tabSize : column + 1;
                if (next > visualColumn)
                    return visualColumn - column <= next - visualColumn ? i : i + 1;
                column = next;
            }
            return lineText.Length;
        }

        public static int TabSize(Document document) => document.TabSize > 0 ? document.TabSize : 4;

        public static string LeadingWhitespace(string lineText)
        {
            int i = 0;
            while (i < lineText.Length && (lineText[i] == ' ' || lineText[i] == '\t'))
                i++;
            return lineText.Substring(0, i);
        }
    }
}
