using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Controls;
using Artech.Architecture.UI.Framework.Core;
using Artech.Architecture.UI.Framework.Services;
using Artech.Common.Properties;
using Artech.Genexus.Common;
using Artech.Genexus.Common.Helpers;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using Artech.Packages.Patterns.Objects;
using Artech.Packages.Patterns.UI.Editor;

namespace GxQuickVar
{
    /// <summary>
    /// El desplegable de tipos para las variables de una instancia de pattern (WorkWithPlus): el nodo
    /// seleccionado en el árbol, o el que muestra Properties. El tipo se escribe igual que en el resto
    /// ("VarChar(100)", "att ClienteId", "sdtX"...) y se traduce a sus atributos: dataType (Basic o Based on),
    /// domain (dominio, atributo, SDT, BC u objeto externo) o basicType con su largo y decimales. Todo el
    /// cambio es un solo paso del deshacer del pattern (InstancePart.ExecuteUpdate).
    /// </summary>
    static class PatternVariables
    {
        const string DataType = "dataType";
        const string Domain = "domain";
        const string BasicType = "basicType";
        const string BasicLength = "basicLength";
        const string BasicDecimals = "basicDecimals";
        const string BasicCLength = "basicCLength";
        const string BasicLVCLength = "basicLVCLength";
        const string Basic = "Basic";
        const string BasedOn = "Based on";

        /// <summary>Los atributos de la definición del tipo que se escriben (el resto queda como estaba).</summary>
        static readonly string[] TypeAttributes = { DataType, Domain, BasicType, BasicLength, BasicDecimals, BasicCLength, BasicLVCLength };

        /// <summary>Los tipos básicos que acepta una variable de WorkWithPlus (enum de basicType).</summary>
        static readonly Dictionary<eDBType, string> BasicTypes = new Dictionary<eDBType, string>
        {
            { eDBType.NUMERIC, "Numeric" },
            { eDBType.VARCHAR, "VarChar" },
            { eDBType.CHARACTER, "Character" },
            { eDBType.DATE, "Date" },
            { eDBType.DATETIME, "DateTime" },
            { eDBType.Boolean, "Boolean" },
            { eDBType.LONGVARCHAR, "LongVarChar" },
            { eDBType.AUDIO, "Audio" },
            { eDBType.GUID, "GUID" },
            { eDBType.BITMAP, "Image" },
            { eDBType.VIDEO, "Video" },
            { eDBType.BINARY, "Blob" },
        };

        static readonly Font CodeFont = new Font("Consolas", 10f);

        /// <summary>
        /// Lo que hace el atajo de declarar con el foco en el editor de un pattern o en Properties, si lo
        /// seleccionado es una variable del pattern; si no, null.
        /// </summary>
        public static Action ForFocus(Control focused)
        {
            for (Control control = focused; control != null; control = control.Parent)
            {
                IPropertyInspector inspector = control as IPropertyInspector;
                if (inspector == null && !(control is CompositeInstanceEditorControl))
                    continue;
                inspector = inspector ?? PropertiesWindow();
                PatternInstanceElement element = inspector != null ? SelectedVariable(inspector) : null;
                if (element == null || IsReadOnly(control, element))
                    return null;
                Control anchor = control;
                return () => Guard(() => Declare(element, focused, anchor));
            }
            return null;
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

        static IPropertyInspector PropertiesWindow() =>
            UIServices.IsToolWindowsAvailable && UIServices.ToolWindows.TryGet(ToolWindowsGuid.PropertiesView, out IToolWindow window)
                ? window as IPropertyInspector
                : null;

        /// <summary>La variable del pattern que muestra Properties, si es una sola.</summary>
        static PatternInstanceElement SelectedVariable(IPropertyInspector inspector)
        {
            object[] selected = inspector.SelectedObjects;
            object single = selected != null && selected.Length > 0
                ? (selected.Length == 1 ? selected[0] : null)
                : inspector.SelectedObject;
            // En un objeto de solo lectura Properties muestra un envoltorio (ReadOnlyProperties): no es esta clase
            PatternInstanceElement element = (single as PatternInstanceElementAttributes)?.Element;
            return element != null && HasTypeDefinition(element) ? element : null;
        }

        static bool HasTypeDefinition(PatternInstanceElement element) =>
            TypeAttributes.All(name => element.Specification.TryFindAttribute(name) != null);

        static bool IsReadOnly(Control control, PatternInstanceElement element)
        {
            for (Control c = control; c != null; c = c.Parent)
            {
                if (c is CompositeInstanceEditorControl editor && editor.ReadOnly)
                    return true;
            }
            return ((KBObject)element.Instance).IsReadOnly;
        }

        static void Declare(PatternInstanceElement element, Control focused, Control anchor)
        {
            PatternInstanceElementAttributes attributes = element.Attributes;
            KBObject owner = element.Instance.OwnerObject ?? element.Instance;
            KBModel model = owner.Model;
            VariablesPart part = owner.Parts.Get<VariablesPart>();
            if (part == null)
            {
                VariableDeclarer.Status($"GxQuickVar: {owner.Name} no tiene variables, no se puede interpretar el tipo");
                return;
            }

            string name = attributes.GetPropertyValueString("name") ?? string.Empty;
            PopupOptions options = VariableDeclarer.CreateOptions(model, owner, name, null,
                text => Apply(element, model, part, text), Palette.For(focused?.BackColor ?? anchor.BackColor), CodeFont);
            string current = CurrentTypeText(model, attributes);
            if (current != null)
            {
                options.InitialText = current;
                options.Hint = null;
            }
            options.AlreadyDeclared = true;

            Rectangle area = anchor.RectangleToScreen(anchor.ClientRectangle);
            VariableDeclarer.ShowPopup(options,
                size => VariableDeclarer.PlaceNear(new Rectangle(area.Left, area.Top, area.Width, 0), size, alignRight: anchor is IPropertyInspector),
                anchor.FindForm());

            if (focused != null && !focused.IsDisposed && focused.CanFocus)
                focused.Focus();
        }

        /// <summary>Qué hay que escribir en cada atributo: el valor o null para dejarlo por defecto.</summary>
        sealed class Definition
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        }

        /// <summary>Interpreta el texto como lo hace el resto de GxQuickVar y lo escribe en el elemento.</summary>
        internal static string Apply(PatternInstanceElement element, KBModel model, VariablesPart part, string typeText)
        {
            // Una variable de prueba, que no se agrega a la parte: el mismo análisis que en el código y la grilla
            var probe = new Variable(part) { Name = "GxQuickVarPattern" };
            Artech.Genexus.Common.Types.DataType.SetDefault(probe);
            string error = VariableDeclarer.ApplyType(model, probe, typeText);
            if (error != null)
                return error;
            if (!ToDefinition(probe, out Definition definition, out error))
                return error;

            PatternInstanceElementAttributes attributes = element.Attributes;
            Dictionary<string, string> before = Snapshot(attributes);
            try
            {
                element.InstancePart.ExecuteUpdate("GxQuickVar: tipo de " + attributes.GetPropertyValueString("name"),
                    () => Write(attributes, definition));
                string wrong = Verify(attributes, definition);
                if (wrong != null)
                {
                    Restore(attributes, before);
                    return $"WorkWithPlus no aceptó el tipo ({wrong})";
                }
            }
            catch (Exception)
            {
                Restore(attributes, before);
                throw;
            }

            VariableDeclarer.Declared(model, attributes.GetPropertyValueString("name") ?? string.Empty, typeText, probe);
            return null;
        }

        /// <summary>La variable de prueba, en los términos de WorkWithPlus.</summary>
        static bool ToDefinition(Variable probe, out Definition definition, out string error)
        {
            definition = new Definition();
            error = null;
            if (probe.IsCollection)
            {
                error = "Las variables de WorkWithPlus no pueden ser colecciones";
                return false;
            }

            KBObject basedOn = (KBObject)probe.AttributeBasedOn ?? probe.DomainBasedOn ?? StructureHelper.GetAssociatedObject(probe);
            if (basedOn != null)
            {
                definition.Values[DataType] = BasedOn;
                definition.Values[Domain] = basedOn;
                foreach (string name in new[] { BasicType, BasicLength, BasicDecimals, BasicCLength, BasicLVCLength })
                    definition.Values[name] = null;
                return true;
            }

            if (!BasicTypes.TryGetValue(probe.Type, out string basicType))
            {
                error = $"WorkWithPlus no admite el tipo {VariableDeclarer.TypeText(probe)} en una variable; usá un dominio";
                return false;
            }
            if (probe.Signed)
            {
                error = "WorkWithPlus no admite números con signo en una variable; usá un dominio";
                return false;
            }
            definition.Values[DataType] = Basic;
            definition.Values[Domain] = null;
            definition.Values[BasicType] = basicType;
            definition.Values[BasicLength] = null;
            definition.Values[BasicDecimals] = null;
            definition.Values[BasicCLength] = null;
            definition.Values[BasicLVCLength] = null;
            switch (probe.Type)
            {
                case eDBType.NUMERIC:
                    definition.Values[BasicLength] = probe.Length;
                    definition.Values[BasicDecimals] = probe.Decimals;
                    break;
                case eDBType.VARCHAR:
                case eDBType.CHARACTER:
                    definition.Values[BasicCLength] = probe.Length;
                    break;
                case eDBType.LONGVARCHAR:
                    definition.Values[BasicLVCLength] = probe.Length;
                    break;
            }
            return true;
        }

        static void Write(PatternInstanceElementAttributes attributes, Definition definition)
        {
            // dataType primero: los demás atributos dependen de él (cuáles se ven y valen)
            foreach (string name in TypeAttributes)
            {
                object value = definition.Values[name];
                string bagName = attributes.AttributeNameToPropertyBagName(name);
                if (value == null)
                {
                    if (!attributes.IsPropertyDefault(bagName))
                        attributes.ResetProperty(bagName);
                }
                else if (value is KBObject obj)
                {
                    attributes.SetPropertyValue(name, obj);   // "domain" envuelve la referencia ("domainReference")
                }
                else
                {
                    attributes.SetPropertyValue(bagName, attributes.ConvertPropertyValueFromString<StringStorage>(bagName, Convert.ToString(value)));
                }
            }
        }

        /// <summary>El primer atributo que no quedó como se pidió, o null.</summary>
        static string Verify(PatternInstanceElementAttributes attributes, Definition definition)
        {
            foreach (string name in TypeAttributes)
            {
                object value = definition.Values[name];
                if (value is KBObject obj)
                {
                    if (!(attributes.GetPropertyValue(name) is KBObject actual) || actual.Key != obj.Key)
                        return name;
                }
                else if (value != null && attributes.GetPropertyValueString(attributes.AttributeNameToPropertyBagName(name)) != Convert.ToString(value))
                {
                    return name;
                }
            }
            return null;
        }

        /// <summary>Los atributos del tipo como texto (null = por defecto), para dejarlos como estaban si algo falla.</summary>
        static Dictionary<string, string> Snapshot(PatternInstanceElementAttributes attributes)
        {
            var values = new Dictionary<string, string>();
            foreach (string name in TypeAttributes)
            {
                string bagName = attributes.AttributeNameToPropertyBagName(name);
                values[bagName] = attributes.IsPropertyDefault(bagName) ? null : attributes.GetPropertyValueString(bagName);
            }
            return values;
        }

        static void Restore(PatternInstanceElementAttributes attributes, Dictionary<string, string> values)
        {
            foreach (KeyValuePair<string, string> value in values)
            {
                try
                {
                    if (value.Value == null)
                        attributes.ResetProperty(value.Key);
                    else
                        attributes.SetPropertyValue(value.Key, attributes.ConvertPropertyValueFromString<StringStorage>(value.Key, value.Value));
                }
                catch (Exception ex)
                {
                    CommonServices.Output.AddErrorLine($"GxQuickVar: no se pudo restaurar {value.Key}: {ex.Message}");
                }
            }
        }

        /// <summary>El tipo actual escrito como en el desplegable, o null si todavía no tiene uno.</summary>
        static string CurrentTypeText(KBModel model, PatternInstanceElementAttributes attributes)
        {
            if (attributes.GetPropertyValueString(DataType) == BasedOn)
            {
                switch (attributes.GetPropertyValue(Domain))
                {
                    case Artech.Genexus.Common.Objects.Attribute attribute:
                        return TypeSyntax.AttributePrefix + attribute.Name;
                    case KBObject obj:
                        return TypeCatalog.FindExact(model, obj.QualifiedName.ToString())?.Name ?? obj.QualifiedName.ToString();
                    default:
                        return null;
                }
            }

            string type = attributes.GetPropertyValueString(BasicType);
            switch (type)
            {
                case "Numeric":
                    return $"Numeric({Int(attributes, BasicLength)}.{Int(attributes, BasicDecimals)})";
                case "VarChar":
                case "Character":
                    return $"{type}({Int(attributes, BasicCLength)})";
                case "LongVarChar":
                    return $"LongVarChar({Int(attributes, BasicLVCLength)})";
                default:
                    return string.IsNullOrEmpty(type) ? null : type;
            }
        }

        static int Int(PatternInstanceElementAttributes attributes, string name) =>
            int.TryParse(attributes.GetPropertyValueString(name), out int value) ? value : 0;
    }
}
