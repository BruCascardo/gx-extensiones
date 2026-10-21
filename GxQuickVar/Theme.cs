using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GxQuickVar
{
    enum TypeGroup { Text, Number, Date, Bool, Binary, Geo, Sdt, Bc, Domain, ExternalObject, Attribute, Other }

    /// <summary>Colores del desplegable. Se elige claro u oscuro según el fondo del editor de código.</summary>
    sealed class Palette
    {
        public bool Dark;
        public Color Back, Border, Text, Gray, InputBack, Selection, SelectionBar, Accent, Error, ChipBorder;

        public static Palette For(Color editorBack) => editorBack.GetBrightness() < 0.5f ? DarkPalette() : LightPalette();

        static Palette LightPalette() => new Palette
        {
            Dark = false,
            Back = Color.White,
            Border = Color.FromArgb(0xC7, 0xCD, 0xD4),
            Text = Color.FromArgb(0x1F, 0x23, 0x28),
            Gray = Color.FromArgb(0x6B, 0x72, 0x80),
            InputBack = Color.FromArgb(0xF6, 0xF8, 0xFA),
            Selection = Color.FromArgb(0xE6, 0xF0, 0xFB),
            SelectionBar = Color.FromArgb(0x0B, 0x6B, 0xCB),
            Accent = Color.FromArgb(0x0B, 0x6B, 0xCB),
            Error = Color.FromArgb(0xC6, 0x28, 0x28),
            ChipBorder = Color.FromArgb(0xD0, 0xD7, 0xDE),
        };

        static Palette DarkPalette() => new Palette
        {
            Dark = true,
            Back = Color.FromArgb(0x25, 0x25, 0x26),
            Border = Color.FromArgb(0x45, 0x45, 0x4C),
            Text = Color.FromArgb(0xDC, 0xDC, 0xDC),
            Gray = Color.FromArgb(0x9A, 0x9A, 0x9A),
            InputBack = Color.FromArgb(0x1E, 0x1E, 0x1E),
            Selection = Color.FromArgb(0x09, 0x47, 0x71),
            SelectionBar = Color.FromArgb(0x37, 0x94, 0xFF),
            Accent = Color.FromArgb(0x4F, 0xA3, 0xFF),
            Error = Color.FromArgb(0xF4, 0x87, 0x71),
            ChipBorder = Color.FromArgb(0x55, 0x55, 0x5C),
        };

        /// <summary>Fondo y texto de la etiqueta de categoría de cada tipo.</summary>
        public void Badge(TypeGroup group, out Color back, out Color fore)
        {
            Color strong = BadgeColor(group);
            if (Dark)
            {
                back = Blend(strong, Back, 0.30f);
                fore = Blend(strong, Color.White, 0.55f);
            }
            else
            {
                back = Blend(strong, Color.White, 0.13f);
                fore = strong;
            }
        }

        static Color BadgeColor(TypeGroup group)
        {
            switch (group)
            {
                case TypeGroup.Text: return Color.FromArgb(0x15, 0x65, 0xC0);
                case TypeGroup.Number: return Color.FromArgb(0x2E, 0x7D, 0x32);
                case TypeGroup.Date: return Color.FromArgb(0xD8, 0x4A, 0x00);
                case TypeGroup.Bool: return Color.FromArgb(0x7B, 0x1F, 0xA2);
                case TypeGroup.Binary: return Color.FromArgb(0x45, 0x5A, 0x64);
                case TypeGroup.Geo: return Color.FromArgb(0x00, 0x69, 0x5C);
                case TypeGroup.Sdt: return Color.FromArgb(0x00, 0x83, 0x8F);
                case TypeGroup.Bc: return Color.FromArgb(0x28, 0x35, 0x93);
                case TypeGroup.Domain: return Color.FromArgb(0x9A, 0x6B, 0x00);
                case TypeGroup.ExternalObject: return Color.FromArgb(0x6D, 0x4C, 0x41);
                case TypeGroup.Attribute: return Color.FromArgb(0xAD, 0x14, 0x57);
                default: return Color.FromArgb(0x61, 0x61, 0x61);
            }
        }

        /// <summary>Mezcla <paramref name="a"/> sobre <paramref name="b"/> con opacidad <paramref name="amount"/>.</summary>
        public static Color Blend(Color a, Color b, float amount) => Color.FromArgb(
            (int)(a.R * amount + b.R * (1 - amount)),
            (int)(a.G * amount + b.G * (1 - amount)),
            (int)(a.B * amount + b.B * (1 - amount)));
    }

    static class Drawing
    {
        public static GraphicsPath RoundRect(Rectangle r, int radius)
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

    /// <summary>Esquinas redondeadas y borde de color nativos de Windows 11 (en Windows 10 no hace nada).</summary>
    static class Dwm
    {
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_BORDER_COLOR = 34;
        const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static bool TryRoundCorners(IntPtr hwnd, Color border)
        {
            try
            {
                int corner = DWMWCP_ROUND;
                if (DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int)) != 0)
                    return false;
                int color = ColorTranslator.ToWin32(border);
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref color, sizeof(int));
                return true;
            }
            catch (Exception)
            {
                return false;   // dwmapi ausente o atributo no soportado
            }
        }
    }
}
