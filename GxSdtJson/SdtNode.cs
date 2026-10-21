using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GxSdtJson
{
    /// <summary>
    /// Un nivel o un miembro de la estructura de un SDT, sin depender de la KB. Es lo que se
    /// escribe y se lee como JSON; <see cref="SdtReader"/> y <see cref="SdtWriter"/> lo pasan
    /// desde y hacia los objetos de GeneXus.
    /// </summary>
    sealed class SdtNode
    {
        public string Name;
        /// <summary>Tipo como lo escribe GeneXus: "VarChar(40)", "Numeric(10.2-)", "Domain:Importe", "Attribute:CliId", "SDTCliente".</summary>
        public string Type;
        /// <summary>Para dominios y atributos: su tipo, por si en la KB de destino no existen.</summary>
        public string BaseType;
        public string Description;
        public string JsonName;
        public bool Collection;
        public string CollectionItemName;
        /// <summary>Null en un miembro; en un nivel (aunque esté vacío), sus hijos.</summary>
        public List<SdtNode> Items;

        public bool IsLevel => Items != null;

        public int CountMembers() => Items == null ? 1 : Items.Sum(i => i.CountMembers());
    }

    /// <summary>
    /// El formato JSON:
    /// <code>
    /// {
    ///   "name": "SDTCliente",
    ///   "description": "Cliente",
    ///   "items": [
    ///     { "name": "Id", "type": "Numeric(8.0)" },
    ///     { "name": "Importe", "type": "Domain:Importe", "baseType": "Numeric(12.2)" },
    ///     { "name": "Tags", "type": "VarChar(20)", "collection": true },
    ///     { "name": "Telefonos", "collection": true, "collectionItemName": "Telefono", "items": [ ... ] }
    ///   ]
    /// }
    /// </code>
    /// Un elemento con "items" es un nivel; sin "items", un miembro. Al leer también se acepta
    /// directamente el arreglo de ítems, y el sufijo " collection" o "[]" en el tipo.
    /// </summary>
    static class SdtJsonFormat
    {
        public static string Write(SdtNode root)
        {
            JObject json = new JObject();
            json["name"] = root.Name;
            if (!string.IsNullOrEmpty(root.Description))
                json["description"] = root.Description;
            WriteCollection(json, root);
            json["items"] = new JArray(root.Items.Select(WriteItem));
            return json.ToString(Formatting.Indented);
        }

        /// <summary>Solo algunos ítems (sin el SDT): el arreglo, que <see cref="Read"/> también acepta.</summary>
        public static string WriteItems(IEnumerable<SdtNode> items) =>
            new JArray(items.Select(WriteItem)).ToString(Formatting.Indented);

        static JObject WriteItem(SdtNode node)
        {
            JObject json = new JObject();
            json["name"] = node.Name;
            if (!node.IsLevel)
            {
                json["type"] = node.Type;
                if (!string.IsNullOrEmpty(node.BaseType))
                    json["baseType"] = node.BaseType;
            }
            if (!string.IsNullOrEmpty(node.Description))
                json["description"] = node.Description;
            if (!string.IsNullOrEmpty(node.JsonName))
                json["jsonName"] = node.JsonName;
            WriteCollection(json, node);
            if (node.IsLevel)
                json["items"] = new JArray(node.Items.Select(WriteItem));
            return json;
        }

        static void WriteCollection(JObject json, SdtNode node)
        {
            if (!node.Collection)
                return;
            json["collection"] = true;
            if (!string.IsNullOrEmpty(node.CollectionItemName))
                json["collectionItemName"] = node.CollectionItemName;
        }

        /// <summary>Lee el JSON. Tira <see cref="FormatException"/> con un mensaje para mostrar si no es válido.</summary>
        public static SdtNode Read(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("Pegá el JSON de la estructura.");

            JToken token;
            try
            {
                token = JToken.Parse(text);
            }
            catch (JsonReaderException ex)
            {
                throw new FormatException($"JSON inválido (línea {ex.LineNumber}, posición {ex.LinePosition}): {FirstSentence(ex.Message)}");
            }

            SdtNode root;
            if (token is JArray array)
            {
                root = new SdtNode { Items = ReadItems(array, "") };
            }
            else if (token is JObject obj)
            {
                if (!(obj["items"] is JArray))
                    throw new FormatException("Falta \"items\": el JSON tiene que ser el que genera \"Copiar SDT como JSON\" (un objeto con \"items\") o directamente el arreglo de ítems.");
                root = ReadNode(obj, "", isRoot: true);
            }
            else
            {
                throw new FormatException("El JSON tiene que ser un objeto con \"items\" o un arreglo de ítems.");
            }

            if (root.Items.Count == 0)
                throw new FormatException("La estructura no tiene ítems.");
            return root;
        }

        static SdtNode ReadNode(JObject obj, string path, bool isRoot)
        {
            var node = new SdtNode
            {
                Name = String(obj, "name", path),
                Description = String(obj, "description", path),
                JsonName = String(obj, "jsonName", path),
                CollectionItemName = String(obj, "collectionItemName", path),
                Collection = Bool(obj, "collection", path),
            };
            string here = isRoot ? "" : Path(path, node.Name);
            if (!isRoot && string.IsNullOrWhiteSpace(node.Name))
                throw new FormatException($"Hay un ítem sin \"name\" en {Where(path)}.");

            JToken items = obj["items"];
            if (items != null && items.Type != JTokenType.Null)
            {
                if (!(items is JArray itemsArray))
                    throw new FormatException($"\"items\" de {Where(here)} tiene que ser un arreglo.");
                node.Items = ReadItems(itemsArray, here);
                return node;
            }

            string type = String(obj, "type", path);
            if (string.IsNullOrWhiteSpace(type))
                throw new FormatException($"Falta \"type\" en {Where(here)} (o \"items\" si es un nivel).");
            node.Collection |= StripCollection(ref type);
            node.Type = type;
            node.BaseType = String(obj, "baseType", path);
            return node;
        }

        static List<SdtNode> ReadItems(JArray array, string path)
        {
            var items = new List<SdtNode>();
            foreach (JToken item in array)
            {
                if (!(item is JObject itemObj))
                    throw new FormatException($"Los elementos de \"items\" en {Where(path)} tienen que ser objetos.");
                items.Add(ReadNode(itemObj, path, isRoot: false));
            }

            string duplicated = items
                .GroupBy(i => i.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .FirstOrDefault();
            if (duplicated != null)
                throw new FormatException($"\"{duplicated}\" está repetido en {Where(path)}.");
            return items;
        }

        static readonly string[] CollectionSuffixes = { " collection", "[]" };

        static bool StripCollection(ref string type)
        {
            type = type.Trim();
            foreach (string suffix in CollectionSuffixes)
            {
                if (type.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    type = type.Substring(0, type.Length - suffix.Length).Trim();
                    return true;
                }
            }
            return false;
        }

        static string String(JObject obj, string key, string path)
        {
            JToken value = obj[key];
            if (value == null || value.Type == JTokenType.Null)
                return null;
            if (value.Type != JTokenType.String)
                throw new FormatException($"\"{key}\" tiene que ser texto en {Where(Path(path, obj["name"]?.ToString()))}.");
            return ((string)value).Trim();
        }

        static bool Bool(JObject obj, string key, string path)
        {
            JToken value = obj[key];
            if (value == null || value.Type == JTokenType.Null)
                return false;
            if (value.Type != JTokenType.Boolean)
                throw new FormatException($"\"{key}\" tiene que ser true o false en {Where(Path(path, obj["name"]?.ToString()))}.");
            return (bool)value;
        }

        static string Path(string parent, string name) =>
            string.IsNullOrEmpty(parent) ? name ?? "?" : parent + "." + (name ?? "?");

        static string Where(string path) => string.IsNullOrEmpty(path) ? "la raíz" : path;

        static string FirstSentence(string message)
        {
            int cut = message.IndexOf(". ", StringComparison.Ordinal);
            return cut > 0 ? message.Substring(0, cut + 1) : message;
        }
    }
}
