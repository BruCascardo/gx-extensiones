using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace GxCallParms
{
    /// <summary>De dónde sale una sugerencia (y su ícono en la lista).</summary>
    enum ArgumentKind { Preset, EnumValue, NewVariable, Variable, Member }

    sealed class ArgumentOption
    {
        public string Text;
        public string Detail;
        public ArgumentKind Kind;
    }

    /// <summary>
    /// Los íconos de las sugerencias. Se agregan al final de la lista de imágenes del autocompletado de GeneXus
    /// (la misma lista sirve a las dos), así sus índices no cambian.
    /// </summary>
    static class Icons
    {
        static readonly ArgumentKind[] Kinds = (ArgumentKind[])Enum.GetValues(typeof(ArgumentKind));
        static readonly Dictionary<ImageList, int> s_First = new Dictionary<ImageList, int>();

        /// <summary>El índice del primer ícono en <paramref name="images"/> (el de cada clase es este + la clase); -1 sin íconos.</summary>
        public static int Register(ImageList images)
        {
            if (images == null)
                return -1;
            try
            {
                if (s_First.TryGetValue(images, out int first) && first + Kinds.Length <= images.Images.Count)
                    return first;
                first = images.Images.Count;
                foreach (ArgumentKind kind in Kinds)
                    images.Images.Add(Draw(kind, images.ImageSize));
                s_First[images] = first;
                return first;
            }
            catch (Exception ex)
            {
                Log.Write("íconos: " + ex.Message);
                return -1;
            }
        }

        /// <summary>Un cuadrado redondeado del color de la clase con su letra.</summary>
        static Bitmap Draw(ArgumentKind kind, Size size)
        {
            var bitmap = new Bitmap(size.Width, size.Height);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (GraphicsPath path = RoundRect(new Rectangle(1, 1, size.Width - 2, size.Height - 2), Math.Max(2, size.Width / 5)))
            using (var back = new SolidBrush(ColorOf(kind)))
            using (var font = new Font("Segoe UI", size.Height * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.FillPath(back, path);
                g.DrawString(LetterOf(kind), font, Brushes.White, new RectangleF(0, 0.5f, size.Width, size.Height), format);
            }
            return bitmap;
        }

        static Color ColorOf(ArgumentKind kind)
        {
            switch (kind)
            {
                case ArgumentKind.Preset: return Color.FromArgb(0x7B, 0x1F, 0xA2);
                case ArgumentKind.EnumValue: return Color.FromArgb(0x9A, 0x6B, 0x00);
                case ArgumentKind.NewVariable: return Color.FromArgb(0x2E, 0x7D, 0x32);
                case ArgumentKind.Member: return Color.FromArgb(0x00, 0x83, 0x8F);
                default: return Color.FromArgb(0x15, 0x65, 0xC0);
            }
        }

        static string LetterOf(ArgumentKind kind)
        {
            switch (kind)
            {
                case ArgumentKind.Preset: return "P";
                case ArgumentKind.EnumValue: return "E";
                case ArgumentKind.NewVariable: return "+";
                case ArgumentKind.Member: return "M";
                default: return "V";
            }
        }

        static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
