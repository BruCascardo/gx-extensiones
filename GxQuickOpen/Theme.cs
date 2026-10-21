using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GxQuickOpen
{
    /// <summary>Colores de la ventana. Se elige claro u oscuro según el fondo del editor de código.</summary>
    sealed class Palette
    {
        public bool Dark;
        public Color Back, Border, Text, Gray, InputBack, Selection, SelectionBar, Accent, Error, ChipBorder, ChipBack, ChipSelectedBack, ChipSelectedText, Module, Highlight;

        public static Palette For(Color editorBack) => editorBack.GetBrightness() < 0.5f ? DarkPalette() : LightPalette();

        public static Palette LightPalette() => new Palette
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
            ChipBack = Color.FromArgb(0xF6, 0xF8, 0xFA),
            ChipSelectedBack = Color.FromArgb(0x0B, 0x6B, 0xCB),
            ChipSelectedText = Color.White,
            Module = Color.FromArgb(0x00, 0x7A, 0x6E),
            Highlight = Color.FromArgb(0x0B, 0x6B, 0xCB),
        };

        public static Palette DarkPalette() => new Palette
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
            ChipBack = Color.FromArgb(0x2D, 0x2D, 0x30),
            ChipSelectedBack = Color.FromArgb(0x37, 0x94, 0xFF),
            ChipSelectedText = Color.White,
            Module = Color.FromArgb(0x4E, 0xC9, 0xB0),
            Highlight = Color.FromArgb(0x6C, 0xB6, 0xFF),
        };
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
