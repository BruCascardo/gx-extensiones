using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Services;
using Artech.Udm.Framework;

namespace GxQuickOpen
{
    /// <summary>Abre la búsqueda y, al aceptar, los objetos elegidos.</summary>
    static class QuickOpen
    {
        static bool s_Showing;
        static string s_LastSearch = string.Empty;

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        static extern bool IsWindowEnabled(IntPtr hwnd);

        const uint GA_ROOTOWNER = 3;

        /// <summary>
        /// null si el atajo tiene que abrir la búsqueda ahora; si no, por qué sigue de largo hacia
        /// GeneXus (queda en el log de diagnóstico).
        /// </summary>
        public static string WhyNotFromKeyboard()
        {
            if (s_Showing)
                return "la búsqueda ya está abierta";
            if (CurrentModel == null)
                return "no hay una KB abierta";
            IntPtr main = MainHandle;
            if (main == IntPtr.Zero)
                return "no se encontró la ventana principal";
            // Con un diálogo modal abierto la ventana principal queda deshabilitada: ahí no
            if (!IsWindowEnabled(main))
                return "hay un diálogo abierto";
            // El foco en la ventana principal o en una ventana flotante suya (KB Explorer suelto...)
            IntPtr foreground = GetForegroundWindow();
            IntPtr root = foreground == IntPtr.Zero ? IntPtr.Zero : GetAncestor(foreground, GA_ROOTOWNER);
            if (root != main)
                return $"la ventana activa ({foreground.ToInt64():X}, raíz {root.ToInt64():X}) no es la principal ({main.ToInt64():X})";
            return null;
        }

        /// <summary>Abre la búsqueda cuando termine de procesarse la tecla (la llama el hook de teclado).</summary>
        public static void ShowDeferred()
        {
            Control target = MainForm ?? Form.ActiveForm;
            Diag.Log("abrir diferido sobre " + (target?.GetType().FullName ?? "(sin ventana)"));
            if (target != null && target.IsHandleCreated)
                target.BeginInvoke((Action)Show);
            else if (System.Threading.SynchronizationContext.Current != null)
                System.Threading.SynchronizationContext.Current.Post(_ => Show(), null);
            else
                CommonServices.Output.AddWarningLine("GxQuickOpen: no hay ventana activa para abrir la búsqueda; usá File > Abrir objeto (búsqueda rápida).");
        }

        static IntPtr MainHandle
        {
            get
            {
                try
                {
                    IntPtr handle = UIServices.Environment?.MainWindow?.Handle ?? IntPtr.Zero;
                    return handle == IntPtr.Zero ? IntPtr.Zero : GetAncestor(handle, GA_ROOTOWNER);
                }
                catch (Exception)
                {
                    return IntPtr.Zero;
                }
            }
        }

        public static bool IsAvailable => CurrentModel != null;

        static KBModel CurrentModel
        {
            get
            {
                try
                {
                    return UIServices.KB?.CurrentModel;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        static Form MainForm
        {
            get
            {
                IWin32Window window = UIServices.Environment?.MainWindow;
                return window == null ? null : Control.FromHandle(window.Handle) as Form;
            }
        }

        /// <summary>Clave de la KB para los recientes: su carpeta.</summary>
        public static string KbKey
        {
            get
            {
                try
                {
                    var kb = UIServices.KB?.CurrentKB;
                    return (kb?.Location ?? kb?.Name)?.ToLowerInvariant();
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public static void Show()
        {
            KBModel model = CurrentModel;
            if (s_Showing || model == null)
            {
                Diag.Log(s_Showing ? "Show: ya abierta" : "Show: sin KB");
                return;
            }
            s_Showing = true;
            try
            {
                Run(model);
            }
            catch (Exception ex)
            {
                Diag.Log("Show: " + ex);
                CommonServices.Output.AddErrorLine("GxQuickOpen: " + ex.Message);
            }
            finally
            {
                s_Showing = false;
            }
        }

        static void Run(KBModel model)
        {
            Form owner = MainForm ?? Form.ActiveForm;
            IndexSnapshot snapshot = ObjectIndex.Current(model);
            bool stale = ObjectIndex.IsStale(snapshot);

            int iconSize;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                iconSize = (int)Math.Round(16 * g.DpiX / 96f);

            var options = new QuickOpenOptions
            {
                Entries = snapshot?.Entries,
                Rank = BuildRank(),
                InitialText = Settings.RememberSearch ? s_LastSearch : string.Empty,
                Palette = ChoosePalette(owner),
                IconFor = type => ObjectIndex.IconFor(type, iconSize),
            };

            QuickOpenOutcome outcome;
            List<ObjectEntry> selected;
            string text;
            using (var popup = new QuickOpenPopup(options))
            {
                popup.Location = PopupLocation(owner, popup.Size);
                if (stale)
                    ObjectIndex.RefreshAsync(model).ContinueWith(task => OnIndexReady(popup, task), TaskScheduler.Default);
                Diag.Log($"ventana: {(snapshot == null ? "sin índice" : snapshot.Entries.Count + " objetos")}{(stale ? ", actualizando" : "")}");
                popup.ShowDialog(owner);
                outcome = popup.Outcome;
                Diag.Log("ventana cerrada: " + outcome + " (" + popup.CloseReason + ")");
                selected = popup.SelectedEntries.ToList();
                text = popup.SearchText;
            }
            s_LastSearch = text.Trim();

            if (outcome == QuickOpenOutcome.Open)
                OpenEntries(model, selected);
            else if (outcome == QuickOpenOutcome.Classic)
                OpenClassic(text);
        }

        static void OnIndexReady(QuickOpenPopup popup, Task<IndexSnapshot> task)
        {
            Action update;
            if (task.Status == TaskStatus.RanToCompletion)
            {
                List<ObjectEntry> entries = task.Result.Entries;
                update = () => popup.SetEntries(entries);
            }
            else
            {
                string error = task.Exception?.GetBaseException().Message ?? "cancelado";
                update = () => popup.SetLoadingText("No se pudieron cargar los objetos: " + error);
            }
            try
            {
                if (!popup.IsDisposed && popup.IsHandleCreated)
                    popup.BeginInvoke((Action)(() =>
                    {
                        if (!popup.IsDisposed)
                            update();
                    }));
            }
            catch (InvalidOperationException)
            {
                // La ventana se cerró mientras se armaba el índice
            }
        }

        static RankContext BuildRank()
        {
            var rank = new RankContext();
            string kb = KbKey;
            if (kb != null)
            {
                List<Guid> recents = Recents.For(kb);
                for (int i = 0; i < recents.Count; i++)
                    rank.RecentRank[recents[i]] = i;
                rank.RecentCount = recents.Count;
            }
            try
            {
                rank.CurrentModule = UIServices.Environment?.ActiveDocument?.Object?.QualifiedName?.ModuleName;
            }
            catch (Exception)
            {
                rank.CurrentModule = null;
            }
            return rank;
        }

        static void OpenEntries(KBModel model, List<ObjectEntry> entries)
        {
            var missing = new List<string>();
            foreach (ObjectEntry entry in entries)
            {
                KBObject obj = model.Objects.Get((EntityKey)entry.Key);
                if (obj == null)
                {
                    missing.Add(entry.Name);
                    continue;
                }
                Recents.Record(KbKey, obj.Guid);
                UIServices.Objects.Open(obj, OpenDocumentOptions.CurrentVersion);
            }
            if (missing.Count > 0)
                Status("GxQuickOpen: ya no existe " + string.Join(", ", missing));
        }

        /// <summary>El diálogo de GeneXus de siempre, con lo escrito como patrón (si no tiene @ ni #).</summary>
        static void OpenClassic(string text)
        {
            var options = new SelectObjectOptions();
            options.Filters.Add(obj => UIServices.DocumentManager.IsOpenable(obj.TypeDescriptor));
            string pattern = (text ?? string.Empty).Split(' ').FirstOrDefault(t => t.Length > 0 && t[0] != '@' && t[0] != '#');
            if (pattern != null)
                options.DefaultPattern = pattern.Substring(pattern.LastIndexOf('.') + 1);
            foreach (KBObject obj in UIServices.SelectObjectDialog.SelectObjects(options))
            {
                Recents.Record(KbKey, obj.Guid);
                UIServices.Objects.Open(obj, obj.Mode.ToString() == "Inserted" ? new OpenDocumentOptions() : OpenDocumentOptions.CurrentVersion);
            }
        }

        static void Status(string message)
        {
            if (UIServices.IsStatusBarAvailable)
                UIServices.StatusBar.DisplayText(message);
            CommonServices.Output.AddWarningLine(message);
        }

        /// <summary>Centrada arriba de la ventana principal (como la paleta de comandos de VS Code).</summary>
        static Point PopupLocation(Form owner, Size size)
        {
            Rectangle area = owner != null ? owner.Bounds : Screen.PrimaryScreen.WorkingArea;
            Rectangle screen = Screen.FromRectangle(area).WorkingArea;
            int x = area.X + (area.Width - size.Width) / 2;
            int y = area.Y + Math.Min(area.Height / 8, 120);
            x = Math.Max(screen.X, Math.Min(x, screen.Right - size.Width));
            y = Math.Max(screen.Y, Math.Min(y, screen.Bottom - size.Height));
            return new Point(x, y);
        }

        static Palette ChoosePalette(Form owner)
        {
            switch (Settings.Theme)
            {
                case ThemeChoice.Light: return Palette.LightPalette();
                case ThemeChoice.Dark: return Palette.DarkPalette();
            }
            Color? editorBack = FindEditorBack(owner);
            return editorBack.HasValue ? Palette.For(editorBack.Value) : Palette.LightPalette();
        }

        /// <summary>
        /// El fondo de un editor de código visible (SyntaxEditor de Actipro): así la ventana sigue el
        /// esquema de colores que el usuario eligió para el código.
        /// </summary>
        static Color? FindEditorBack(Control root)
        {
            if (root == null)
                return null;
            foreach (Control child in root.Controls)
            {
                if (!child.Visible)
                    continue;
                for (Type type = child.GetType(); type != null; type = type.BaseType)
                {
                    if (type.FullName == "ActiproSoftware.SyntaxEditor.SyntaxEditor")
                        return child.BackColor;
                }
                Color? nested = FindEditorBack(child);
                if (nested.HasValue)
                    return nested;
            }
            return null;
        }
    }
}
