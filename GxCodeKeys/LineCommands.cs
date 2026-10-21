using ActiproSoftware.SyntaxEditor;

namespace GxCodeKeys
{
    /// <summary>
    /// Mover líneas (Alt+↑/↓) y duplicarlas (Alt+Shift+↑/↓), sobre la línea del cursor o sobre
    /// todas las líneas que toca la selección. Cada operación es una sola modificación del
    /// documento, dentro de un grupo de deshacer: un Ctrl+Z la revierte completa.
    /// </summary>
    static class LineCommands
    {
        public static void Move(SyntaxEditor syntaxEditor, bool up)
        {
            Document document = syntaxEditor.Document;
            Selection selection = syntaxEditor.SelectedView.Selection;
            GetSelectedLines(document, selection, out int first, out int last);

            DocumentLineCollection lines = document.Lines;
            if (up ? first == 0 : last >= lines.Count - 1)
                return;   // ya está en el borde del documento

            DocumentLine firstLine = lines[first];
            DocumentLine lastLine = lines[last];
            string block = DocumentText.Get(document, firstLine.StartOffset, lastLine.EndOffset);

            int start, end, delta;
            string replacement;
            if (up)
            {
                // [anterior][salto][bloque] → [bloque][salto][anterior]
                DocumentLine previous = lines[first - 1];
                string separator = DocumentText.Get(document, previous.EndOffset, firstLine.StartOffset);
                string previousText = DocumentText.LineText(document, previous);
                start = previous.StartOffset;
                end = lastLine.EndOffset;
                replacement = block + separator + previousText;
                delta = -(previousText.Length + separator.Length);
            }
            else
            {
                // [bloque][salto][siguiente] → [siguiente][salto][bloque]
                DocumentLine next = lines[last + 1];
                string separator = DocumentText.Get(document, lastLine.EndOffset, next.StartOffset);
                string nextText = DocumentText.LineText(document, next);
                start = firstLine.StartOffset;
                end = next.EndOffset;
                replacement = nextText + separator + block;
                delta = nextText.Length + separator.Length;
            }

            // El salto de línea final del documento (si lo hay) queda fuera del rango: la última
            // línea sin salto sigue sin salto, y la indentación viaja con cada línea tal cual.
            ReplaceKeepingSelection(syntaxEditor, start, end - start, replacement, selection, delta);
        }

        public static void Duplicate(SyntaxEditor syntaxEditor, bool up)
        {
            Document document = syntaxEditor.Document;
            Selection selection = syntaxEditor.SelectedView.Selection;
            GetSelectedLines(document, selection, out int first, out int last);

            DocumentLine firstLine = document.Lines[first];
            DocumentLine lastLine = document.Lines[last];
            string block = DocumentText.Get(document, firstLine.StartOffset, lastLine.EndOffset);

            if (up)
            {
                // La copia queda arriba, en los mismos offsets que el original: la selección no se mueve
                ReplaceKeepingSelection(syntaxEditor, firstLine.StartOffset, 0, block + DocumentText.NewLine, selection, 0);
            }
            else
            {
                // La copia queda abajo: la selección se corre al bloque nuevo
                ReplaceKeepingSelection(syntaxEditor, lastLine.EndOffset, 0, DocumentText.NewLine + block, selection,
                                        block.Length + DocumentText.NewLine.Length);
            }
        }

        /// <summary>
        /// Líneas que abarca la selección. Si la selección termina justo al principio de una línea
        /// (por ejemplo, después de seleccionar líneas completas con Shift+↓), esa línea no cuenta.
        /// </summary>
        static void GetSelectedLines(Document document, Selection selection, out int first, out int last)
        {
            int firstOffset = DocumentText.Clamp(document, selection.FirstOffset);
            int lastOffset = DocumentText.Clamp(document, selection.LastOffset);
            first = DocumentText.LineIndexOf(document, firstOffset);
            last = DocumentText.LineIndexOf(document, lastOffset);
            if (last > first && document.Lines[last].StartOffset == lastOffset)
                last--;
        }

        static void ReplaceKeepingSelection(SyntaxEditor syntaxEditor, int offset, int length, string text, Selection selection, int delta)
        {
            Document document = syntaxEditor.Document;
            // Ancla y cursor por separado: se conserva el sentido de la selección y dónde está el cursor
            int anchor = selection.StartOffset;
            int caret = selection.EndOffset;
            SelectionModes mode = selection.Mode == SelectionModes.None ? SelectionModes.ContinuousStream : selection.Mode;

            bool done = false;
            DocumentText.AsSingleUndo(document, () => done = DocumentText.Replace(document, offset, length, text));
            if (!done)
                return;   // rango de solo lectura: el documento no cambió

            selection.SelectRange(new TextRange(DocumentText.Clamp(document, anchor + delta), DocumentText.Clamp(document, caret + delta)), mode);
            syntaxEditor.SelectedView.ScrollToCaret();
        }
    }
}
