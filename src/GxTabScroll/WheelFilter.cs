using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Infragistics.Win.UltraWinTabbedMdi;
using Infragistics.Win.UltraWinTabs;

namespace GxTabScroll
{
    /// <summary>
    /// Rueda del mouse sobre la barra de pestañas de objetos, como en VS Code: desplaza la barra sin
    /// cambiar la pestaña activa (la rueda vertical se toma como horizontal, hacia abajo = a la derecha) y
    /// Shift+rueda cambia a la pestaña anterior o siguiente. Con "La rueda cambia de pestaña" (el
    /// workbench.editor.scrollToSwitchTabs de VS Code) es al revés.
    /// </summary>
    sealed class WheelFilter : IMessageFilter
    {
        const int WM_MOUSEWHEEL = 0x020A;
        const int WM_MOUSEHWHEEL = 0x020E;
        const int MK_SHIFT = 0x0004;
        const int WheelDelta = 120;

        // Los touchpads mandan fracciones de un paso: se acumulan hasta completar uno
        MdiTabGroup m_Group;
        int m_Pending;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL && m.Msg != WM_MOUSEHWHEEL)
                return false;
            if (!Settings.Enabled)
                return false;

            // El mensaje va a la ventana con foco o a la que está bajo el mouse según Windows: se mira el mouse
            MdiTabGroup group = TabGroupAt(PointOf(m.LParam));
            if (group == null)
                return false;

            try
            {
                long wParam = m.WParam.ToInt64();
                int delta = (short)(wParam >> 16);
                bool shift = (wParam & MK_SHIFT) != 0;

                // Hacia adelante (más pestañas a la derecha): rueda hacia abajo o inclinada a la derecha
                int forward = m.Msg == WM_MOUSEWHEEL ? -delta : delta;
                if (group != m_Group || Math.Sign(forward) != Math.Sign(m_Pending))
                    m_Pending = 0;
                m_Group = group;
                m_Pending += forward;

                int steps = m_Pending / WheelDelta;
                if (steps == 0)
                    return true;
                m_Pending -= steps * WheelDelta;

                if (shift != Settings.SwitchTabs)
                    SwitchTab(group, steps);
                else
                    group.Scroll(steps > 0 ? ScrollType.Next : ScrollType.Previous, Math.Abs(steps) * Settings.TabsPerStep);
            }
            catch (Exception ex)
            {
                Log.Write("Error al desplazar las pestañas: " + ex);
            }
            return true;
        }

        /// <summary>Activa la pestaña que está steps lugares a la derecha (o a la izquierda) de la actual, sin dar la vuelta.</summary>
        static void SwitchTab(MdiTabGroup group, int steps)
        {
            MdiTab selected = group.SelectedTab;
            if (selected == null || group.Tabs.Count < 2)
                return;
            int target = Math.Max(0, Math.Min(group.Tabs.Count - 1, selected.Index + steps));
            if (target != selected.Index)
                group.Tabs[target].Activate();
        }

        /// <summary>Grupo de pestañas cuya barra está bajo el punto (de pantalla), o null.</summary>
        static MdiTabGroup TabGroupAt(Point screenPoint)
        {
            // Los formularios de los objetos son ventanas aparte: MdiTabGroupControl es solo la barra
            for (Control control = Control.FromChildHandle(WindowFromPoint(screenPoint)); control != null; control = control.Parent)
            {
                if (control is MdiTabGroupControl tabGroupControl)
                    return tabGroupControl.TabGroup;
            }
            return null;
        }

        static Point PointOf(IntPtr lParam)
        {
            long value = lParam.ToInt64();
            return new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
        }

        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(Point point);
    }
}
