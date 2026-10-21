using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ActiproSoftware.SyntaxEditor;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Objects;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;
using Artech.Genexus.Common;
using Artech.Genexus.Common.CustomTypes;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using Artech.Genexus.Common.Parts.Variables;
using Artech.Genexus.Common.Types;
using Artech.Genexus.UI.Common.Editor;
using Attribute = Artech.Genexus.Common.Objects.Attribute;
using Group = System.Text.RegularExpressions.Group;

namespace GxCallParms
{
    /// <summary>
    /// Completa los parámetros de Objeto.call( / Objeto.udp( con las variables de la regla parm del
    /// objeto llamado y las declara en el objeto actual con el mismo tipo. Si el objeto tiene un único
    /// parámetro de salida y es el último, queda fuera de los paréntesis: &amp;Salida = Objeto.udp(&amp;Entrada).
    /// </summary>
    static class CallFiller
    {
        // Objeto(  /  Objeto.call(  /  Modulo.Sub.Objeto.udp(  ; no empieza en una &variable ni en medio
        // de un nombre. El nombre es perezoso para que ".call" / ".udp" quede como método y no como parte de él.
        static readonly Regex CallRegex = new Regex(
            @"(?<![&\w.])(?<name>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*?)(?:\.(?<method>call|udp))?\s*\((?<args>[ \t]*)(?<close>\))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Lo que hay antes de la llamada cuando ya es una asignación: "	&Resultado = "
        static readonly Regex AssignmentRegex = new Regex(@"^\s*&(?<var>[A-Za-z_]\w*)\s*=\s*$", RegexOptions.CultureInvariant);

        const string UdpMethod = "udp";

        // Tipos que se buscan si la resolución sin tipo no encuentra el nombre
        static readonly Guid?[] CallableTypes = { null, ObjClass.Procedure, ObjClass.DataProvider, ObjClass.WebPanel, ObjClass.Transaction };

        sealed class Context
        {
            public GxTextEditor Editor;
            public SyntaxEditor SyntaxEditor;
            public VariablesPart Part;
            public KBObject Caller;
        }

        enum CallKind { Implicit, Call, Udp }

        /// <summary>Una llamada con los paréntesis vacíos en el documento.</summary>
        sealed class CallSite
        {
            public string Name;
            public CallKind Kind;
            /// <summary>Lo que hay en la línea antes del nombre del objeto.</summary>
            public string Prefix;
            public int NameOffset;
            public int MethodOffset;
            public int MethodLength;
            public int ArgsOffset;
            public int ArgsLength;
            public bool HasClose;

            /// <summary>Si el objeto devuelve su salida como valor: udp, la llamada implícita "&amp;X = Objeto(", o call convertido.</summary>
            public bool ReturnsValue => Kind != CallKind.Call || Settings.CallToUdp;
        }

        enum Declaration { Declared, Existing, Different, Undeclared }

        static readonly HashSet<SyntaxEditor> s_Hooked = new HashSet<SyntaxEditor>();
        static bool s_Pending;
        static bool s_Applying;

        /// <summary>
        /// Escucha los cambios del documento del editor: el "(" puede llegar tecleado o insertado por el
        /// IntelliSense de GeneXus al elegir call/udp de la lista, y en ese caso no pasa por el teclado.
        /// </summary>
        public static void Hook(GxTextEditor editor)
        {
            SyntaxEditor syntaxEditor = EditorLocator.FindSyntaxEditor(editor);
            if (syntaxEditor == null || !s_Hooked.Add(syntaxEditor))
                return;
            syntaxEditor.DocumentTextChanged += (s, e) =>
            {
                if (s_Applying || s_Pending || !Settings.AutoFill || e.Modification == null)
                    return;
                string inserted = e.Modification.InsertedText;
                if (string.IsNullOrEmpty(inserted) || inserted.IndexOf('(') < 0)
                    return;
                // Diferido: que terminen el tecleo y lo que agregue GeneXus (el ")", el IntelliSense) antes de mirar el texto
                s_Pending = true;
                syntaxEditor.BeginInvoke((Action)(() =>
                {
                    s_Pending = false;
                    OnParenTyped(editor, syntaxEditor);
                }));
            };
            syntaxEditor.Disposed += (s, e) => s_Hooked.Remove(syntaxEditor);
            Log.Write("editor enganchado: " + (editor.GetVariablesPart()?.KBObject?.Name ?? "?"));
        }

        /// <summary>Se acaba de escribir un "(": si es de una llamada a un objeto, completarla.</summary>
        static void OnParenTyped(GxTextEditor editor, SyntaxEditor syntaxEditor)
        {
            Guard(() =>
            {
                if (editor.IsDisposed || editor.ReadOnly || !syntaxEditor.ContainsFocus)
                    return;
                Context context = CreateContext(editor);
                if (context == null)
                    return;
                int caret = context.SyntaxEditor.SelectedView.Selection.EndOffset;
                List<CallSite> sites = FindCallSites(context, caret, typed: true);
                if (sites.Count > 0)
                    Fill(context, sites, typed: true);
            });
        }

        /// <summary>Comando / atajo: completa la llamada bajo el cursor.</summary>
        public static void Run(CommandData data)
        {
            Guard(() =>
            {
                GxTextEditor editor = EditorLocator.Find(data);
                Context context = editor != null ? CreateContext(editor) : null;
                if (context == null)
                    return;
                int caret = context.SyntaxEditor.SelectedView.Selection.EndOffset;
                List<CallSite> sites = FindCallSites(context, caret, typed: false);
                if (sites.Count == 0)
                {
                    Status("GxCallParms: poné el cursor sobre una llamada a un objeto con los paréntesis vacíos");
                    return;
                }
                Fill(context, sites, typed: false);
                context.SyntaxEditor.Focus();
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
                Log.Write("error: " + ex);
                CommonServices.Output.AddErrorLine("GxCallParms: error inesperado: " + ex);
            }
        }

        static Context CreateContext(GxTextEditor editor)
        {
            if (editor.ReadOnly)
                return null;
            SyntaxEditor syntaxEditor = EditorLocator.FindSyntaxEditor(editor);
            VariablesPart part = editor.GetVariablesPart();
            if (syntaxEditor == null || part == null)
            {
                Log.Write($"sin contexto: syntaxEditor={syntaxEditor != null} variables={part != null}");
                return null;
            }

            return new Context
            {
                Editor = editor,
                SyntaxEditor = syntaxEditor,
                Part = part,
                Caller = part.KBObject,
            };
        }

        /// <summary>
        /// Las llamadas candidatas de la línea del cursor, la más cercana primero. Recién escrito el "(",
        /// los paréntesis tienen que estar junto al cursor; a pedido, la que contiene al cursor o las
        /// anteriores. Los paréntesis tienen que estar vacíos (cerrados, o sin cerrar al final de la línea).
        /// </summary>
        static List<CallSite> FindCallSites(Context context, int caret, bool typed)
        {
            Document document = context.SyntaxEditor.Document;
            DocumentLine line = document.Lines[document.OffsetToPosition(Math.Max(0, Math.Min(caret, document.Length))).Line];
            string text = line.EndOffset > line.StartOffset
                ? document.GetSubstring(new TextRange(line.StartOffset, line.EndOffset), LineTerminator.Newline)
                : string.Empty;
            int column = caret - line.StartOffset;

            var sites = new List<CallSite>();
            foreach (Match match in CallRegex.Matches(text))
            {
                Group args = match.Groups["args"];
                Group close = match.Groups["close"];
                int end = match.Index + match.Length;
                bool atCaret = args.Index <= column && column <= end;
                if (typed ? !atCaret : match.Index > column)
                    continue;
                if (!close.Success && text.Substring(end).Trim().Length > 0)
                    continue;   // "(" con algo escrito después: no es una llamada vacía

                int nameOffset = line.StartOffset + match.Index;
                if (IsCommentOrString(document, nameOffset))
                    continue;

                Group method = match.Groups["method"];
                sites.Insert(0, new CallSite   // de derecha a izquierda: la más cercana al cursor primero
                {
                    Name = match.Groups["name"].Value,
                    Kind = !method.Success ? CallKind.Implicit
                        : string.Equals(method.Value, UdpMethod, StringComparison.OrdinalIgnoreCase) ? CallKind.Udp : CallKind.Call,
                    Prefix = text.Substring(0, match.Index),
                    NameOffset = nameOffset,
                    MethodOffset = method.Success ? line.StartOffset + method.Index : 0,
                    MethodLength = method.Length,
                    ArgsOffset = line.StartOffset + args.Index,
                    ArgsLength = args.Length,
                    HasClose = close.Success,
                });
            }
            Log.Write($"línea \"{text.Trim()}\" columna {column}: {sites.Count} candidata(s) {string.Join(", ", sites.Select(s => s.Name + "/" + s.Kind))}");
            return sites;
        }

        static bool IsCommentOrString(Document document, int offset)
        {
            IToken token = document.Tokens.GetTokenAtOffset(offset);
            return token != null && (token.IsComment || (token.Key ?? string.Empty).IndexOf("String", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static void Fill(Context context, List<CallSite> sites, bool typed)
        {
            // La primera candidata que es un objeto que se puede llamar. Las implícitas que no resuelven
            // (Trim(, Format(...) son funciones: se ignoran sin avisar al escribir.
            CallSite site = null;
            KBObject target = null;
            ICallableInfo callable = null;
            foreach (CallSite candidate in sites)
            {
                target = Resolve(context.Caller, candidate.Name);
                callable = target as ICallableInfo;
                Log.Write($"resolver {candidate.Name}: {(target == null ? "no encontrado" : target.GetType().Name + " " + target.QualifiedName + (callable == null ? " (no invocable)" : ""))}");
                if (callable != null)
                {
                    site = candidate;
                    break;
                }
            }
            if (site == null)
            {
                CallSite first = sites[0];
                if (!typed || first.Kind != CallKind.Implicit)
                    Status(target == null ? $"GxCallParms: no encontré el objeto {first.Name}" : $"GxCallParms: {target.Name} no se puede llamar con parámetros");
                return;
            }

            List<Parameter> parameters = ParametersOf(target, callable);

            // Un único parámetro de salida que además es el último: es el valor que devuelve el udp
            Parameter returned = null;
            List<Parameter> outputs = parameters.Where(p => p.Accessor == RuleDefinition.ParameterAccess.PARM_OUT).ToList();
            if (outputs.Count == 1 && outputs[0] == parameters.Last() && site.ReturnsValue)
                returned = outputs[0];

            // Dónde va el valor devuelto: "&X = Objeto.udp(" ya escrito, la llamada sola en la línea
            // (se agrega "&Salida = ") o dentro de una expresión (lo devuelve el udp, no hay que declararlo)
            Match assignment = AssignmentRegex.Match(site.Prefix);
            bool statement = site.Prefix.Trim().Length == 0;
            if (returned != null && site.Kind == CallKind.Call && !assignment.Success && !statement)
                returned = null;   // un .call dentro de una expresión no se convierte: todos entre paréntesis

            var declared = new List<string>();
            var different = new List<string>();
            var undeclared = new List<string>();
            void Track(string name, Declaration result)
            {
                switch (result)
                {
                    case Declaration.Declared: declared.Add(name); break;
                    case Declaration.Different: different.Add(name); break;
                    case Declaration.Undeclared: undeclared.Add(name); break;
                }
            }

            var arguments = new List<string>();
            foreach (Parameter parameter in parameters)
            {
                if (parameter == returned)
                    continue;
                string name = ArgumentName(target, parameter);
                if (!Variable.IsValidName(name))
                {
                    arguments.Add(parameter.Name);   // un parámetro que no es variable ni atributo: tal cual
                    continue;
                }
                arguments.Add("&" + name);
                Track(name, Declare(context.Part, name, parameter.Object));
            }

            string prefix = string.Empty;
            if (returned != null)
            {
                if (assignment.Success)
                {
                    string name = assignment.Groups["var"].Value;
                    Track(name, Declare(context.Part, name, returned.Object));
                }
                else if (statement)
                {
                    string name = ArgumentName(target, returned);
                    if (Variable.IsValidName(name))
                    {
                        prefix = "&" + name + " = ";
                        Track(name, Declare(context.Part, name, returned.Object));
                    }
                    else
                    {
                        returned = null;   // sin nombre para la variable de salida: va entre paréntesis
                        arguments.Add(ArgumentNameOrRaw(target, outputs[0]));
                    }
                }
            }
            bool toUdp = returned != null && site.Kind == CallKind.Call;

            if (declared.Count > 0)
                MarkModified(context.Part);

            ApplyText(context, site, string.Join(", ", arguments), toUdp, prefix);
            Log.Write($"completado {target.Name}: ({string.Join(", ", arguments)}) salida={(returned == null ? "-" : prefix.Length > 0 ? prefix.Trim() : "asignada/expresión")}");
            Status(Summary(target, parameters.Count, declared, different, undeclared));
        }

        /// <summary>Inserta los argumentos, cambia call por udp y agrega "&amp;Salida = " como un solo deshacer.</summary>
        static void ApplyText(Context context, CallSite site, string arguments, bool toUdp, string prefix)
        {
            Document document = context.SyntaxEditor.Document;
            string inside = arguments + (site.HasClose ? string.Empty : ")");
            int caret = site.ArgsOffset + inside.Length + (site.HasClose ? 1 : 0);

            s_Applying = true;
            document.UndoRedo.StartGroup(DocumentModificationType.Custom);
            try
            {
                // De derecha a izquierda, así los offsets de lo anterior siguen valiendo
                Replace(document, site.ArgsOffset, site.ArgsLength, inside);
                caret -= site.ArgsLength;
                if (toUdp)
                {
                    Replace(document, site.MethodOffset, site.MethodLength, UdpMethod);
                    caret += UdpMethod.Length - site.MethodLength;
                }
                if (prefix.Length > 0)
                {
                    Replace(document, site.NameOffset, 0, prefix);
                    caret += prefix.Length;
                }
            }
            finally
            {
                document.UndoRedo.EndGroup();
                s_Applying = false;
            }
            context.SyntaxEditor.SelectedView.Selection.SelectRange(caret, 0);
        }

        static void Replace(Document document, int offset, int length, string text)
        {
            const DocumentModificationOptions options = DocumentModificationOptions.CheckReadOnly;
            if (length == 0)
            {
                if (text.Length > 0)
                    document.InsertText(DocumentModificationType.Custom, offset, text, options);
            }
            else
            {
                document.ReplaceText(DocumentModificationType.Custom, offset, length, text, options);
            }
        }

        /// <summary>El objeto llamado, resuelto como GeneXus desde el módulo del objeto actual.</summary>
        static KBObject Resolve(KBObject caller, string name)
        {
            KBObject nonCallable = null;
            foreach (Module from in new[] { caller.Module, null })   // null: desde la raíz
            {
                foreach (Guid? type in CallableTypes)
                {
                    try
                    {
                        if (caller.Model.Objects.ResolveName(from, type, name, out ResolveResult result) && result.IsMatch && result.Match != null)
                        {
                            if (result.Match is ICallableInfo)
                                return result.Match;
                            nonCallable = nonCallable ?? result.Match;   // un módulo o carpeta con ese nombre: seguir buscando
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"ResolveName({name}, {type}): {ex.Message}");
                    }
                }
            }
            return nonCallable;
        }

        /// <summary>Los parámetros de la regla parm. Un Data Provider devuelve su SDT como último parámetro de salida.</summary>
        static List<Parameter> ParametersOf(KBObject target, ICallableInfo callable)
        {
            Signature signature = callable.GetSignatures()?.FirstOrDefault();
            List<Parameter> parameters = signature?.Parameters.ToList() ?? new List<Parameter>();

            if (target is DataProvider dataProvider && !parameters.Any(p => p.Accessor == RuleDefinition.ParameterAccess.PARM_OUT))
            {
                Variable output = SourceHelper.DataProviderSignature.CreateReturnValueVariable(dataProvider);
                if (output != null)
                    parameters.Add(new Parameter(output, false, RuleDefinition.ParameterAccess.PARM_OUT));
            }
            return parameters;
        }

        /// <summary>
        /// El nombre de la variable en el objeto actual: el mismo que en la regla parm del llamado (la
        /// variable o el atributo). La salida de un Data Provider ("ReturnValue") toma el nombre de su SDT.
        /// </summary>
        static string ArgumentName(KBObject target, Parameter parameter)
        {
            switch (parameter.Object)
            {
                case Variable variable when target is DataProvider && string.Equals(variable.Name, "ReturnValue", StringComparison.OrdinalIgnoreCase):
                    string typeName = TypeObjectName(variable);
                    return Variable.IsValidName(typeName) ? typeName : target.Name;
                case Variable variable:
                    return variable.Name;
                case Attribute attribute:
                    return attribute.Name;
                default:
                    return (parameter.Name ?? string.Empty).TrimStart('&');
            }
        }

        static string ArgumentNameOrRaw(KBObject target, Parameter parameter)
        {
            string name = ArgumentName(target, parameter);
            return Variable.IsValidName(name) ? "&" + name : parameter.Name;
        }

        /// <summary>"sdtCliente, Ventas" → "sdtCliente".</summary>
        static string TypeObjectName(Variable variable)
        {
            try
            {
                string text = new DataType(variable).ToString();
                int comma = text.IndexOf(',');
                if (comma >= 0)
                    text = text.Substring(0, comma);
                int colon = text.LastIndexOf(':');
                if (colon >= 0)
                    text = text.Substring(colon + 1);
                int dot = text.LastIndexOf('.');
                if (dot >= 0)
                    text = text.Substring(dot + 1);
                return text.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Declara la variable con el tipo del parámetro. Si ya estaba declarada no se toca (solo se avisa si
        /// el tipo es otro); si GeneXus la había autodefinido, se le pone el tipo del parámetro.
        /// </summary>
        static Declaration Declare(VariablesPart part, string name, ITypedObject source)
        {
            Variable existing = part.GetVariable(name);
            if (existing != null && !existing.IsAutoDefined)
                return source == null || SameType(existing, source) ? Declaration.Existing : Declaration.Different;
            if (existing == null && (VariableDefinition.IsStandard(name) || VariableDefinition.IsNewStandard(part, name)))
                return Declaration.Existing;   // &Pgmname, &Today...: GeneXus les pone su tipo fijo
            if (source == null)
                return existing != null ? Declaration.Existing : Declaration.Undeclared;

            Variable variable = existing ?? new Variable(name, part);
            if (!CopyType(part.KBObject.Model, variable, source))
                return existing != null ? Declaration.Existing : Declaration.Undeclared;

            if (existing == null)
                part.Variables.Add(variable);
            else
                variable.IsAutoDefined = false;
            return Declaration.Declared;
        }

        static bool CopyType(KBModel model, Variable variable, ITypedObject source)
        {
            switch (source)
            {
                case Variable original:
                    // Lo mismo que hace GeneXus al copiar una variable a otro objeto (Variable.MoveToPart)
                    variable.CopyPropertiesFrom(original);
                    return true;
                case Attribute attribute:
                    DataType.SetDefault(variable);
                    variable.DomainBasedOn = null;
                    variable.AttributeBasedOn = attribute;
                    return variable.AttributeBasedOn != null;
                case Domain domain:
                    DataType.SetDefault(variable);
                    return SetDomain(model, variable, domain);
                default:
                    return false;
            }
        }

        /// <summary>
        /// "Based on" el dominio, como la grilla de variables (DataTypeCaps); según el upgrade ese camino puede
        /// no asignarlo, y entonces se asigna directo por su clave.
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

        static bool SameType(Variable existing, ITypedObject source)
        {
            try
            {
                switch (source)
                {
                    case Attribute attribute:
                        if (existing.AttributeBasedOn?.Id == attribute.Id)
                            return true;
                        return !existing.IsCollection && new DataType(existing).ToString() == new DataType(attribute).ToString();
                    case Domain domain:
                        return existing.DomainBasedOn?.Id == domain.Id;
                    case Variable variable:
                        if (variable.AttributeBasedOn != null)
                            return existing.AttributeBasedOn?.Id == variable.AttributeBasedOn.Id
                                || (existing.IsCollection == variable.IsCollection && new DataType(existing).ToString() == new DataType(variable).ToString());
                        return existing.IsCollection == variable.IsCollection && new DataType(existing).ToString() == new DataType(variable).ToString();
                    default:
                        return true;
                }
            }
            catch (Exception)
            {
                return true;   // si no se puede comparar, no avisar
            }
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

        static string Summary(KBObject target, int parameterCount, List<string> declared, List<string> different, List<string> undeclared)
        {
            var parts = new List<string>
            {
                parameterCount == 0 ? $"{target.Name} no tiene parámetros"
                    : parameterCount == 1 ? $"{target.Name}: 1 parámetro" : $"{target.Name}: {parameterCount} parámetros",
            };
            if (declared.Count > 0)
                parts.Add("declaradas " + Names(declared));
            if (different.Count > 0)
                parts.Add("ya existían con otro tipo " + Names(different));
            if (undeclared.Count > 0)
                parts.Add("sin tipo para declarar " + Names(undeclared));
            return "GxCallParms: " + string.Join(" · ", parts);
        }

        static string Names(IEnumerable<string> names) => string.Join(", ", names.Select(n => "&" + n));

        static void Status(string message)
        {
            if (UIServices.IsStatusBarAvailable)
                UIServices.StatusBar.DisplayText(message);
        }
    }
}
