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

            PopupOptions options = CreateOptions(context.Model, context.Part.KBObject, name, existing,
                text => ApplyToPart(context.Model, context.Part, name, text), context.Palette, context.SyntaxEditor.Font);
            options.BatchIndex = batchIndex;
            options.BatchCount = batchCount;
            return ShowPopup(options, size => PopupLocation(context.SyntaxEditor, token, size), context.Editor.FindForm());
        }

        /// <summary>Opciones del desplegable para declarar o cambiarle el tipo a &amp;<paramref name="name"/>.</summary>
        internal static PopupOptions CreateOptions(KBModel model, KBObject obj, string name, Variable existing,
            Func<string, string> apply, Palette palette, Font codeFont)
        {
            var options = new PopupOptions
            {
                VariableName = name,
                AlreadyDeclared = existing != null && !existing.IsAutoDefined,
                Types = TypeCatalog.Get(model),
                Recents = ValidRecents(model),
                ModulePath = obj.QualifiedName.ModuleName ?? string.Empty,
                ModuleTypes = TypeCatalog.ModuleSdts(model, obj.QualifiedName.ModuleName),
                Apply = apply,
                Describe = entry => TypeCatalog.Describe(model, entry),
                AllAttributes = () => TypeCatalog.AllAttributes(model),
                FindAttributes = prefix => TypeCatalog.FindAttributes(model, prefix),
                Palette = palette,
                CodeFont = codeFont,
            };
            Suggest(model, name, existing, options);
            return options;
        }

        /// <summary>Muestra el desplegable donde diga <paramref name="locate"/> (recibe su tamaño) y espera a que se cierre.</summary>
        internal static PopupOutcome ShowPopup(PopupOptions options, Func<Size, Point> locate, IWin32Window owner)
        {
            using (var popup = new TypePopup(options))
            {
                popup.Location = locate(popup.Size);
                popup.ShowDialog(owner);
                return popup.Outcome;
            }
        }

        /// <summary>Texto inicial del desplegable y el porqué.</summary>
        static void Suggest(KBModel model, string name, Variable existing, PopupOptions options)
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
            var sameNameAttribute = Artech.Genexus.Common.Objects.Attribute.Get(model, name);
            if (sameNameAttribute != null)
            {
                options.InitialText = TypeSyntax.AttributePrefix + sameNameAttribute.Name;
                options.Hint = $"Se llama igual que el atributo {sameNameAttribute.Name}: GeneXus la basa en él. Enter la declara así.";
                return;
            }

            if (Settings.RememberByName)
            {
                string remembered = History.TypeFor(name);
                if (remembered != null && TypeCatalog.IsKnown(model, remembered))
                {
                    options.InitialText = remembered;
                    options.Hint = $"Sugerido: es el tipo que usaste la última vez para &{name}";
                    return;
                }
            }

            if (Settings.SuggestByName)
            {
                TypeEntry sameName = TypeCatalog.FindExact(model, name);
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

        /// <summary>
        /// Declara &amp;<paramref name="name"/> en la parte de variables con el tipo escrito, o le cambia el tipo
        /// si ya existe. Devuelve el mensaje de error, o null si salió bien.
        /// </summary>
        internal static string ApplyToPart(KBModel model, VariablesPart part, string name, string typeText)
        {
            Variable variable = part.GetVariable(name);
            bool isNew = variable == null;
            if (isNew)
            {
                variable = new Variable(part);
                variable.Name = name;
                DataType.SetDefault(variable);
            }

            string error = ApplyType(model, variable, typeText);
            if (error != null)
                return error;

            if (isNew)
                part.Variables.Add(variable);
            MarkModified(part);
            Declared(model, name, typeText, variable);
            return null;
        }

        /// <summary>
        /// Le asigna a la variable el tipo escrito ("VarChar(100)", "sdtX collection", "att ClienteId"...) y la deja
        /// declarada (si era autodefinida, deja de serlo). Si no se puede, la deja como estaba. Devuelve el mensaje
        /// de error, o null si salió bien.
        /// </summary>
        internal static string ApplyType(KBModel model, Variable variable, string typeText)
        {
            string typed = typeText;
            bool collection = TypeSyntax.StripCollection(ref typeText);
            if (typeText.Length == 0)
                return "Escribí un tipo";

            // Asignar el tipo toca varias propiedades: si falla a mitad de camino no tiene que quedar a medias
            VariableSnapshot before = VariableSnapshot.Take(variable);
            string error;
            try
            {
                error = TypeSyntax.TryParseAttribute(typed, out string attributeName)
                    ? SetAttribute(model, variable, attributeName)
                    : SetType(model, variable, typeText) ? null : $"Tipo inválido: {typeText}";
                if (error == null)
                {
                    // Solo si cambia: si no, la propiedad queda escrita aunque tenga el valor por defecto
                    if (variable.IsCollection != collection)
                        variable.IsCollection = collection;
                    if (variable.IsAutoDefined)
                        variable.IsAutoDefined = false;
                    VariableSnapshot.ForgetStaleDomain(variable);   // si estaba basada en un dominio y ya no
                }
            }
            catch (Exception)
            {
                before.RestoreInto(variable);
                throw;
            }
            if (error != null)
                before.RestoreInto(variable);
            return error;
        }

        /// <summary>Recuerda el tipo elegido para la próxima vez y lo informa en la barra de estado.</summary>
        internal static void Declared(KBModel model, string name, string typeText, Variable variable)
        {
            History.Record(name, TypeCatalog.Normalize(model, typeText));
            Status($"GxQuickVar: &{name} declarada como {TypeText(variable)}");
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

            // ParseInto acepta "Domain:X" de un dominio que no existe (la deja basada en "(none)") y
            // "VarChar(abc)" (le pone el largo por defecto): los dos son un error de tipeo
            if (TypeSyntax.BaseName(typeText, out _).StartsWith("Domain:", StringComparison.OrdinalIgnoreCase)
                || !TypeSyntax.HasValidParameters(typeText))
                return false;
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

        internal static void MarkModified(VariablesPart part)
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

        internal static string TypeText(Variable variable)
        {
            var attribute = variable.AttributeBasedOn;
            string text = attribute != null ? TypeSyntax.AttributePrefix + attribute.Name : new DataType(variable).ToString();
            return variable.IsCollection ? text + " collection" : text;
        }

        static Point PopupLocation(SyntaxEditor syntaxEditor, IToken token, Size popupSize)
        {
            Rectangle bounds = syntaxEditor.SelectedView.GetCharacterBounds(token.StartOffset);
            return PlaceNear(syntaxEditor.RectangleToScreen(bounds), popupSize, alignRight: false);
        }

        /// <summary>
        /// Debajo de <paramref name="anchor"/> (en coordenadas de pantalla), o arriba si no entra; alineado a su
        /// borde izquierdo o derecho, sin salirse de la pantalla.
        /// </summary>
        internal static Point PlaceNear(Rectangle anchor, Size popupSize, bool alignRight)
        {
            var location = new Point(alignRight ? anchor.Right - popupSize.Width : anchor.Left, anchor.Bottom + 4);

            Rectangle screen = Screen.FromRectangle(anchor).WorkingArea;
            if (location.X + popupSize.Width > screen.Right)
                location.X = screen.Right - popupSize.Width;
            location.X = Math.Max(screen.Left, location.X);
            if (location.Y + popupSize.Height > screen.Bottom)
                location.Y = Math.Max(screen.Top, anchor.Top - popupSize.Height - 4);
            return location;
        }

        internal static void Status(string message)
        {
            if (UIServices.IsStatusBarAvailable)
                UIServices.StatusBar.DisplayText(message);
        }
    }
}
