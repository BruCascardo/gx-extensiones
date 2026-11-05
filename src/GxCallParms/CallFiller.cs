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
using Artech.Common.Helpers.Structure;
using Artech.Common.Properties;
using Artech.Genexus.Common;
using Artech.Genexus.Common.CustomTypes;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using Artech.Genexus.Common.Parts.SDT;
using Artech.Genexus.Common.Parts.Variables;
using Artech.Genexus.Common.Types;
using Artech.Genexus.UI.Common.Editor;
using Attribute = Artech.Genexus.Common.Objects.Attribute;
using Group = System.Text.RegularExpressions.Group;

namespace GxCallParms
{
    /// <summary>
    /// Completa los parámetros de Objeto.call( / Objeto.udp( y declara en el objeto actual, con el tipo del
    /// parámetro, las variables nuevas: sugiriendo qué pasar en cada lugar mientras se escribe (<see cref="Session"/>)
    /// o, sin sugerir, con las variables de la regla parm del objeto llamado. Si el objeto tiene un único
    /// parámetro de salida y es el último, queda fuera de los paréntesis: &amp;Salida = Objeto.udp(&amp;Entrada).
    /// </summary>
    static partial class CallFiller
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
                if (editor.IsDisposed || editor.ReadOnly || !syntaxEditor.ContainsFocus || Session.IsActive(syntaxEditor))
                    return;   // un "(" dentro de los argumentos de la llamada que se está completando no empieza otra
                Context context = CreateContext(editor);
                if (context == null)
                    return;
                int caret = context.SyntaxEditor.SelectedView.Selection.EndOffset;
                List<CallSite> sites = FindCallSites(context, caret, typed: true);
                if (sites.Count == 0)
                    return;
                Fill(context, sites, typed: true);
                context.SyntaxEditor.Focus();
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
            if (returned != null && !assignment.Success && statement && !Variable.IsValidName(ArgumentName(target, returned)))
                returned = null;   // sin nombre para la variable de salida: va entre paréntesis

            // Un lugar por parámetro entre paréntesis y, si la llamada va sola, el de la variable que recibe la salida
            List<ParameterPreset> presets = Presets.For(target);
            Slot NewSlot(Parameter parameter, bool isReturn)
            {
                int index = parameters.IndexOf(parameter);
                return new Slot
                {
                    Parameter = parameter,
                    IsReturn = isReturn,
                    Default = ArgumentNameOrRaw(target, parameter),
                    Preset = presets != null && index < presets.Count ? presets[index] : null,
                };
            }
            var slots = parameters.Where(p => p != returned).Select(p => NewSlot(p, false)).ToList();
            if (returned != null && !assignment.Success && statement)
                slots.Add(NewSlot(returned, true));
            bool toUdp = returned != null && site.Kind == CallKind.Call;

            string callText = site.Name + (site.Kind == CallKind.Udp ? "." + UdpMethod : site.Kind == CallKind.Call ? ".call" : string.Empty);
            if (Settings.AskEachParameter && slots.Count > 0)
            {
                // Como el IntelliSense: se completa mientras se escribe, lugar por lugar
                Session.Start(new Plan
                {
                    Context = context,
                    Site = site,
                    Target = target,
                    ParameterCount = parameters.Count,
                    Slots = slots,
                    Returned = returned,
                    AssignedName = returned != null && assignment.Success ? assignment.Groups["var"].Value : null,
                    CallText = callText,
                });
                return;
            }

            // Sin sugerir: lo predefinido o las variables de la regla parm
            var declarations = new Declarations();
            var arguments = new List<string>();
            string prefix = string.Empty;
            foreach (Slot slot in slots)
            {
                string argument = ToArgument(slot.Automatic, out string variable);
                if (variable != null)
                    declarations.Track(variable, Declare(context.Part, variable, slot.Parameter.Object));
                if (slot.IsReturn)
                    prefix = argument + " = ";
                else
                    arguments.Add(argument);
            }
            if (returned != null && assignment.Success)
            {
                string name = assignment.Groups["var"].Value;
                declarations.Track(name, Declare(context.Part, name, returned.Object));
            }

            if (declarations.Declared.Count > 0)
                MarkModified(context.Part);

            // El cursor queda en el primer lugar vacío (para escribirlo), o después del ")"
            string text = string.Join(", ", arguments);
            int empty = arguments.FindIndex(a => a.Length == 0);
            int caret = empty < 0 ? -1 : arguments.Take(empty).Sum(a => a.Length + 2);

            ApplyText(context, site, text, toUdp, prefix, caret);
            Log.Write($"completado {target.Name}: ({text}) salida={(returned == null ? "-" : prefix.Length > 0 ? prefix.Trim() : "asignada/expresión")}");
            Status(Summary(target, parameters.Count, declarations));
        }

        /// <summary>Un lugar de la llamada: un parámetro entre paréntesis o la variable que recibe la salida.</summary>
        sealed class Slot
        {
            public Parameter Parameter;
            /// <summary>La salida que devuelve el udp: se escribe como último argumento y al terminar sale a "&amp;X = ".</summary>
            public bool IsReturn;
            /// <summary>La variable que se llama como en la regla parm ("&amp;CliId").</summary>
            public string Default;
            public ParameterPreset Preset;

            public bool Fixed => Preset != null && !Preset.Ask;

            /// <summary>La sugerencia que queda elegida en la lista: solo un valor predefinido.</summary>
            public string Proposed => Preset != null && Preset.Ask ? Preset.Values.FirstOrDefault() ?? string.Empty : string.Empty;

            /// <summary>Lo que va sin preguntar (opción apagada o Ctrl+Enter): lo predefinido o la variable del parm.</summary>
            public string Automatic => Fixed ? Preset.Values[0] : Preset?.Values.FirstOrDefault() ?? Default;
        }

        /// <summary>Las variables declaradas al completar, para el resumen de la barra de estado.</summary>
        sealed class Declarations
        {
            public readonly List<string> Declared = new List<string>();
            public readonly List<string> Different = new List<string>();
            public readonly List<string> Undeclared = new List<string>();

            public void Track(string name, Declaration result)
            {
                switch (result)
                {
                    case Declaration.Declared: Declared.Add(name); break;
                    case Declaration.Different: Different.Add(name); break;
                    case Declaration.Undeclared: Undeclared.Add(name); break;
                }
            }
        }

        /// <summary>
        /// Lo que se sugiere para un lugar: los valores predefinidos, los del dominio enumerado (si es de entrada),
        /// las variables declaradas del mismo tipo, los miembros de SDTs / BCs de ese tipo (cuando ya se leyeron) y,
        /// al final, la variable del parámetro si todavía no existe.
        /// </summary>
        static List<ArgumentOption> SuggestionsFor(Context context, KBObject target, Slot slot, List<Candidate> variables, List<Candidate> members)
        {
            Parameter parameter = slot.Parameter;
            var source = parameter.Object as ITypedObject;
            string typeText = source != null ? TypeText(source) : string.Empty;

            var options = new List<ArgumentOption>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string text, string detail, ArgumentKind kind)
            {
                if (text.Length > 0 && options.Count < MaxOptions && seen.Add(text))
                    options.Add(new ArgumentOption { Text = text, Detail = detail, Kind = kind });
            }

            if (slot.Preset != null)
                foreach (string value in slot.Preset.Values)
                    Add(value, "predefinido para " + target.Name, ArgumentKind.Preset);

            Domain domain = !slot.IsReturn && parameter.Accessor == RuleDefinition.ParameterAccess.PARM_IN ? EnumeratedDomain(source) : null;
            if (domain != null)
            {
                string reference = DomainReference(context.Caller, domain);
                foreach (EnumValue value in EnumValuesOf(domain))
                    Add(reference + "." + value.Name, $"{value.Description} · valor de {domain.Name}", ArgumentKind.EnumValue);
            }

            if (source != null)
            {
                var sourceType = new TypeInfo(source);
                foreach (Candidate variable in variables)
                    if (variable.Type.Matches(sourceType))
                        Add(variable.Path, "variable · " + TypeText(variable.Type.Typed), ArgumentKind.Variable);
                foreach (Candidate member in members ?? new List<Candidate>())
                    if (member.Type.Matches(sourceType))
                        Add(member.Path, "miembro · " + TypeText(member.Type.Typed), ArgumentKind.Member);
            }

            // La del parámetro si todavía no existe: se sugiere, nunca se escribe sola
            string own = ToArgument(slot.Default, out string ownName);
            Variable existing = ownName != null ? context.Part.GetVariable(ownName) : null;
            if (ownName != null && (existing == null || existing.IsAutoDefined))
                Add(own, "nueva · se declara como " + typeText, ArgumentKind.NewVariable);
            return options;
        }

        static string AccessText(RuleDefinition.ParameterAccess access)
        {
            switch (access)
            {
                case RuleDefinition.ParameterAccess.PARM_IN: return "in";
                case RuleDefinition.ParameterAccess.PARM_OUT: return "out";
                default: return "inout";
            }
        }

        static readonly Regex IdentifierRegex = new Regex(@"^&?(?<name>[A-Za-z_]\w*)$", RegexOptions.CultureInvariant);

        // Palabras que no son variables aunque se escriban sin "&"
        static readonly HashSet<string> Literals = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "true", "false" };

        /// <summary>
        /// Lo escrito como argumento: un nombre ("Cliente" o "&amp;Cliente") es una variable, que se devuelve en
        /// <paramref name="variable"/> para declararla; cualquier otra cosa (LogTipo.Debug, &amp;Sdt.Item, 10,
        /// "texto"...) va tal cual. Vacío queda vacío.
        /// </summary>
        static string ToArgument(string text, out string variable)
        {
            variable = null;
            text = (text ?? string.Empty).Trim();
            Match match = IdentifierRegex.Match(text);
            if (!match.Success || (text[0] != '&' && Literals.Contains(text)))
                return text;
            string name = match.Groups["name"].Value;
            if (!Variable.IsValidName(name))
                return text;
            variable = name;
            return "&" + name;
        }

        // ---- Valores de dominios enumerados ----

        static Domain EnumeratedDomain(ITypedObject source)
        {
            Domain domain = source as Domain
                ?? (source as Attribute)?.DomainBasedOn
                ?? (source as IDomainTypedObject)?.DomainBasedOn
                ?? (source as IAttributeTypedObject)?.AttributeBasedOn?.DomainBasedOn;
            return domain != null && domain.IsEnumerated ? domain : null;
        }

        static List<EnumValue> EnumValuesOf(Domain domain)
        {
            try
            {
                return (domain.GetPropertyValue(Properties.ATT.EnumValues) as EnumValues)?.Values ?? new List<EnumValue>();
            }
            catch (Exception ex)
            {
                Log.Write($"valores de {domain.Name}: {ex.Message}");
                return new List<EnumValue>();
            }
        }

        /// <summary>
        /// Cómo se nombra el dominio desde el objeto actual: solo el nombre si GeneXus lo resuelve así (mismo
        /// módulo, o la raíz sin otro igual en el módulo), si no calificado ("Sistema.LogTipo").
        /// </summary>
        static string DomainReference(KBObject caller, Domain domain)
        {
            try
            {
                if (caller.Model.Objects.ResolveName(caller.Module, ObjClass.Domain, domain.Name, out ResolveResult result) && result.IsMatch && result.Match != null)
                    return result.Match.Id == domain.Id ? domain.Name : domain.QualifiedName.ToString();
                if (string.IsNullOrEmpty(domain.QualifiedName.ModuleName))
                    return domain.Name;
            }
            catch (Exception ex)
            {
                Log.Write($"ResolveName dominio {domain.Name}: {ex.Message}");
            }
            return domain.QualifiedName.ToString();
        }

        // ---- Miembros de SDTs / BCs ----

        /// <summary>Una variable del objeto o un miembro de una variable SDT / BC ("&amp;sdtRespuesta.Output") y su tipo.</summary>
        sealed class Candidate
        {
            public string Path;
            public TypeInfo Type;
        }

        const int MaxMemberDepth = 3;
        const int MaxCandidates = 2000;
        const int MaxOptions = 200;

        /// <summary>Las variables declaradas en el objeto (las autodefinidas no: todavía no tienen tipo propio).</summary>
        static List<Candidate> VariableCandidates(Context context)
        {
            var candidates = new List<Candidate>();
            foreach (Variable variable in context.Part.Variables)
                if (!variable.IsAutoDefined)
                    candidates.Add(new Candidate { Path = "&" + variable.Name, Type = new TypeInfo(variable) });
            return candidates;
        }

        /// <summary>
        /// Los miembros de las variables SDT / BC declaradas (no colecciones), con los subniveles simples y
        /// los miembros de tipo SDT / BC hasta <see cref="MaxMemberDepth"/> niveles. Los niveles colección no:
        /// se recorren con un ítem.
        /// </summary>
        static List<Candidate> MemberCandidates(Context context)
        {
            var candidates = new List<Candidate>();
            KBModel model = context.Caller.Model;
            foreach (Variable variable in context.Part.Variables)
            {
                if (variable.IsCollection || !IsStructured(variable.Type))
                    continue;
                try
                {
                    IStructureItem structure = ResolveStructure(model, Properties.ATT.GetDataType(variable));
                    if (structure != null)
                        AddMembers(model, structure, "&" + variable.Name + ".", 1, new HashSet<string> { Key(structure) }, candidates);
                }
                catch (Exception ex)
                {
                    Log.Write($"miembros de &{variable.Name}: {ex.Message}");
                }
            }
            return candidates;
        }

        static void AddMembers(KBModel model, IStructureItem structure, string prefix, int depth, HashSet<string> open, List<Candidate> into)
        {
            switch (structure)
            {
                case SDTLevel level when !level.IsCollection:
                    foreach (IStructureItem item in level.Items)
                    {
                        if (into.Count >= MaxCandidates)
                            return;
                        switch (item)
                        {
                            case SDTLevel child:
                                AddMembers(model, child, prefix + child.Name + ".", depth, open, into);
                                break;
                            case SDTItem member:
                                into.Add(new Candidate { Path = prefix + member.Name, Type = new TypeInfo(member) });
                                if (member.IsCollection || !IsStructured(member.Type) || depth >= MaxMemberDepth)
                                    break;
                                IStructureItem inner = ResolveStructure(model, Properties.SDTITEM.GetDataType(member));
                                if (inner != null && open.Add(Key(inner)))
                                {
                                    AddMembers(model, inner, prefix + member.Name + ".", depth + 1, open, into);
                                    open.Remove(Key(inner));
                                }
                                break;
                        }
                    }
                    break;

                case TransactionLevel level:
                    foreach (TransactionAttribute attribute in level.Attributes)
                        if (attribute.Attribute != null && into.Count < MaxCandidates)
                            into.Add(new Candidate { Path = prefix + attribute.Name, Type = new TypeInfo(attribute.Attribute) });
                    break;
            }
        }

        static bool IsStructured(eDBType type) =>
            type == eDBType.GX_SDT || type == eDBType.GX_BUSCOMP || type == eDBType.GX_BUSCOMP_LEVEL;

        /// <summary>
        /// El nivel del SDT o de la transacción al que apunta el tipo (como lo resuelve GeneXus): la raíz
        /// para un SDT o un BC, el nivel para "Sdt.Item" o "Trn.Nivel".
        /// </summary>
        static IStructureItem ResolveStructure(KBModel model, AttCustomType type)
        {
            try
            {
                StructureTypeReference reference = StructureTypeReference.Deserialize(type);
                if (reference == null)
                    return null;
                bool isCollection = false;
                eDBType infoType = eDBType.NONE;
                IStructureItem item = StructureInfoProvider.GetItem(model, reference, ref isCollection, ref infoType);
                return item is SDTLevel || item is TransactionLevel ? item : null;
            }
            catch (Exception ex)
            {
                Log.Write($"resolver {type}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Identifica una estructura para cortar ciclos.</summary>
        static string Key(IStructureItem structure)
        {
            switch (structure)
            {
                case SDTLevel level:
                    return "S" + level.ItemEntity.Type + ":" + level.ItemEntity.Id;
                case TransactionLevel level:
                    return "T" + level.Transaction.Id + ":" + level.Id;
                default:
                    return structure.GetType().Name + ":" + structure.Name;
            }
        }

        /// <summary>Inserta los argumentos, cambia call por udp y agrega "&amp;Salida = " como un solo deshacer.</summary>
        static void ApplyText(Context context, CallSite site, string arguments, bool toUdp, string prefix, int caretInArguments)
        {
            Document document = context.SyntaxEditor.Document;
            string inside = arguments + (site.HasClose ? string.Empty : ")");
            // En el lugar pedido de los argumentos, o después del ")"
            int caret = site.ArgsOffset + (caretInArguments >= 0 ? caretInArguments : arguments.Length + 1);

            s_Applying = true;
            document.UndoRedo.StartGroup(DocumentModificationType.Custom);
            try
            {
                // De derecha a izquierda, así los offsets de lo anterior siguen valiendo
                Replace(document, site.ArgsOffset, site.ArgsLength, inside);
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
                return source == null || SameTypeOrUnknown(existing, source) ? Declaration.Existing : Declaration.Different;
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

        /// <summary>
        /// El tipo de una variable, un miembro de SDT o un atributo, para compararlo con el del parámetro. Cada
        /// dato se calcula una sola vez y solo si hace falta (leer el dominio o el SDT puede cargar objetos).
        /// </summary>
        sealed class TypeInfo
        {
            public readonly ITypedObject Typed;
            readonly Lazy<int?> m_DomainId;
            readonly Lazy<int?> m_AttributeId;
            readonly Lazy<string> m_Structure;
            readonly Lazy<string> m_DataType;

            public TypeInfo(ITypedObject typed)
            {
                Typed = typed;
                m_DomainId = new Lazy<int?>(() => DomainOf(typed)?.Id);
                m_AttributeId = new Lazy<int?>(() => AttributeOf(typed)?.Id);
                m_Structure = new Lazy<string>(() => StructureKey(CustomType(typed)));
                m_DataType = new Lazy<string>(() => new DataType(typed).ToString());
            }

            /// <summary>
            /// Si es del tipo de <paramref name="source"/> (el parámetro): el mismo dominio, el mismo atributo, el
            /// mismo SDT / BC o el mismo tipo básico, y los dos colección o ninguno.
            /// </summary>
            public bool SameAs(TypeInfo source)
            {
                if (source.Typed is Domain domain)
                    return m_DomainId.Value == domain.Id;
                if (Typed.IsCollection != source.Typed.IsCollection)
                    return false;
                if (IsStructured(Typed.Type) || IsStructured(source.Typed.Type))
                    return m_Structure.Value != null && m_Structure.Value == source.m_Structure.Value;
                if (source.m_AttributeId.Value != null && m_AttributeId.Value == source.m_AttributeId.Value)
                    return true;
                return m_DataType.Value == source.m_DataType.Value;
            }

            /// <summary>Para ofrecer una opción: si no se puede comparar, no se ofrece.</summary>
            public bool Matches(TypeInfo source)
            {
                try
                {
                    return SameAs(source);
                }
                catch (Exception)
                {
                    return false;
                }
            }

            /// <summary>
            /// El SDT / BC (o el nivel) al que apunta el tipo, igual aunque las referencias se hayan armado
            /// distinto; null si no se puede leer.
            /// </summary>
            static string StructureKey(AttCustomType type)
            {
                if (type == null)
                    return null;
                StructureTypeReference reference = StructureTypeReference.Deserialize(type);
                return reference == null ? null : $"{type.DataType}:{reference.Type}:{reference.Id}:{reference.AdditionalInfo}";
            }
        }

        /// <summary>Para avisar que una variable ya existía con otro tipo: si no se puede comparar, no avisar.</summary>
        static bool SameTypeOrUnknown(ITypedObject candidate, ITypedObject source)
        {
            try
            {
                return new TypeInfo(candidate).SameAs(new TypeInfo(source));
            }
            catch (Exception)
            {
                return true;
            }
        }

        static Attribute AttributeOf(ITypedObject typed) => typed as Attribute ?? (typed as IAttributeTypedObject)?.AttributeBasedOn;

        static Domain DomainOf(ITypedObject typed) => typed is Attribute attribute
            ? attribute.DomainBasedOn
            : (typed as IDomainTypedObject)?.DomainBasedOn ?? AttributeOf(typed)?.DomainBasedOn;

        static AttCustomType CustomType(ITypedObject typed)
        {
            switch (typed)
            {
                case SDTItem item: return Properties.SDTITEM.GetDataType(item);
                case IPropertyBag bag: return Properties.ATT.GetDataType(bag);
                default: return null;
            }
        }

        /// <summary>"Attribute:CliId", "sdtCliente, Ventas collection", "VarChar(100)"...</summary>
        static string TypeText(ITypedObject typed)
        {
            try
            {
                Attribute attribute = AttributeOf(typed);
                string text = attribute != null ? "Attribute:" + attribute.Name : new DataType(typed).ToString();
                return typed.IsCollection ? text + " collection" : text;
            }
            catch (Exception)
            {
                return string.Empty;
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

        static string Summary(KBObject target, int parameterCount, Declarations declarations)
        {
            var parts = new List<string>
            {
                parameterCount == 0 ? $"{target.Name} no tiene parámetros"
                    : parameterCount == 1 ? $"{target.Name}: 1 parámetro" : $"{target.Name}: {parameterCount} parámetros",
            };
            if (declarations.Declared.Count > 0)
                parts.Add("declaradas " + Names(declarations.Declared));
            if (declarations.Different.Count > 0)
                parts.Add("ya existían con otro tipo " + Names(declarations.Different));
            if (declarations.Undeclared.Count > 0)
                parts.Add("sin tipo para declarar " + Names(declarations.Undeclared));
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
