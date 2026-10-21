using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ActiproSoftware.SyntaxEditor;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Objects;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Framework.Commands;
using Artech.Common.Helpers.Structure;
using Artech.Genexus.Common;
using Artech.Genexus.Common.CustomTypes;
using Artech.Genexus.Common.Parts;
using Artech.Genexus.Common.Parts.SDT;
using Artech.Genexus.Common.Parts.Variables;
using Artech.Genexus.Common.Types;
using Artech.Genexus.UI.Common.Editor;

namespace GxSdtExpand
{
    /// <summary>
    /// Expande la &amp;variable SDT o Business Component que está sola en la línea del cursor en
    /// "&amp;Variable = New()" y una asignación por cada miembro, con los "=" alineados:
    /// <code>
    /// &amp;inCliente = New()
    /// &amp;inCliente.CliId     =
    /// &amp;inCliente.CliNombre =
    /// </code>
    /// La expansión completa además recorre los miembros de tipo SDT / BC y expande cada colección
    /// en una variable del ítem (que declara) agregada con Add:
    /// <code>
    /// &amp;Telefono = New()
    /// &amp;Telefono.Numero =
    /// &amp;inCliente.Telefonos.Add(&amp;Telefono)
    /// </code>
    /// </summary>
    static class SdtExpander
    {
        // La &variable sola en la línea, o ya con "= New()"
        static readonly Regex LineRegex = new Regex(
            @"^(?<indent>[ \t]*)&(?<var>[A-Za-z_]\w*)(?<new>[ \t]*=[ \t]*New[ \t]*\([ \t]*\))?[ \t]*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        const string NewLine = "\n";   // SyntaxEditor guarda los saltos de línea como "\n"
        const int MaxDepth = 8;

        /// <summary>Los miembros de una estructura, con su ruta desde la variable ("Direccion.Calle").</summary>
        sealed class Members
        {
            public readonly List<string> Assignable = new List<string>();
            /// <summary>Colecciones de un tipo simple (expansión completa): "&amp;X.Tags.Add()".</summary>
            public readonly List<string> SimpleCollections = new List<string>();
            /// <summary>Colecciones de ítems estructurados (expansión completa): se expanden en una variable del ítem.</summary>
            public readonly List<Collection> Collections = new List<Collection>();
        }

        /// <summary>Una colección a expandir: la ruta, el ítem (su estructura y su tipo para declarar la variable).</summary>
        sealed class Collection
        {
            public string Path;
            public string ItemName;
            public AttCustomType ItemType;
            public IStructureItem Item;
        }

        /// <summary>Lo de una expansión: el objeto, el modo y lo que se fue declarando y salteando.</summary>
        sealed class Context
        {
            public VariablesPart Part;
            public KBModel Model;
            public bool Full;
            public string Indent;
            public int TabSize;
            public int Assignments;
            public int FirstAssignmentLine = -1;
            public readonly List<string> Lines = new List<string>();
            /// <summary>Variables de ítem usadas en esta expansión, con su tipo.</summary>
            public readonly Dictionary<string, AttCustomType> ItemVariables = new Dictionary<string, AttCustomType>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Declared = new List<string>();
            public readonly List<string> Undeclared = new List<string>();
            public readonly List<string> Skipped = new List<string>();
            public readonly List<string> ReadOnly = new List<string>();
            /// <summary>Estructuras que se están expandiendo (para no entrar en un ciclo de SDTs que se referencian).</summary>
            public readonly HashSet<string> Open = new HashSet<string>();
        }

        /// <summary>Comando / atajo: expande la variable de la línea del cursor.</summary>
        public static void Run(CommandData data, bool full)
        {
            try
            {
                GxTextEditor editor = EditorLocator.Find(data);
                if (editor == null || editor.ReadOnly)
                    return;
                SyntaxEditor syntaxEditor = EditorLocator.FindSyntaxEditor(editor);
                VariablesPart part = editor.GetVariablesPart();
                if (syntaxEditor == null || part == null)
                {
                    Log.Write($"sin contexto: syntaxEditor={syntaxEditor != null} variables={part != null}");
                    return;
                }
                Expand(syntaxEditor, part, full);
                syntaxEditor.Focus();
            }
            catch (Exception ex)
            {
                Log.Write("error: " + ex);
                CommonServices.Output.AddErrorLine("GxSdtExpand: error inesperado: " + ex);
            }
        }

        static void Expand(SyntaxEditor syntaxEditor, VariablesPart part, bool full)
        {
            Document document = syntaxEditor.Document;
            int caret = Math.Max(0, Math.Min(syntaxEditor.SelectedView.Selection.EndOffset, document.Length));
            DocumentLine line = document.Lines[document.OffsetToPosition(caret).Line];
            string text = line.EndOffset > line.StartOffset
                ? document.GetSubstring(new TextRange(line.StartOffset, line.EndOffset), LineTerminator.Newline)
                : string.Empty;

            Match match = LineRegex.Match(text);
            if (!match.Success)
            {
                Status("GxSdtExpand: poné el cursor en una línea que tenga solo la &variable");
                return;
            }

            string name = match.Groups["var"].Value;
            string target = "&" + name;
            Variable variable = part.GetVariable(name);
            if (variable == null)
            {
                Status($"GxSdtExpand: {target} no está declarada");
                return;
            }

            KBModel model = part.KBObject.Model;
            AttCustomType type = Properties.ATT.GetDataType(variable);
            IStructureItem structure = Resolve(model, type, out StructureTypeReference reference);
            Log.Write($"{target}: tipo {variable.Type} colección={variable.IsCollection} ref={Describe(reference)} → {(structure == null ? "sin estructura" : structure.GetType().Name + " " + structure.Name)}");
            if (structure == null)
            {
                Status(IsStructured(variable.Type)
                    ? $"GxSdtExpand: no encontré el tipo de {target} en la KB"
                    : $"GxSdtExpand: {target} no es un SDT ni un Business Component");
                return;
            }

            // Una colección: la variable misma (SDT collection) o un SDT cuya raíz es colección
            bool rootCollection = !variable.IsCollection && reference.Type == ObjClass.SDT && structure is SDTLevel root && root.IsCollection;
            if ((variable.IsCollection || rootCollection) && !full)
            {
                Status($"GxSdtExpand: {target} es una colección; {Shortcuts.Format(Settings.ExpandAllKey)} la expande con una variable del ítem");
                return;
            }

            var context = new Context
            {
                Part = part,
                Model = model,
                Full = full,
                Indent = match.Groups["indent"].Value,
                TabSize = document.TabSize > 0 ? document.TabSize : 4,
            };
            if (variable.IsCollection || rootCollection)
            {
                var collection = new Collection { Item = structure };
                if (rootCollection)
                {
                    SDTLevel level = (SDTLevel)structure;
                    collection.ItemName = ItemName(level.CollectionItemName, level.Name);
                    collection.ItemType = SdtLevelType(level);
                }
                else
                {
                    collection.ItemName = ItemName(null, StructureInfoProvider.GetName(model, reference));
                    collection.ItemType = type;
                }
                EmitCollection(context, target, collection, separate: false);
            }
            else
            {
                context.Open.Add(Key(structure));
                EmitBlock(context, target, ReadMembers(context, structure, string.Empty), Settings.AddNew || match.Groups["new"].Success);
            }

            if (context.Assignments == 0 && context.Lines.Count <= 1)
            {
                Status($"GxSdtExpand: {target} no tiene miembros para asignar");
                return;
            }
            if (context.Declared.Count > 0)
                MarkModified(part);

            ApplyText(syntaxEditor, line, context);
            Log.Write($"expandida {target} ({(full ? "completa" : "simple")}): {context.Assignments} asignación(es), declaradas [{string.Join(", ", context.Declared)}], salteadas [{string.Join(", ", context.Skipped)}], solo lectura [{string.Join(", ", context.ReadOnly)}]");
            Status(Summary(target, context));
        }

        /// <summary>Reemplaza la línea por la expansión en un solo deshacer y deja el cursor en la primera asignación.</summary>
        static void ApplyText(SyntaxEditor syntaxEditor, DocumentLine line, Context context)
        {
            Document document = syntaxEditor.Document;
            int caretLine = context.FirstAssignmentLine >= 0 ? context.FirstAssignmentLine : context.Lines.Count - 1;
            int caret = line.StartOffset + context.Lines.Take(caretLine + 1).Sum(l => l.Length + NewLine.Length) - NewLine.Length;

            document.UndoRedo.StartGroup(DocumentModificationType.Custom);
            try
            {
                document.ReplaceText(DocumentModificationType.Custom, line.StartOffset, line.EndOffset - line.StartOffset,
                    string.Join(NewLine, context.Lines), DocumentModificationOptions.CheckReadOnly);
            }
            finally
            {
                document.UndoRedo.EndGroup();
            }
            syntaxEditor.SelectedView.Selection.SelectRange(caret, 0);
        }

        // ---- Texto ----

        /// <summary>"&amp;X = New()", las asignaciones alineadas, los Add de colecciones simples y cada colección expandida.</summary>
        static void EmitBlock(Context context, string target, Members members, bool addNew)
        {
            if (addNew)
                context.Lines.Add(context.Indent + target + " = New()");
            if (members.Assignable.Count > 0)
            {
                if (context.FirstAssignmentLine < 0)
                    context.FirstAssignmentLine = context.Lines.Count;
                context.Lines.AddRange(Assignments(context.Indent, members.Assignable.Select(m => target + "." + m).ToList(), Settings.AlignWithTabs, context.TabSize));
                context.Assignments += members.Assignable.Count;
            }
            foreach (string collection in members.SimpleCollections)
                context.Lines.Add(context.Indent + target + "." + collection + ".Add()");
            foreach (Collection collection in members.Collections)
                EmitCollection(context, target + "." + collection.Path, collection, separate: true);
        }

        /// <summary>
        /// La variable del ítem (declarada con el tipo del ítem), su expansión y el Add a la colección.
        /// </summary>
        static void EmitCollection(Context context, string collectionTarget, Collection collection, bool separate)
        {
            string key = Key(collection.Item);
            if (!context.Open.Add(key) || context.Open.Count > MaxDepth)
            {
                context.Open.Remove(key);
                context.Skipped.Add(collectionTarget);   // un SDT que se contiene a sí mismo
                return;
            }
            try
            {
                string item = "&" + ItemVariable(context, collection.ItemName, collection.ItemType);
                if (separate)
                    context.Lines.Add(string.Empty);
                EmitBlock(context, item, ReadMembers(context, collection.Item, string.Empty), addNew: true);
                context.Lines.Add(context.Indent + collectionTarget + ".Add(" + item + ")");
            }
            finally
            {
                context.Open.Remove(key);
            }
        }

        /// <summary>
        /// "&amp;X.Miembro = " con los "=" en la misma columna: con espacios, uno más que el miembro más
        /// largo; con tabs, en el primer tab stop que deja al menos un tab después de cada miembro.
        /// </summary>
        static IEnumerable<string> Assignments(string indent, List<string> targets, bool useTabs, int tabSize)
        {
            if (!useTabs)
            {
                int width = targets.Max(t => t.Length);
                return targets.Select(t => indent + t.PadRight(width) + " = ");
            }

            int Next(int column) => (column / tabSize + 1) * tabSize;
            int Column(string value) => value.Aggregate(0, (column, c) => c == '\t' ? Next(column) : column + 1);

            int[] ends = targets.Select(t => Column(indent + t)).ToArray();
            int stop = Next(ends.Max());
            return targets.Select((t, i) =>
            {
                var builder = new StringBuilder(indent + t);
                for (int column = ends[i]; column < stop; column = Next(column))
                    builder.Append('\t');
                return builder.Append("= ").ToString();
            });
        }

        // ---- Estructura ----

        /// <summary>
        /// El nivel del SDT o de la transacción al que apunta el tipo (como lo resuelve GeneXus): la raíz
        /// para un SDT o un BC, el nivel para "Sdt.Item" o "Trn.Nivel".
        /// </summary>
        static IStructureItem Resolve(KBModel model, AttCustomType type, out StructureTypeReference reference)
        {
            reference = null;
            try
            {
                reference = StructureTypeReference.Deserialize(type);
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

        static Members ReadMembers(Context context, IStructureItem structure, string prefix)
        {
            var members = new Members();
            switch (structure)
            {
                case SDTLevel level:
                    AddSdtItems(context, members, level.Items, prefix);
                    break;
                case TransactionLevel level:
                    AddTransactionLevel(context, members, level, prefix);
                    break;
            }
            return members;
        }

        /// <summary>
        /// Los miembros de un nivel del SDT. Los subniveles simples se recorren ("Direccion.Calle"). En la
        /// expansión completa también los miembros de tipo SDT / BC, y las colecciones se expanden; si no,
        /// las colecciones se saltean porque se llenan con Add.
        /// </summary>
        static void AddSdtItems(Context context, Members members, IEnumerable<IStructureItem> items, string prefix)
        {
            foreach (IStructureItem structureItem in items)
            {
                switch (structureItem)
                {
                    case SDTLevel level when level.IsCollection:
                        if (context.Full)
                            members.Collections.Add(new Collection
                            {
                                Path = prefix + level.Name,
                                ItemName = ItemName(level.CollectionItemName, level.Name),
                                ItemType = SdtLevelType(level),
                                Item = level,
                            });
                        else
                            context.Skipped.Add(prefix + level.Name);
                        break;

                    case SDTLevel level:
                        AddSdtItems(context, members, level.Items, prefix + level.Name + ".");
                        break;

                    case SDTItem member:
                        AddSdtMember(context, members, member, prefix);
                        break;
                }
            }
        }

        static void AddSdtMember(Context context, Members members, SDTItem member, string prefix)
        {
            string path = prefix + member.Name;
            IStructureItem structure = null;
            AttCustomType type = null;
            if (context.Full && IsStructured(member.Type))
            {
                type = Properties.SDTITEM.GetDataType(member);
                structure = Resolve(context.Model, type, out _);
            }

            if (member.IsCollection)
            {
                if (!context.Full)
                    context.Skipped.Add(path);
                else if (structure != null)
                    members.Collections.Add(new Collection { Path = path, ItemName = ItemName(member.CollectionItemName, member.Name), ItemType = type, Item = structure });
                else
                    members.SimpleCollections.Add(path);
                return;
            }

            // Un miembro de tipo SDT / BC: en la expansión completa se recorre como un subnivel
            string key = structure != null ? Key(structure) : null;
            if (key != null && context.Open.Count < MaxDepth && context.Open.Add(key))
            {
                try
                {
                    Members inner = ReadMembers(context, structure, path + ".");
                    members.Assignable.AddRange(inner.Assignable);
                    members.SimpleCollections.AddRange(inner.SimpleCollections);
                    members.Collections.AddRange(inner.Collections);
                }
                finally
                {
                    context.Open.Remove(key);
                }
                return;
            }
            members.Assignable.Add(path);
        }

        /// <summary>
        /// Los atributos de un nivel de la transacción. Los inferidos y las fórmulas no se pueden asignar
        /// en un Business Component; los subniveles son colecciones (se expanden en la expansión completa).
        /// </summary>
        static void AddTransactionLevel(Context context, Members members, TransactionLevel level, string prefix)
        {
            foreach (TransactionAttribute attribute in level.Attributes)
            {
                if (attribute.IsInferred || attribute.Attribute?.Formula != null)
                    context.ReadOnly.Add(attribute.Name);
                else
                    members.Assignable.Add(prefix + attribute.Name);
            }
            foreach (TransactionLevel child in level.Levels)
            {
                if (context.Full)
                    members.Collections.Add(new Collection
                    {
                        Path = prefix + child.Name,
                        ItemName = ItemName(null, child.Type),
                        ItemType = AttCustomType.CreateFromReference(
                            new StructureTypeReference(child.Transaction.Type, child.Transaction.Id, StructureInfoProvider.GetLevelPrimaryKey(child)),
                            eDBType.GX_BUSCOMP_LEVEL),
                        Item = child,
                    });
                else
                    context.Skipped.Add(prefix + child.Name);
            }
        }

        /// <summary>El tipo del ítem de un nivel colección del SDT ("Sdt.Item"), como lo arma GeneXus.</summary>
        static AttCustomType SdtLevelType(SDTLevel level) =>
            AttCustomType.CreateFromReference(new StructureTypeReference(level.ItemEntity.Type, level.ItemEntity.Id), eDBType.GX_SDT);

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

        static bool IsStructured(eDBType type) =>
            type == eDBType.GX_SDT || type == eDBType.GX_BUSCOMP || type == eDBType.GX_BUSCOMP_LEVEL;

        /// <summary>El nombre del ítem: el "Collection item name", o el último tramo del nombre ("Sdt.Item" → "Item").</summary>
        static string ItemName(string collectionItemName, string fallback)
        {
            string name = !string.IsNullOrWhiteSpace(collectionItemName) ? collectionItemName : fallback ?? string.Empty;
            int comma = name.IndexOf(',');
            if (comma >= 0)
                name = name.Substring(0, comma);
            name = name.Substring(name.LastIndexOf('.') + 1).Trim();
            return Variable.IsValidName(name) ? name : "Item";
        }

        static string Describe(StructureTypeReference reference) =>
            reference == null ? "-" : $"{reference.Type}/{reference.Id}{(reference.AdditionalInfo == null ? "" : " " + reference.AdditionalInfo)}";

        // ---- Variables de ítem ----

        /// <summary>
        /// La variable para el ítem de una colección: la que ya existe con ese nombre y tipo, o una nueva
        /// declarada con el tipo del ítem. Si el nombre está tomado por otro tipo, se numera (Item2, Item3...).
        /// </summary>
        static string ItemVariable(Context context, string baseName, AttCustomType type)
        {
            for (int number = 1; ; number++)
            {
                string name = number == 1 ? baseName : baseName + number;
                if (context.ItemVariables.TryGetValue(name, out AttCustomType used))
                {
                    if (SameType(used, type))
                        return name;
                    continue;
                }

                Variable existing = context.Part.GetVariable(name);
                if (existing != null && !existing.IsAutoDefined)
                {
                    if (existing.IsCollection || !SameType(Properties.ATT.GetDataType(existing), type))
                        continue;
                }
                else if (existing == null && (VariableDefinition.IsStandard(name) || VariableDefinition.IsNewStandard(context.Part, name)))
                {
                    continue;
                }
                else
                {
                    Declare(context, existing, name, type);
                }
                context.ItemVariables[name] = type;
                return name;
            }
        }

        static void Declare(Context context, Variable existing, string name, AttCustomType type)
        {
            try
            {
                Variable variable = existing ?? new Variable(name, context.Part);
                DataType.SetDefault(variable);
                variable.AttributeBasedOn = null;
                variable.DomainBasedOn = null;
                variable.IsCollection = false;
                Properties.ATT.SetDataType(variable, type);
                if (existing == null)
                    context.Part.Variables.Add(variable);
                else
                    variable.IsAutoDefined = false;
                context.Declared.Add(name);
            }
            catch (Exception ex)
            {
                Log.Write($"declarar &{name}: {ex.Message}");
                context.Undeclared.Add(name);
            }
        }

        static bool SameType(AttCustomType a, AttCustomType b)
        {
            if (a == null || b == null || a.DataType != b.DataType)
                return false;
            if (a.Equals(b))
                return true;
            try
            {
                StructureTypeReference ra = StructureTypeReference.Deserialize(a);
                StructureTypeReference rb = StructureTypeReference.Deserialize(b);
                return ra != null && rb != null && ra.Type == rb.Type && ra.Id == rb.Id
                    && string.Equals(ra.AdditionalInfo ?? string.Empty, rb.AdditionalInfo ?? string.Empty, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
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

        // ---- Barra de estado ----

        static string Summary(string target, Context context)
        {
            var parts = new List<string> { $"{target}: {context.Assignments} asignación(es)" };
            if (context.Declared.Count > 0)
                parts.Add("declaradas " + Names(context.Declared));
            if (context.Undeclared.Count > 0)
                parts.Add("no se pudieron declarar " + Names(context.Undeclared));
            if (context.Skipped.Count > 0)
                parts.Add((context.Full ? "sin expandir (se contienen a sí mismas) " : "colecciones sin expandir ") + string.Join(", ", context.Skipped));
            if (context.ReadOnly.Count > 0)
                parts.Add("inferidos/fórmulas omitidos " + string.Join(", ", context.ReadOnly.Distinct()));
            return "GxSdtExpand: " + string.Join(" · ", parts);
        }

        static string Names(IEnumerable<string> names) => string.Join(", ", names.Select(n => "&" + n));

        static void Status(string message)
        {
            if (UIServices.IsStatusBarAvailable)
                UIServices.StatusBar.DisplayText(message);
        }
    }
}
