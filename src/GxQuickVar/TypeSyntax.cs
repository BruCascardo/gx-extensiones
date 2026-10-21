using System;
using System.Text.RegularExpressions;

namespace GxQuickVar
{
    /// <summary>
    /// Texto que se escribe en el desplegable: "VarChar(100)", "SDTCliente collection",
    /// "SDTCliente[]", "Domain:Importe"...
    /// </summary>
    static class TypeSyntax
    {
        static readonly string[] CollectionSuffixes = { " collection", "[]" };

        /// <summary>Saca el sufijo de colección (si lo hay) y deja el resto recortado.</summary>
        public static bool StripCollection(ref string text)
        {
            text = text.Trim();
            foreach (string suffix in CollectionSuffixes)
            {
                if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring(0, text.Length - suffix.Length).Trim();
                    return true;
                }
            }
            return false;
        }

        /// <summary>Lo que se usa para filtrar: el nombre del tipo, sin parámetros ni sufijo.</summary>
        public static string BaseName(string text, out string suffix)
        {
            string trimmed = text.Trim();
            string withoutSuffix = trimmed;
            suffix = StripCollection(ref withoutSuffix) ? trimmed.Substring(withoutSuffix.Length) : string.Empty;

            int paren = withoutSuffix.IndexOf('(');
            return (paren >= 0 ? withoutSuffix.Substring(0, paren) : withoutSuffix).Trim();
        }

        public static bool HasParameters(string text) => text.IndexOf('(') >= 0;

        // "att RecId", "att:RecId", "Attribute:RecId" (el nombre puede estar vacío mientras se escribe).
        // Mientras se busca puede tener varias palabras: "att cli nom" → ClienteNombre
        static readonly Regex AttributeRegex = new Regex(@"^(?:att|attribute)(?:\s+|\s*:\s*)(.*?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public const string AttributePrefix = "att ";

        /// <summary>
        /// Si el texto es una referencia a atributo, devuelve el nombre (o lo escrito hasta ahora para buscarlo).
        /// "att " con el espacio ya cuenta, para empezar a buscar mientras se escribe.
        /// </summary>
        public static bool TryParseAttribute(string text, out string name)
        {
            string value = text.TrimStart();
            foreach (string suffix in CollectionSuffixes)
            {
                if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(0, value.Length - suffix.Length);
                    break;
                }
            }
            Match match = AttributeRegex.Match(value);
            name = match.Success ? match.Groups[1].Value : null;
            return match.Success;
        }
    }
}
