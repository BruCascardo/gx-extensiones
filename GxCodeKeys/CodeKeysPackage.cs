using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ActiproSoftware.SyntaxEditor;
using Artech.Architecture.Common.Packages;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Packages;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;

[assembly: Package(typeof(GxCodeKeys.CodeKeysPackage))]

namespace GxCodeKeys
{
    [Guid(PackageGuid)]
    public class CodeKeysPackage : AbstractPackageUI
    {
        public const string PackageGuid = "8A4083E3-88B9-4F33-9068-92D23AA4EAE0";

        static CommandKey Key(string id) => new CommandKey(new Guid(PackageGuid), id);

        public static readonly CommandKey OptionsCommand = Key("GxCodeKeysOptions");

        KeyboardFilter m_Filter;

        public override string Name => "GxCodeKeys";

        public override void Initialize(IGxServiceProvider services)
        {
            base.Initialize(services);
            // Los mismos comandos de los atajos, también desde el menú Edit (ids del manifiesto: "GxCodeKeys" + clave)
            foreach (CommandInfo command in Settings.Commands)
            {
                Action<SyntaxEditor> action = ActionFor(command.Command);
                AddCommand(Key("GxCodeKeys" + command.Key), data => Execute(command.Key, action, EditorLocator.Find(data)), QueryEditorCommand);
            }
            AddCommand(OptionsCommand, data => { UIServices.ToolsOptions.ShowOptions("GxCodeKeys"); return true; }, QueryAlwaysEnabled);
        }

        public override void PostInitialize()
        {
            base.PostInitialize();

            // Los atajos se leen de la configuración en cada tecla, así un cambio en
            // Tools > Options se aplica sin reiniciar GeneXus.
            m_Filter = new KeyboardFilter();
            foreach (CommandInfo command in Settings.Commands)
            {
                CodeCommand id = command.Command;
                m_Filter.Add(command.Key, () => Settings.GetKeys(id), ActionFor(id));
            }
            Application.AddMessageFilter(m_Filter);

            try
            {
                UIServices.ToolsOptions.RegisterCategory(new OptionsCategory());
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddWarningLine("GxCodeKeys: no se pudo agregar la página de opciones: " + ex.Message);
            }
        }

        public override void Close()
        {
            if (m_Filter != null)
            {
                Application.RemoveMessageFilter(m_Filter);
                m_Filter.Dispose();
            }
            MultiCursor.ClearAll();
            base.Close();
        }

        static Action<SyntaxEditor> ActionFor(CodeCommand command)
        {
            switch (command)
            {
                // Mover/duplicar actúan sobre el cursor principal: los extra se descartan antes
                case CodeCommand.MoveLineUp: return editor => { MultiCursor.Clear(editor); LineCommands.Move(editor, up: true); };
                case CodeCommand.MoveLineDown: return editor => { MultiCursor.Clear(editor); LineCommands.Move(editor, up: false); };
                case CodeCommand.DuplicateLineUp: return editor => { MultiCursor.Clear(editor); LineCommands.Duplicate(editor, up: true); };
                case CodeCommand.DuplicateLineDown: return editor => { MultiCursor.Clear(editor); LineCommands.Duplicate(editor, up: false); };
                case CodeCommand.CursorAbove: return editor => MultiCursor.AddVertical(editor, up: true);
                case CodeCommand.CursorBelow: return editor => MultiCursor.AddVertical(editor, up: false);
            }
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        static bool Execute(string name, Action<SyntaxEditor> action, SyntaxEditor editor)
        {
            if (!EditorLocator.IsWritable(editor))
                return false;
            try
            {
                action(editor);
                editor.Focus();
            }
            catch (Exception ex)
            {
                MultiCursor.Clear(editor);
                Log.Error(name, ex);
            }
            return true;
        }

        static bool QueryEditorCommand(CommandData data, ref CommandStatus status)
        {
            status.State = EditorLocator.IsWritable(EditorLocator.Find(data)) ? CommandState.Enabled : CommandState.Disabled;
            return true;
        }

        static bool QueryAlwaysEnabled(CommandData data, ref CommandStatus status)
        {
            status.State = CommandState.Enabled;
            return true;
        }
    }
}
