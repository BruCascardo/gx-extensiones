using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using ActiproSoftware.SyntaxEditor;

namespace GxCodeCheck
{
    /// <summary>
    /// Los subrayados en el documento de un SyntaxEditor, con los <see cref="Diagnostic"/> en el Tag de cada uno
    /// (una lista: los que caen en el mismo lugar van en el mismo subrayado).
    /// Los errores (lo que no deja guardar o hace fallar el build) se ven fuerte: onda doble roja, fondo rojo
    /// en lo que está mal, la línea entera teñida y un círculo rojo en el margen de indicadores. Las
    /// advertencias, una onda naranja; las consecuencias de otro error, una línea punteada gris.
    /// </summary>
    static class Squiggles
    {
        public const string LayerKey = "GxCodeCheck";
        // La línea entera va en otra capa, por debajo: dentro de una capa los indicadores no se superponen
        const string LineLayerKey = "GxCodeCheck.Lineas";
        const int LayerPriority = SpanIndicatorLayer.SyntaxErrorDisplayPriority + 10;
        const int LineLayerPriority = SpanIndicatorLayer.ReadOnlyDisplayPriority + 1;

        static readonly Color ErrorColor = Color.FromArgb(0xE5, 0x14, 0x00);
        static readonly Color WarningColor = Color.FromArgb(0xE0, 0x9B, 0x00);
        static readonly Color InfoColor = Color.FromArgb(0x8A, 0x96, 0xA8);

        /// <summary>Reemplaza los subrayados del editor por los de <paramref name="diagnostics"/>.</summary>
        public static void Apply(SyntaxEditor editor, IEnumerable<Diagnostic> diagnostics, bool showWarnings)
        {
            SpanIndicatorLayer layer = Layer(editor, LayerKey, LayerPriority);
            SpanIndicatorLayer lines = Layer(editor, LineLayerKey, LineLayerPriority);
            layer.Clear();
            lines.Clear();
            bool dark = editor.BackColor.GetBrightness() < 0.5f;
            Color spanBack = dark ? Color.FromArgb(0x6E, 0x1E, 0x1E) : Color.FromArgb(0xFF, 0xC4, 0xC4);
            Color lineBack = dark ? Color.FromArgb(0x3C, 0x1A, 0x1A) : Color.FromArgb(0xFF, 0xEC, 0xEC);
            Document document = editor.Document;
            var errorLines = new HashSet<int>();
            var added = new List<(int Start, int End, DiagnosticIndicator Indicator)>();

            // Primero los más graves: si dos se pisan, queda el error
            foreach (Diagnostic d in diagnostics.OrderBy(d => d.Severity))
            {
                if (d.Severity == Severity.Warning && !showWarnings)
                    continue;
                if (d.Line < 0 || d.Line >= document.Lines.Count)
                    continue;
                DocumentLine line = document.Lines[d.Line];
                // Dónde empieza lo marcado sin acomodarlo a la línea: desde ahí se ubican sus correcciones
                int original = line.StartOffset + d.Column;
                int start = line.StartOffset + Math.Min(d.Column, line.Length);
                int end = Math.Min(start + Math.Max(1, d.Length), line.EndOffset);
                // Un error al final de la línea marca el último carácter
                if (end <= start)
                {
                    if (line.Length == 0)
                        continue;
                    start = line.EndOffset - 1;
                    end = line.EndOffset;
                }
                // Dentro de una capa los subrayados no se pueden superponer: se suma al que ya está ahí
                int overlap = added.FindIndex(a => a.Start < end && start < a.End);
                if (overlap >= 0)
                {
                    added[overlap].Indicator.Diagnostics.Add(d);
                    added[overlap].Indicator.Deltas[d] = original - added[overlap].Start;
                    continue;
                }
                DiagnosticIndicator indicator;
                switch (d.Severity)
                {
                    case Severity.Error:
                        indicator = new DiagnosticIndicator(ErrorColor, HighlightingStyleLineStyle.Wave, HighlightingStyleLineWeight.Double, spanBack, glyph: false);
                        break;
                    case Severity.Warning:
                        indicator = new DiagnosticIndicator(WarningColor, HighlightingStyleLineStyle.Wave, HighlightingStyleLineWeight.Single, Color.Empty, glyph: false);
                        break;
                    default:
                        indicator = new DiagnosticIndicator(InfoColor, HighlightingStyleLineStyle.Dot, HighlightingStyleLineWeight.Single, Color.Empty, glyph: false);
                        break;
                }
                indicator.Diagnostics.Add(d);
                indicator.Deltas[d] = original - start;
                added.Add((start, end, indicator));
                Add(layer, indicator, start, end, d);

                if (d.Severity == Severity.Error && errorLines.Add(d.Line) && line.Length > 0)
                {
                    // Comparte la lista: si se corrigen los errores de la línea, se va el fondo
                    var lineIndicator = new DiagnosticIndicator(Color.Empty, HighlightingStyleLineStyle.None, HighlightingStyleLineWeight.Single, lineBack, glyph: true);
                    lineIndicator.Diagnostics = indicator.Diagnostics;
                    lineIndicator.Tag = indicator.Diagnostics;
                    Add(lines, lineIndicator, line.StartOffset, line.EndOffset, d);
                }
            }
        }

        public static void Clear(SyntaxEditor editor)
        {
            Layer(editor, LayerKey, LayerPriority).Clear();
            Layer(editor, LineLayerKey, LineLayerPriority).Clear();
        }

        /// <summary>Dónde empieza ahora lo marcado por el error (el texto se pudo editar desde que se marcó), o null.</summary>
        public static int? Anchor(SyntaxEditor editor, Diagnostic d)
        {
            SpanIndicatorLayer layer = editor.Document.SpanIndicatorLayers[LayerKey];
            if (layer == null)
                return null;
            foreach (SpanIndicator indicator in layer)
            {
                if (indicator is DiagnosticIndicator marked && marked.Deltas.TryGetValue(d, out int delta))
                    return indicator.TextRange.StartOffset + delta;
            }
            return null;
        }

        /// <summary>Saca el subrayado del error (y el fondo de la línea si no le queda otro error).</summary>
        public static void Remove(SyntaxEditor editor, Diagnostic d)
        {
            foreach (string key in new[] { LayerKey, LineLayerKey })
            {
                SpanIndicatorLayer layer = editor.Document.SpanIndicatorLayers[key];
                if (layer == null)
                    continue;
                var remove = new List<SpanIndicator>();
                foreach (SpanIndicator indicator in layer)
                {
                    if (!(indicator is DiagnosticIndicator marked))
                        continue;
                    marked.Diagnostics.Remove(d);
                    marked.Deltas.Remove(d);
                    bool empty = key == LayerKey ? marked.Diagnostics.Count == 0 : !marked.Diagnostics.Any(o => o.Severity == Severity.Error);
                    if (empty)
                        remove.Add(indicator);
                }
                foreach (SpanIndicator indicator in remove)
                    layer.Remove(indicator);
            }
        }

        /// <summary>Los errores marcados en esa posición del texto, los más graves primero.</summary>
        public static List<Diagnostic> At(SyntaxEditor editor, int offset, bool includeEnd)
        {
            var result = new List<Diagnostic>();
            SpanIndicatorLayer layer = editor.Document.SpanIndicatorLayers[LayerKey];
            if (layer == null)
                return result;
            foreach (SpanIndicator indicator in layer)
            {
                TextRange range = indicator.TextRange;
                if (indicator.Tag is List<Diagnostic> diagnostics && range.StartOffset <= offset && (offset < range.EndOffset || (includeEnd && offset == range.EndOffset)))
                    result.AddRange(diagnostics);
            }
            return result.OrderBy(d => d.Severity).ToList();
        }

        static void Add(SpanIndicatorLayer layer, SpanIndicator indicator, int start, int end, Diagnostic d)
        {
            try
            {
                layer.Add(indicator, start, end - start);
            }
            catch (Exception ex)
            {
                // Se pisa con otro de la misma capa
                Log.Write($"No se pudo marcar {d}: {ex.Message}");
            }
        }

        static SpanIndicatorLayer Layer(SyntaxEditor editor, string key, int priority)
        {
            SpanIndicatorLayerCollection layers = editor.Document.SpanIndicatorLayers;
            SpanIndicatorLayer layer = layers[key];
            if (layer == null)
            {
                layer = new SpanIndicatorLayer(key, priority);
                layers.Add(layer);
            }
            return layer;
        }

        /// <summary>Subrayado, fondo y círculo en el margen, cada uno opcional.</summary>
        sealed class DiagnosticIndicator : SpanIndicator
        {
            readonly Color m_Line;
            readonly HighlightingStyleLineStyle m_Style;
            readonly HighlightingStyleLineWeight m_Weight;
            readonly Color m_Back;
            readonly bool m_Glyph;
            /// <summary>Los errores que marca (los que caen en el mismo lugar van juntos; es también su Tag).</summary>
            public List<Diagnostic> Diagnostics = new List<Diagnostic>();
            /// <summary>Dónde empieza cada error respecto del comienzo del subrayado.</summary>
            public readonly Dictionary<Diagnostic, int> Deltas = new Dictionary<Diagnostic, int>();

            public DiagnosticIndicator(Color line, HighlightingStyleLineStyle style, HighlightingStyleLineWeight weight, Color back, bool glyph)
                : base("GxCodeCheck")
            {
                m_Line = line;
                m_Style = style;
                m_Weight = weight;
                m_Back = back;
                m_Glyph = glyph;
                Tag = Diagnostics;
            }

            public override bool HasFontChange => false;

            public override bool HasGlyph => m_Glyph;

            protected override void ApplyHighlightingStyleAdornments(HighlightingStyleResolver resolver)
            {
                if (m_Line != Color.Empty)
                    resolver.ApplyUnderline(m_Line, m_Style, m_Weight);
            }

            protected override void ApplyHighlightingStyleColors(HighlightingStyleResolver resolver)
            {
                if (m_Back != Color.Empty)
                    resolver.SetBackColor(m_Back);
            }

            public override void DrawGlyph(PaintEventArgs e, Rectangle bounds)
            {
                if (!m_Glyph)
                    return;
                int size = Math.Max(6, Math.Min(bounds.Width, bounds.Height) - 2);
                var circle = new Rectangle(bounds.Left + (bounds.Width - size) / 2, bounds.Top + (bounds.Height - size) / 2, size, size);
                SmoothingMode smoothing = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(ErrorColor))
                    e.Graphics.FillEllipse(brush, circle);
                // El "!" blanco: una barra y un punto
                int x = circle.Left + circle.Width / 2;
                using (var pen = new Pen(Color.White, Math.Max(1.5f, size / 7f)))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLine(pen, x, circle.Top + size * 0.22f, x, circle.Top + size * 0.58f);
                    e.Graphics.DrawLine(pen, x, circle.Top + size * 0.76f, x, circle.Top + size * 0.78f);
                }
                e.Graphics.SmoothingMode = smoothing;
            }
        }
    }
}
