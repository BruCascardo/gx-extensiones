using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ActiproSoftware.SyntaxEditor;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Objects;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;
using Artech.Genexus.Common;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using Artech.Genexus.Common.Parts.Variables;
using Artech.Genexus.Common.Types;
using Artech.Genexus.UI.Common.Editor;

namespace GxQuickVar
{
    /// <summary>
    /// Toma la &amp;variable bajo el cursor (o todas las que faltan declarar), abre el desplegable de
    /// tipos y la declara, o le cambia el tipo si ya existía. Replica lo que hace el "Add variable" de
    /// GeneXus (GxTextEditor.ExecuteAddVariable) más la asignación de tipo de la grilla de variables
    /// (TypeCell.Value).
    /// </summary>
    static class VariableDeclarer
    {
        const int MaxRecentsShown = 8;

        sealed class Context
        {
            public GxTextEditor Editor;
            public SyntaxEditor SyntaxEditor;
            public VariablesPart Part;
            public KBModel Model;
            public Palette Palette;
        }

        public static void Run(CommandData data)
        {
            Guard(() =>
            {
                Context context = CreateContext(data);
                if (context == null)
                    return;

                IToken token = FindVariableToken(context, context.SyntaxEditor.SelectedView.Selection.EndOffset);
                if (token == null)
                {
                    Status("GxQuickVar: poné el cursor sobre una &variable");
                    return;
                }
                Declare(context, token, 0, 0);
                context.SyntaxEditor.Focus();
            });
        }

        /// <summary>Recorre las variables usadas en el editor que no están declaradas y las declara una por una.</summary>
        public static void RunAll(CommandData data)
        {
            Guard(() =>
            {
                Context context = CreateContext(data);
                if (context == null)
                    return;

                List<int> pending = PendingVariableOffsets(context);
                if (pending.Count == 0)
                {
                    Status("GxQuickVar: no hay variables sin declarar ni autodefinidas en este editor");
                    return;
                }

                int caret = context.SyntaxEditor.SelectedView.Selection.EndOffset;
                int declared = 0;
                for (int i = 0; i < pending.Count; i++)
                {
                    IToken token = context.SyntaxEditor.Document.Tokens.GetTokenAtOffset(pending[i]);
                    if (!context.Editor.IsVariableToken(token) || !IsPending(context.Part.GetVariable(context.Editor.GetVariableName(token))))
                        continue;   // la declaró otro paso (p.ej. una estándar) o cambió el texto

                    // Seleccionar la variable en el código para que se vea cuál se está declarando
                    context.SyntaxEditor.SelectedView.Selection.SelectRange(token.StartOffset, token.Length);
                    context.SyntaxEditor.SelectedView.ScrollToCaret();
                    context.SyntaxEditor.Update();

                    PopupOutcome outcome = Declare(context, token, i + 1, pending.Count);
                    if (outcome == PopupOutcome.Accepted)
                        declared++;
                    else if (outcome == PopupOutcome.Stopped)
                        break;
                }

                context.SyntaxEditor.SelectedView.Selection.SelectRange(caret, 0);
                context.SyntaxEditor.Focus();
                Status($"GxQuickVar: {declared} de {pending.Count} variables declaradas");
            });
        }

        static void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                CommonServices.Output.AddErrorLine("GxQuickVar: error inesperado: " + ex);
            }
        }

        static Context CreateContext(CommandData data)
        {
            GxTextEditor editor = EditorLocator.Find(data);
            if (editor == null || editor.ReadOnly)
                return null;
            SyntaxEditor syntaxEditor = EditorLocator.FindSyntaxEditor(editor);
            VariablesPart part = editor.GetVariablesPart();
            if (syntaxEditor == null || part == null)
                return null;

            return new Context
            {
                Editor = editor,
                SyntaxEditor = syntaxEditor,
                Part = part,
                Model = part.KBObject.Model,
                Palette = Palette.For(syntaxEditor.BackColor),
            };
        }

        /// <summary>El token de variable que contiene al offset o termina justo antes.</summary>
        static IToken FindVariableToken(Context context, int caret)
        {
            Document document = context.SyntaxEditor.Document;
            foreach (int offset in new[] { caret, caret - 1 })
            {
                if (offset < 0 || offset >= document.Length)
                    continue;
                IToken token = document.Tokens.GetTokenAtOffset(offset);
                if (context.Editor.IsVariableToken(token))
                    return token;
            }
            return null;
        }

        /// <summary>
        /// Una variable "pendiente" no está declarada o la definió GeneXus automáticamente (por nombre de
        /// atributo/dominio): en ese caso se ofrece con el tipo asumido para confirmarlo o cambiarlo.
        /// </summary>
        static bool IsPending(Variable variable) => variable == null || variable.IsAutoDefined;

        /// <summary>Primera aparición de cada variable pendiente.</summary>
        static List<int> PendingVariableOffsets(Context context)
        {
            var offsets = new List<int>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            TokenCollection tokens = context.SyntaxEditor.Document.Tokens;
            for (int i = 0; i < tokens.Count; i++)
            {
                IToken token = tokens[i];
                if (!context.Editor.IsVariableToken(token))
                    continue;
                string name = context.Editor.GetVariableName(token);
                if (Variable.IsValidName(name) && seen.Add(name) && IsPending(context.Part.GetVariable(name)))
                    offsets.Add(token.StartOffset);
            }
            return offsets;
        }

        static PopupOutcome Declare(Context context, IToken token, int batchIndex, int batchCount)
        {
            string name = context.Editor.GetVariableName(token);
            if (!Variable.IsValidName(name))
                return PopupOutcome.Skipped;

            Variable existing = context.Part.GetVariable(name);
            if (existing == null && DeclareStandard(context.Part, name))
                return PopupOutcome.Accepted;
            if (existing != null && VariableDefinition.IsStandard(existing))
            {
                Status($"GxQuickVar: &{name} es una variable estándar, su tipo no se cambia");
                return PopupOutcome.Skipped;
            }

            var options = new PopupOptions
            {
                VariableName = name,
                AlreadyDeclared = existing != null && !existing.IsAutoDefined,
                Types = TypeCatalog.Get(context.Model),
                Recents = ValidRecents(context.Model),
                ModulePath = context.Part.KBObject.QualifiedName.ModuleName ?? string.Empty,
                ModuleTypes = TypeCatalog.ModuleSdts(context.Model, context.Part.KBObject.QualifiedName.ModuleName),
                Apply = text => Apply(context, name, text),
                Describe = entry => TypeCatalog.Describe(context.Model, entry),
                AllAttributes = () => TypeCatalog.AllAttributes(context.Model),
                FindAttributes = prefix => TypeCatalog.FindAttributes(context.Model, prefix),
                BatchIndex = batchIndex,
                BatchCount = batchCount,
                Palette = context.Palette,
                CodeFont = context.SyntaxEditor.Font,
            };
            Suggest(context, name, existing, options);

            using (var popup = new TypePopup(options))
            {
                popup.Location = PopupLocation(context.SyntaxEditor, token, popup.Size);
                popup.ShowDialog(context.Editor.FindForm());
                return popup.Outcome;
            }
        }

        /// <summary>Texto inicial del desplegable y el porqué.</summary>
        static void Suggest(Context context, string name, Variable existing, PopupOptions options)
        {
            if (existing != null)
            {
                options.InitialText = TypeText(existing);
                if (existing.IsAutoDefined)
                {
                    var attribute = existing.AttributeBasedOn;
                    options.Hint = attribute != null
                        ? $"Autodefinida: GeneXus la basa en el atributo {attribute.Name}. Enter la declara así."
                        : $"Autodefinida: GeneXus la asume como {options.InitialText}. Enter la declara así.";
                }
                return;
            }

            // Antes de grabar GeneXus todavía no la autodefinió: si hay un atributo que se llama igual,
            // proponer lo mismo que va a asumir al grabar.
            var sameNameAttribute = Artech.Genexus.Common.Objects.Attribute.Get(context.Model, name);
            if (sameNameAttribute != null)
            {
                options.InitialText = TypeSyntax.AttributePrefix + sameNameAttribute.Name;
                options.Hint = $"Se llama igual que el atributo {sameNameAttribute.Name}: GeneXus la basa en él. Enter la declara así.";
                return;
            }

            if (Settings.RememberByName)
            {
                string remembered = History.TypeFor(name);
                if (remembered != null && TypeCatalog.IsKnown(context.Model, remembered))
                {
                    options.InitialText = remembered;
                    options.Hint = $"Sugerido: es el tipo que usaste la última vez para &{name}";
                    return;
                }
            }

            if (Settings.SuggestByName)
            {
                TypeEntry sameName = TypeCatalog.FindExact(context.Model, name);
                if (sameName != null && sameName.Group != TypeGroup.Other)
                {
                    options.InitialText = sameName.Name;
                    options.Hint = $"Sugerido: hay un {GroupName(sameName.Group)} que se llama igual";
                }
            }
        }

        static string GroupName(TypeGroup group)
        {
            switch (group)
            {
                case TypeGroup.Sdt: return "SDT";
                case TypeGroup.Bc: return "Business Component";
                case TypeGroup.Domain: return "dominio";
                case TypeGroup.ExternalObject: return "objeto externo";
                default: return "tipo";
            }
        }

        static List<RecentType> ValidRecents(KBModel model)
        {
            return History.Recent
                .Select(text => new RecentType { Text = text, Entry = TypeCatalog.FindForText(model, text) })
                .Where(r => r.Entry != null)
                .Take(MaxRecentsShown)
                .ToList();
        }

        /// <summary>&amp;Pgmname, &amp;Today, etc. tienen tipo fijo: se declaran sin preguntar.</summary>
        static bool DeclareStandard(VariablesPart part, string name)
        {
            Variable variable;
            if (VariableDefinition.IsStandard(name))
                variable = VariableDefinition.DefineStandard(part, name);
            else if (VariableDefinition.IsNewStandard(part, name))
                variable = VariableDefinition.DefineNewStandard(part, name);
            else
                return false;

            part.Variables.Add(variable);
            MarkModified(part);
            Status($"GxQuickVar: &{name} es estándar, declarada con su tipo fijo");
            return true;
        }

        /// <summary>Aplica el tipo escrito. Devuelve el mensaje de error, o null si salió bien.</summary>
        static string Apply(Context context, string name, string typeText)
        {
            string typed = typeText;
            bool collection = TypeSyntax.StripCollection(ref typeText);
            if (typeText.Length == 0)
                return "Escribí un tipo";

            VariablesPart part = context.Part;
            Variable variable = part.GetVariable(name);
            bool isNew = variable == null;
            if (isNew)
            {
                variable = new Variable(part);
                variable.Name = name;
                DataType.SetDefault(variable);
            }

            string error = TypeSyntax.TryParseAttribute(typed, out string attributeName)
                ? SetAttribute(context.Model, variable, attributeName)
                : SetType(context.Model, variable, typeText) ? null : $"Tipo inválido: {typeText}";
            if (error != null)
                return error;

            variable.IsCollection = collection;
            if (isNew)
                part.Variables.Add(variable);
            else
                variable.IsAutoDefined = false;

            MarkModified(part);
            History.Record(name, TypeCatalog.Normalize(context.Model, typed));
            Status($"GxQuickVar: &{name} declarada como {TypeText(variable)}");
            return null;
        }

        /// <summary>
        /// "Based on" un atributo, igual que elegirlo en las properties. DataType.ParseInto acepta
        /// "Attribute:X" pero pasa por el conversor de la propiedad idBasedOn, que no lo resuelve y la
        /// deja en (none).
        /// </summary>
        static string SetAttribute(KBModel model, Variable variable, string attributeName)
        {
            if (attributeName.Length == 0)
                return "Escribí el nombre del atributo";
            var attribute = Artech.Genexus.Common.Objects.Attribute.Get(model, attributeName);
            if (attribute == null)
                return $"No existe el atributo {attributeName}";

            variable.DomainBasedOn = null;   // limpia "Domain" si estaba basada en un dominio
            variable.AttributeBasedOn = attribute;
            return variable.AttributeBasedOn != null ? null : $"No se pudo basar en el atributo {attribute.Name}";
        }

        static bool SetType(KBModel model, Variable variable, string typeText)
        {
            // Un tipo de la lista (SDT, BC, EO, dominio...) se asigna con su DataTypeCaps, como hace
            // la grilla al elegir del combo. ParseInto solo reconoce "sdtOutput, Sistema.Global",
            // no "Sistema.Global.sdtOutput", y no hace falta para estos casos.
            // Si el nombre es de un tipo que no lleva parámetros (dominio, SDT...), unos paréntesis
            // de más ("domX, Cobranzas(0)") se ignoran.
            TypeEntry entry = TypeCatalog.FindExact(model, TypeSyntax.BaseName(typeText, out _));
            if (entry != null && (!TypeSyntax.HasParameters(typeText) || !TypeCatalog.TakesParameters(entry.Group)))
            {
                if (entry.Group == TypeGroup.Domain)
                {
                    Domain listed = Domain.Get(model, entry.Caps.Name);
                    if (listed != null && SetDomain(model, variable, listed))
                        return true;
                }
                else if (DataType.SetType(model, entry.Caps, variable))
                {
                    return true;
                }
            }

            // Un dominio escrito de cualquier forma ("Importe, Sistema", "Sistema.Importe", "Domain:Importe"),
            // antes que ParseInto: con parámetros de más ("Importe, Sistema(10.2)") ParseInto no lo reconoce,
            // y sin ellos lo resuelve por nombre y puede no asignarlo. Un tipo básico ("Numeric(10.2)") va directo.
            if (entry == null || entry.Group == TypeGroup.Domain)
            {
                Domain domain = FindDomain(model, typeText);
                if (domain != null)
                    return SetDomain(model, variable, domain);
            }

            return DataType.ParseInto(model, typeText, variable);
        }

        /// <summary>El dominio al que se refiere el texto (sin parámetros ni sufijo de colección), o null.</summary>
        static Domain FindDomain(KBModel model, string typeText)
        {
            string text = TypeSyntax.BaseName(typeText, out _);
            if (text.StartsWith("Domain:", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(7).Trim();
            if (text.Length == 0)
                return null;
            try
            {
                DataTypeHelper.GetNameFromText(text, out string modulePath, out string name);
                name = name.Trim();
                QualifiedName qualified = string.IsNullOrEmpty(modulePath) ? new QualifiedName(name) : new QualifiedName(modulePath, name);
                return Domain.Get(model, qualified);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// "Based on" el dominio. Primero como la grilla de variables (DataTypeCaps); ese camino resuelve el
        /// dominio por nombre a través del conversor de idBasedOn y, si no lo encuentra, devuelve true sin
        /// asignarlo, o falla según el upgrade de GeneXus. Si no quedó, se asigna directo por su clave.
        /// </summary>
        static bool SetDomain(KBModel model, Variable variable, Domain domain)
        {
            bool IsBasedOnIt() => variable.DomainBasedOn?.Id == domain.Id;

            try
            {
                if (DataType.SetType(model, new DataTypeCaps(eDBType.GX_DOM_REF, domain.QualifiedName), variable) && IsBasedOnIt())
                    return true;
            }
            catch (Exception)
            {
                // Se intenta directo
            }

            variable.DomainBasedOn = domain;
            return IsBasedOnIt();
        }

        static void MarkModified(VariablesPart part)
        {
            part.OnInvalidate();
            if (UIServices.DocumentManager.IsOpenDocument(part.KBObject, out IGxDocument document))
            {
                IGxDocumentPart variablesDocPart = document.GetDocumentPart(typeof(VariablesPart).GUID);
                if (variablesDocPart != null)
                    variablesDocPart.Dirty = true;
                document.Dirty = true;
            }
        }

        static string TypeText(Variable variable)
        {
            var attribute = variable.AttributeBasedOn;
            string text = attribute != null ? TypeSyntax.AttributePrefix + attribute.Name : new DataType(variable).ToString();
            return variable.IsCollection ? text + " collection" : text;
        }

        static Point PopupLocation(SyntaxEditor syntaxEditor, IToken token, Size popupSize)
        {
            Rectangle bounds = syntaxEditor.SelectedView.GetCharacterBounds(token.StartOffset);
            Point location = syntaxEditor.PointToScreen(new Point(bounds.Left, bounds.Bottom + 4));

            Rectangle screen = Screen.FromPoint(location).WorkingArea;
            if (location.X + popupSize.Width > screen.Right)
                location.X = Math.Max(screen.Left, screen.Right - popupSize.Width);
            if (location.Y + popupSize.Height > screen.Bottom)
                location.Y = Math.Max(screen.Top, syntaxEditor.PointToScreen(new Point(0, bounds.Top)).Y - popupSize.Height - 4);
            return location;
        }

        static void Status(string message)
        {
            if (UIServices.IsStatusBarAvailable)
                UIServices.StatusBar.DisplayText(message);
        }
    }
}
