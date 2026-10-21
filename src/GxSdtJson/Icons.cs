using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GxSdtJson
{
    /// <summary>Íconos de la barra, dibujados acá para no depender de recursos binarios: "{}" con una flecha.</summary>
    static class Icons
    {
        static Bitmap s_Copy;
        static Bitmap s_Import;

        public static Image Copy => s_Copy ?? (s_Copy = Draw(outward: true));
        public static Image Import => s_Import ?? (s_Import = Draw(outward: false));

        static Bitmap Draw(bool outward)
        {
            var bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var font = new Font("Consolas", 8.5f, FontStyle.Bold, GraphicsUnit.Point))
            using (var braces = new SolidBrush(Color.FromArgb(0, 102, 204)))
            using (var arrow = new Pen(Color.FromArgb(0, 140, 70), 1.6f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString("{}", font, braces, -2f, -1f);

                // Flecha abajo a la derecha: hacia afuera (copiar) o hacia adentro (importar)
                arrow.EndCap = LineCap.ArrowAnchor;
                if (outward)
                    g.DrawLine(arrow, 8f, 13f, 15f, 13f);
                else
                    g.DrawLine(arrow, 15f, 13f, 8f, 13f);
            }
            return bitmap;
        }
    }
}
