using System;
using System.Runtime.InteropServices;

namespace GxCodeCheck
{
    /// <summary>Si se está usando el teclado o el mouse: lo que puede tardar se hace solo cuando no.</summary>
    static class Idle
    {
        /// <summary>Milisegundos desde la última tecla o movimiento del mouse.</summary>
        public static uint MillisecondsSinceInput()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.dwTime) : uint.MaxValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    }
}
