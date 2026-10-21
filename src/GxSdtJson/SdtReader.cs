using System;
using System.Collections.Generic;
using System.Linq;
using Artech.Common.Helpers.Structure;
using Artech.Common.Properties;
using Artech.Genexus.Common;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using Artech.Genexus.Common.Parts.SDT;
using Artech.Genexus.Common.Types;

namespace GxSdtJson
{
    /// <summary>Pasa la estructura de un SDT a <see cref="SdtNode"/>.</summary>
    static class SdtReader
    {
        public static SdtNode Read(SDT sdt, SDTStructurePart structure)
        {
            SDTLevel root = structure.Root;
            return new SdtNode
            {
                Name = sdt.Name,
                Description = string.Equals(sdt.Description, sdt.Name, StringComparison.Ordinal) ? null : sdt.Description,
                Collection = root.IsCollection,
                CollectionItemName = root.IsCollection ? NonDefault(root, "idCollectionItemName") : null,
                Items = ReadItems(root),
            };
        }

        static List<SdtNode> ReadItems(SDTLevel level) => ReadItems(level.Items);

        /// <summary>Ítems sueltos (una parte de la estructura, por ejemplo lo seleccionado en la grilla).</summary>
        public static List<SdtNode> ReadItems(IEnumerable<IStructureItem> items) =>
            items.Select(ReadItem).Where(n => n != null).ToList();

        static SdtNode ReadItem(IStructureItem structureItem)
        {
            switch (structureItem)
            {
                case SDTLevel level:
                    return new SdtNode
                    {
                        Name = level.Name,
                        Description = NonDefault(level, "Description"),
                        JsonName = NonDefault(level, "idJsonName"),
                        Collection = level.IsCollection,
                        CollectionItemName = level.IsCollection ? NonDefault(level, "idCollectionItemName") : null,
                        Items = ReadItems(level),
                    };

                case SDTItem item:
                    var node = new SdtNode
                    {
                        Name = item.Name,
                        Description = NonDefault(item, "Description"),
                        JsonName = NonDefault(item, "idJsonName"),
                        Collection = item.IsCollection,
                        CollectionItemName = item.IsCollection ? NonDefault(item, "idCollectionItemName") : null,
                    };
                    ReadType(item, node);
                    return node;

                default:
                    return null;
            }
        }

        static void ReadType(SDTItem item, SdtNode node)
        {
            // "Based on" un atributo o dominio: se guarda la referencia y, aparte, su tipo
            // para poder importarlo en una KB donde no exista.
            var attribute = item.AttributeBasedOn;
            if (attribute != null)
            {
                node.Type = "Attribute:" + attribute.Name;
                node.BaseType = TypeText(() => new DataType(attribute).ToString());
                return;
            }
            Domain domain = item.DomainBasedOn;
            if (domain != null)
            {
                node.Type = "Domain:" + domain.QualifiedName;
                node.BaseType = TypeText(() => new DataType(domain).ToString());
                return;
            }
            node.Type = TypeText(() => new DataType(item).ToString()) ?? item.Type.ToString();
        }

        static string TypeText(Func<string> text)
        {
            try
            {
                return text();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>El valor de la propiedad si se cambió (no es el que pone GeneXus solo), o null.</summary>
        static string NonDefault(IPropertyBag bag, string property)
        {
            try
            {
                if (bag.IsPropertyDefault(property))
                    return null;
                string value = bag.GetPropertyValue(property)?.ToString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch (Exception)
            {
                return null;   // la propiedad no existe en este tipo de ítem / versión
            }
        }
    }
}
