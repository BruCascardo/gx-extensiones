using System;
using System.Collections.Generic;

namespace GxCodeCheck
{
    /// <summary>Familias de tipos que GeneXus no mezcla: entre dos de distinta familia la especificación da error.</summary>
    enum TypeGroup { Unknown, Text, Number, Date, Bool, Guid, Sdt, Bc, Other }

    /// <summary>El tipo de una variable, un atributo, un parámetro o una expresión simple.</summary>
    sealed class TypeInfo
    {
        public static readonly TypeInfo Unknown = new TypeInfo(TypeGroup.Unknown, "?");
        public static readonly TypeInfo Text = new TypeInfo(TypeGroup.Text, "Character");
        public static readonly TypeInfo Number = new TypeInfo(TypeGroup.Number, "Numeric");
        public static readonly TypeInfo Date = new TypeInfo(TypeGroup.Date, "Date");
        public static readonly TypeInfo Bool = new TypeInfo(TypeGroup.Bool, "Boolean");

        public readonly TypeGroup Group;
        /// <summary>Cómo se muestra: "Numeric(4.0)", "VarChar(100)", "Cobranzas.sdtCupon"...</summary>
        public readonly string Display;
        /// <summary>El SDT o Business Component (nombre calificado), o null.</summary>
        public readonly string Structure;
        public readonly bool IsCollection;
        /// <summary>
        /// Si se sabe exactamente qué SDT es. De un nivel de un SDT (&amp;Sdt.Nivel) no: se sabe que es un SDT y si
        /// es colección, pero no se compara su nombre contra el de otro SDT.
        /// </summary>
        public readonly bool Exact;
        /// <summary>De dónde sacar sus miembros (<see cref="ITypeContext.Member"/>), o null.</summary>
        public readonly object Handle;
        /// <summary>Cuántos caracteres guarda un Character o VarChar, o 0 si no se sabe.</summary>
        public int Length;
        /// <summary>El dominio enumerado en que se basa (con sus valores), o null.</summary>
        public EnumDomain Enum;

        public TypeInfo(TypeGroup group, string text, string structure = null, bool isCollection = false, bool exact = true, object handle = null)
        {
            Group = group;
            Display = text;
            Structure = structure;
            IsCollection = isCollection;
            Exact = exact;
            Handle = handle;
        }

        public bool IsKnown => Group != TypeGroup.Unknown && Group != TypeGroup.Other;

        /// <summary>El tipo de cada elemento de la colección.</summary>
        public TypeInfo Element() => new TypeInfo(Group, Display, Structure, false, Exact, Handle) { Length = Length, Enum = Enum };

        /// <summary>
        /// Si GeneXus no acepta un valor de tipo <paramref name="value"/> donde se espera <paramref name="expected"/>.
        /// Ante la duda, no: un error que no existe confunde más que uno que se escapa (la especificación lo
        /// encuentra igual unos segundos después).
        /// </summary>
        public static bool Mismatch(TypeInfo expected, TypeInfo value)
        {
            if (expected == null || value == null || !expected.IsKnown || !value.IsKnown)
                return false;
            if (expected.IsCollection != value.IsCollection)
                return true;
            if (expected.Group != value.Group)
            {
                // Números y booleanos, o un Guid en un texto: no siempre es error
                var pair = new HashSet<TypeGroup> { expected.Group, value.Group };
                return !(pair.SetEquals(new[] { TypeGroup.Number, TypeGroup.Bool }) || pair.SetEquals(new[] { TypeGroup.Text, TypeGroup.Guid }));
            }
            if (expected.Group == TypeGroup.Sdt || expected.Group == TypeGroup.Bc)
                return expected.Exact && value.Exact && expected.Structure != null && value.Structure != null
                    && !string.Equals(expected.Structure, value.Structure, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        public override string ToString() => IsCollection ? "colección de " + Display : Display;
    }

    /// <summary>
    /// Lo que el chequeo de tipos necesita saber del objeto y de la KB. Se implementa contra la KB abierta
    /// (<see cref="KbTypeContext"/>) y, en las pruebas, con datos fijos.
    /// </summary>
    interface ITypeContext
    {
        /// <summary>El tipo de la variable (sin &amp;), o null si no está definida.</summary>
        TypeInfo Variable(string name);

        /// <summary>El tipo del atributo, o null si no hay un atributo con ese nombre.</summary>
        TypeInfo Attribute(string name);

        /// <summary>
        /// El tipo del miembro de un SDT (&amp;Sdt.Miembro), o null si no se sabe. <paramref name="missing"/>: se
        /// conoce el SDT y no tiene ese miembro.
        /// </summary>
        TypeInfo Member(TypeInfo owner, string name, out bool missing);

        /// <summary>Los nombres de los miembros del SDT (para sugerir uno parecido), o vacío.</summary>
        IEnumerable<string> Members(TypeInfo owner);

        /// <summary>El dominio con ese nombre (como está escrito), o null si no es un dominio.</summary>
        EnumDomain Domain(string name);

        /// <summary>El parm() del procedimiento, como lo resuelve GeneXus desde el objeto, o null si no es un procedimiento.</summary>
        CallSignature Procedure(string name);

        /// <summary>El parm() del objeto que se revisa, o null si no tiene.</summary>
        CallSignature Own();

        /// <summary>Si hay un objeto de la KB con ese nombre (como está escrito), visto desde el objeto que se revisa.</summary>
        bool ObjectExists(string name);
    }

    sealed class EnumDomain
    {
        public string Name;
        public TypeInfo Type;
        /// <summary>Los valores (sus nombres) si es enumerado, o null.</summary>
        public List<string> Values;
        /// <summary>El nombre de cada valor por su valor ("AC" → ACTIVO), si es enumerado.</summary>
        public readonly Dictionary<string, string> NameByValue = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>El nombre del valor que tiene el literal ("AC", 'AC' o 1), o null si no es uno de sus valores.</summary>
        public string NameOf(string literal) => NameByValue.TryGetValue(Unquote(literal), out string name) ? name : null;

        /// <summary>Sin las comillas y sin los espacios del final (un Character se compara así).</summary>
        public static string Unquote(string literal)
        {
            string text = literal.Trim();
            if (text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[text.Length - 1] == text[0])
                text = text.Substring(1, text.Length - 2);
            return text.TrimEnd();
        }
    }

    sealed class CallSignature
    {
        public string Name;
        public readonly List<CallParameter> Parameters = new List<CallParameter>();
    }

    sealed class CallParameter
    {
        /// <summary>Como está en el parm(): "&amp;Cliente", "CliId"...</summary>
        public string Name;
        public TypeInfo Type;
        public ParameterDirection Direction;
    }

    /// <summary>in:, out: o inout: (sin nada es inout).</summary>
    enum ParameterDirection { InOut, In, Out }

    static class ParameterDirectionText
    {
        public static string Of(ParameterDirection direction) => direction == ParameterDirection.In ? "in:" : direction == ParameterDirection.Out ? "out:" : "inout:";
    }
}
