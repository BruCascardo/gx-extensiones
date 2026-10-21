using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Artech.Architecture.UI.Framework.Services;

namespace GxCodeKeys
{
    /// <summary>Página "GxCodeKeys" en Tools &gt; Options (mismo mecanismo que usa el editor de texto de GeneXus).</summary>
    sealed class OptionsCategory : ConfigurationCategory, ICategoryLoader
    {
        public OptionsCategory()
            : base("GxCodeKeys", "GxCodeKeys")
        {
            Loader = this;
        }

        public override Control CreateEditor() => new OptionsControl();

        public void LoadData(IParentDesigner parent, object data, ConfigurationCategory category)
        {
            ((OptionsControl)Control).LoadFromSettings(parent);
        }

        public new bool Flush()
        {
            if (Control != null)
                return ((OptionsControl)Control).SaveToSettings();
            return true;
        }
    }

    sealed class OptionsControl : UserControl
    {
        readonly Dictionary<CodeCommand, ShortcutBox> m_Boxes = new Dictionary<CodeCommand, ShortcutBox>();
        readonly Label m_Warning = new Label();
        readonly CheckBox m_AltClick = new CheckBox();
        IParentDesigner m_Parent;
        bool m_Loading;

        public OptionsControl()
        {
            Font = SystemFonts.MessageBoxFont;
            AutoScroll = true;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                Padding = new Padding(8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddTitle(layout, "Atajos de teclado");
            AddNote(layout, "Funcionan dentro de los editores de código (Events, Source, Rules, Conditions). " +
                            "Hacé clic en el campo y apretá la combinación; Supr o Retroceso lo deja sin atajo.");
            foreach (CommandInfo command in Settings.Commands)
            {
                var box = new ShortcutBox();
                m_Boxes[command.Command] = box;
                AddShortcutRow(layout, command.Caption, box, command.DefaultKeys);
                box.KeysChanged += (s, e) => OnChanged();
            }

            m_Warning.AutoSize = true;
            m_Warning.MaximumSize = new Size(560, 0);
            m_Warning.ForeColor = Color.FromArgb(0xB2, 0x5E, 0x00);
            m_Warning.Margin = new Padding(3, 2, 3, 8);
            layout.Controls.Add(m_Warning, 0, layout.RowCount);
            layout.SetColumnSpan(m_Warning, 3);
            layout.RowCount++;

            AddTitle(layout, "Múltiples cursores");
            AddCheck(layout, m_AltClick, "Alt+Click agrega un cursor (Alt+arrastrar sigue haciendo la selección en bloque)");
            AddNote(layout, "Con varios cursores: escribir, Retroceso, Supr, Enter, Tab, flechas, Inicio, Fin y Ctrl+V " +
                            "actúan en todos. Esc o un click normal vuelven a un solo cursor.");
            m_AltClick.CheckedChanged += (s, e) => OnChanged();

            Controls.Add(layout);
        }

        public void LoadFromSettings(IParentDesigner parent)
        {
            m_Parent = parent;
            m_Loading = true;
            foreach (var pair in m_Boxes)
                pair.Value.Keys = Settings.GetKeys(pair.Key);
            m_AltClick.Checked = Settings.AltClickCursors;
            m_Loading = false;
            UpdateWarning();
        }

        public bool SaveToSettings()
        {
            foreach (var pair in m_Boxes)
                Settings.SetKeys(pair.Key, pair.Value.Keys);
            Settings.AltClickCursors = m_AltClick.Checked;
            Settings.Save();
            return true;
        }

        void OnChanged()
        {
            if (m_Loading)
                return;
            UpdateWarning();
            if (m_Parent != null)
                m_Parent.Dirty = true;
        }

        void UpdateWarning()
        {
            var warnings = new List<string>();
            var seen = new Dictionary<Keys, string>();
            foreach (CommandInfo command in Settings.Commands)
            {
                Keys keys = m_Boxes[command.Command].Keys;
                if (keys == Keys.None)
                    continue;
                string name = command.Caption.TrimEnd(':');
                if (seen.TryGetValue(keys, out string other))
                {
                    warnings.Add($"{Shortcuts.Format(keys)} está repetido en \"{other}\" y \"{name}\": solo funciona el primero.");
                    continue;
                }
                seen[keys] = name;
                string conflict = Shortcuts.ConflictOf(keys);
                if (conflict != null)
                    warnings.Add($"{Shortcuts.Format(keys)} ({name}) ya lo usa {conflict}. Dentro del editor de código gana GxCodeKeys.");
            }
            m_Warning.Text = string.Join(Environment.NewLine, warnings);
            m_Warning.Visible = warnings.Count > 0;
        }

        static void AddTitle(TableLayoutPanel layout, string text)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Margin = new Padding(3, 10, 3, 4),
            };
            layout.Controls.Add(label, 0, layout.RowCount);
            layout.SetColumnSpan(label, 3);
            layout.RowCount++;
        }

        static void AddNote(TableLayoutPanel layout, string text)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 0, 3, 8),
            };
            layout.Controls.Add(label, 0, layout.RowCount);
            layout.SetColumnSpan(label, 3);
            layout.RowCount++;
        }

        static void AddShortcutRow(TableLayoutPanel layout, string caption, ShortcutBox box, Keys defaultKeys)
        {
            int row = layout.RowCount++;
            layout.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) }, 0, row);
            box.Dock = DockStyle.Fill;
            layout.Controls.Add(box, 1, row);
            var reset = new LinkLabel { Text = "Restablecer (" + Shortcuts.Format(defaultKeys) + ")", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 3, 3) };
            reset.LinkClicked += (s, e) => box.Keys = defaultKeys;
            layout.Controls.Add(reset, 2, row);
        }

        static void AddCheck(TableLayoutPanel layout, CheckBox check, string text)
        {
            check.Text = text;
            check.AutoSize = true;
            layout.Controls.Add(check, 0, layout.RowCount);
            layout.SetColumnSpan(check, 3);
            layout.RowCount++;
        }
    }

    /// <summary>Campo que captura una combinación de teclas en vez de texto.</summary>
    sealed class ShortcutBox : TextBox
    {
        Keys m_Keys;

        public event EventHandler KeysChanged;

        public ShortcutBox()
        {
            ReadOnly = true;
            BackColor = SystemColors.Window;
            ShortcutsEnabled = false;
            Cursor = Cursors.IBeam;
        }

        public Keys Keys
        {
            get => m_Keys;
            set
            {
                m_Keys = value;
                Text = Shortcuts.Format(value);
                KeysChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            // Tab sin modificadores sigue moviendo el foco
            if (key == Keys.Tab && (keyData & Keys.Modifiers) == 0)
                return base.ProcessCmdKey(ref msg, keyData);

            if ((key == Keys.Delete || key == Keys.Back) && (keyData & Keys.Modifiers) == 0)
            {
                Keys = Keys.None;
                return true;
            }
            if (Shortcuts.IsModifierKey(key))
                return true;   // esperar la tecla principal

            if (Shortcuts.IsValid(keyData))
                Keys = keyData;
            else
                Text = Shortcuts.Format(keyData) + "  ← necesita Ctrl o Alt";
            return true;
        }

        protected override void OnKeyPress(KeyPressEventArgs e) => e.Handled = true;

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            Text = Shortcuts.Format(m_Keys);   // descarta un intento inválido
        }
    }
}
