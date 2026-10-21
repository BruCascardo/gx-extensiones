using System;
using System.Collections.Generic;
using System.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Common.Helpers.Structure;
using Artech.Genexus.Common;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts.SDT;
using Artech.Genexus.Common.Types;

namespace GxSdtJson
{
    /// <summary>Crea niveles y miembros de un SDT a partir de <see cref="SdtNode"/>.</summary>
    sealed class SdtWriter
    {
        const string FallbackType = "VarChar(40)";

        readonly KBModel m_Model;
        List<DataTypeCaps> m_Types;

        /// <summary>Lo que no se pudo importar tal cual (tipos que no existen en esta KB, nombres repetidos...).</summary>
        public List<string> Warnings { get; } = new List<string>();

        public int Created { get; private set; }

        public SdtWriter(KBModel model)
        {
            m_Model = model;
        }

        /// <summary>Borra todos los ítems del nivel.</summary>
        public static void Clear(SDTLevel level)
        {
            for (int i = level.Items.Count - 1; i >= 0; i--)
                level.Items.RemoveAt(i);
        }

        /// <summary>Colección y nombre del ítem de colección del SDT (nivel raíz).</summary>
        public static void ApplyRootCollection(SDTLevel root, SdtNode node)
        {
            root.IsCollection = node.Collection;
            if (node.Collection && !string.IsNullOrEmpty(node.CollectionItemName))
                root.CollectionItemName = node.CollectionItemName;
        }

        /// <summary>Agrega los ítems al final del nivel. Los que ya existen con el mismo nombre se saltean.</summary>
        public void AddItems(SDTLevel level, IEnumerable<SdtNode> items, string path = "")
        {
            foreach (SdtNode node in items)
            {
                string name = node.Name.Trim();
                string here = string.IsNullOrEmpty(path) ? name : path + "." + name;
                if (level.Items.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    Warnings.Add($"{here}: ya existe un ítem con ese nombre, no se agregó.");
                    continue;
                }

                if (node.IsLevel)
                    AddLevel(level, node, name, here);
                else
                    AddMember(level, node, name, here);
            }
        }

        void AddLevel(SDTLevel parent, SdtNode node, string name, string here)
        {
            // Mismo orden que el "Create SDT from JSON" de GeneXus: propiedades y después AddLevel
            var level = new SDTLevel(parent.SDTStructure) { Name = name };
            SetOptional(level, "idJsonName", node.JsonName, here);
            level.IsCollection = node.Collection;
            parent.AddLevel(level);
            if (node.Collection && !string.IsNullOrEmpty(node.CollectionItemName))
                level.CollectionItemName = node.CollectionItemName;
            SetOptional(level, "Description", node.Description, here);
            Created++;

            AddItems(level, node.Items, here);
        }

        void AddMember(SDTLevel parent, SdtNode node, string name, string here)
        {
            var item = new SDTItem(parent.SDTStructure) { Name = name };
            SetOptional(item, "idJsonName", node.JsonName, here);
            SetType(item, node, here);
            item.IsCollection = node.Collection;
            parent.AddItem(item);
            if (node.Collection && !string.IsNullOrEmpty(node.CollectionItemName))
                item.CollectionItemName = node.CollectionItemName;
            SetOptional(item, "Description", node.Description, here);
            Created++;
        }

        void SetType(SDTItem item, SdtNode node, string here)
        {
            if (TrySetType(item, node.Type))
                return;

            if (!string.IsNullOrEmpty(node.BaseType) && TrySetType(item, node.BaseType))
            {
                Warnings.Add($"{here}: no existe {node.Type} en esta KB, se usó su tipo {node.BaseType}.");
                return;
            }

            DataType.ParseInto(m_Model, FallbackType, item);
            Warnings.Add($"{here}: no se reconoce el tipo \"{node.Type}\", quedó {FallbackType}.");
        }

        bool TrySetType(SDTItem item, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            text = text.Trim();
            try
            {
                if (TryStripPrefix(ref text, "Attribute:") || TryStripPrefix(ref text, "Att:"))
                    return SetAttribute(item, text);

                if (TryStripPrefix(ref text, "Domain:"))
                    return SetDomain(item, text);

                // Tipos básicos ("VarChar(40)", "Numeric(10.2-)") y la forma de GeneXus para
                // SDTs/BCs/EOs ("sdtOutput, Sistema.Global")
                if (DataType.ParseInto(m_Model, text, item))
                    return true;

                // SDT/BC/EO escrito de otra forma ("Sistema.Global.sdtOutput" o el nombre solo)
                DataTypeCaps caps = FindType(text);
                if (caps != null && DataType.SetType(m_Model, caps, item))
                    return true;

                // Igual que la grilla: un nombre suelto puede ser un dominio; si no, un atributo
                // ("ItfFormato" sin "Attribute:")
                return SetDomain(item, text) || SetAttribute(item, text);
            }
            catch (Exception)
            {
                return false;
            }
        }

        bool SetAttribute(SDTItem item, string name)
        {
            var attribute = Artech.Genexus.Common.Objects.Attribute.Get(m_Model, name);
            if (attribute == null)
                return false;
            item.DomainBasedOn = null;
            item.AttributeBasedOn = attribute;
            return item.AttributeBasedOn != null;
        }

        bool SetDomain(SDTItem item, string name)
        {
            Domain domain = FindByName(name, n => Domain.Get(m_Model, n), () => Domain.GetAll(m_Model));
            if (domain == null)
                return false;
            item.AttributeBasedOn = null;
            item.DomainBasedOn = domain;
            return item.DomainBasedOn != null;
        }

        /// <summary>
        /// Busca por nombre calificado ("Sistema.Global.X"); si no, por el nombre solo siempre que
        /// haya uno solo (en otra KB el objeto puede estar en otro módulo).
        /// </summary>
        static T FindByName<T>(string name, Func<QualifiedName, T> get, Func<IEnumerable<T>> all) where T : KBObject
        {
            T exact = get(new QualifiedName(name));
            if (exact != null)
                return exact;
            string objectName = ObjectName(name);
            var matches = all().Where(o => string.Equals(o.Name, objectName, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        DataTypeCaps FindType(string text)
        {
            if (m_Types == null)
                m_Types = DataType.GetTypes(m_Model, typeof(SDTItem)).Where(c => c.Name != null).ToList();

            string objectName = ObjectName(text);

            DataTypeCaps exact = m_Types.FirstOrDefault(c =>
                string.Equals(c.Name.ToString(), text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(DataTypeHelper.GetTypeText(c.Name), text, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            var byName = m_Types
                .Where(c => string.Equals(c.Name.ObjectName, objectName, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            return byName.Count == 1 ? byName[0] : null;
        }

        void SetOptional(Artech.Common.Properties.IPropertyBag bag, string property, string value, string here)
        {
            if (string.IsNullOrEmpty(value))
                return;
            try
            {
                bag.SetPropertyValue(property, value);
            }
            catch (Exception ex)
            {
                Warnings.Add($"{here}: no se pudo poner {property} = \"{value}\" ({ex.Message}).");
            }
        }

        /// <summary>"sdtOutput, Sistema.Global" o "Sistema.Global.sdtOutput" → "sdtOutput".</summary>
        static string ObjectName(string text)
        {
            int comma = text.IndexOf(',');
            return comma >= 0 ? text.Substring(0, comma).Trim() : text.Substring(text.LastIndexOf('.') + 1);
        }

        static bool TryStripPrefix(ref string text, string prefix)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            text = text.Substring(prefix.Length).Trim();
            return true;
        }
    }
}
