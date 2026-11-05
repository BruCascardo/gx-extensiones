using System;
using System.Collections.Generic;
using System.Linq;
using GxCodeCheck;

static partial class AnalyzerTests
{
    static void RunTypeCheckerTests()
    {
        Run("tipos: método con parámetro de otro tipo y de más", MethodArguments);
        Run("tipos: llamada a un procedimiento contra su parm()", ProcedureCall);
        Run("tipos: procedimiento usado como función", ProcedureAsFunction);
        Run("tipos: asignación entre tipos distintos", Assignment);
        Run("tipos: variable no definida", UndefinedVariable);
        Run("tipos: colección donde va un valor", CollectionArgument);
        Run("tipos: atributo como parámetro", AttributeArgument);
        Run("tipos: miembros de un SDT", SdtMembers);
        Run("tipos: colecciones (Add, AddRange, Item) y SDT que son colección por su tipo", Collections);
        Run("tipos: miembro que no existe en el SDT", MissingMember);
        Run("tipos: valores de un dominio enumerado", DomainValues);
        Run("tipos: comparaciones entre tipos distintos", Comparisons);
        Run("tipos: operaciones entre términos", Expressions);
        Run("tipos: método mal escrito", MisspelledMethod);
        Run("tipos: los objetos que llama el código, para leerlos de antemano", CallNames);
        Run("tipos: parámetro in: que se asigna", InputAssigned);
        Run("tipos: procedimiento como función cuyo último parámetro es in:", NoReturn);
        Run("tipos: texto que no entra en la variable", Truncated);
        Run("tipos: condición que no es verdadera o falsa", NotBoolean);
        Run("tipos: llamada a un objeto que no existe", NotFoundObject);
        Run("tipos: lo que no entiende lo deja pasar", NoFalsePositives);
    }

    static readonly TypeInfo Character1 = new TypeInfo(TypeGroup.Text, "Character(1)") { Length = 1 };
    static readonly TypeInfo Character20 = new TypeInfo(TypeGroup.Text, "Character(20)") { Length = 20 };
    static readonly TypeInfo Numeric4 = new TypeInfo(TypeGroup.Number, "Numeric(4.0)");
    static readonly TypeInfo Numeric6 = new TypeInfo(TypeGroup.Number, "Numeric(6.0)");
    static readonly TypeInfo SdtCupon = new TypeInfo(TypeGroup.Sdt, "Cobranzas.sdtCupon", "Cobranzas.sdtCupon", handle: "sdtCupon");
    static readonly TypeInfo SdtDetalle = new TypeInfo(TypeGroup.Sdt, "Cobranzas.sdtCupon.Detalle", isCollection: true, exact: false, handle: "Detalle");
    // GeneXus.Common.Messages es colección por su tipo; Messages.Message es su elemento
    static readonly TypeInfo Messages = new TypeInfo(TypeGroup.Sdt, "GeneXus.Common.Messages", "GeneXus.Common.Messages", isCollection: true, handle: "Messages");
    static readonly TypeInfo Message = new TypeInfo(TypeGroup.Sdt, "GeneXus.Common.Messages", "GeneXus.Common.Messages", handle: "Messages");
    static readonly TypeInfo SdtOutput = new TypeInfo(TypeGroup.Sdt, "Sistema.sdtOutput", "Sistema.sdtOutput", handle: "sdtOutput");
    static readonly TypeInfo OutCrear = new TypeInfo(TypeGroup.Sdt, "Cobranzas.outCrear", "Cobranzas.outCrear", handle: "outCrear");
    static readonly EnumDomain EstadoEnum = new EnumDomain
    {
        Name = "domEstado",
        Values = new List<string> { "ACTIVO", "BAJA" },
        NameByValue = { ["AC"] = "ACTIVO", ["BA"] = "BAJA" },
    };
    static readonly TypeInfo Estado = new TypeInfo(TypeGroup.Text, "domEstado Character(2)") { Length = 2, Enum = EstadoEnum };
    static readonly TypeInfo OutReferencias = new TypeInfo(TypeGroup.Sdt, "Cobranzas.outCrearReferencias", "Cobranzas.outCrearReferencias", handle: "outCrear");

    sealed class FakeContext : ITypeContext
    {
        public readonly Dictionary<string, TypeInfo> Variables = new Dictionary<string, TypeInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cerrar"] = Character1,
            ["Letra"] = Character1,
            ["Texto"] = Character20,
            ["Pos"] = Numeric4,
            ["Id"] = Numeric6,
            ["Cupon"] = SdtCupon,
            ["Lista"] = new TypeInfo(TypeGroup.Number, "Numeric(6.0)", isCollection: true),
            ["Pgmname"] = Character20,
            ["outCrear"] = OutCrear,
            ["outCrearReferencias"] = OutReferencias,
            ["Mensaje"] = Message,
            ["Mensajes"] = Messages,
            ["Fecha"] = new TypeInfo(TypeGroup.Date, "Date"),
            ["Entrada"] = Numeric6,
            ["Estado"] = Estado,
            ["Salida"] = Character20,
            ["outNivel"] = new TypeInfo(TypeGroup.Sdt, "Cobranzas.outNivel", "Cobranzas.outNivel", handle: "outNivel"),
        };

        static readonly Dictionary<string, Dictionary<string, TypeInfo>> Structures = new Dictionary<string, Dictionary<string, TypeInfo>>
        {
            ["sdtCupon"] = new Dictionary<string, TypeInfo>(StringComparer.OrdinalIgnoreCase) { ["Id"] = Numeric6, ["Nombre"] = Character20, ["Detalle"] = SdtDetalle },
            ["outCrear"] = new Dictionary<string, TypeInfo>(StringComparer.OrdinalIgnoreCase) { ["Output"] = SdtOutput },
            // Output como nivel del SDT (no un ítem con otro SDT): solo se sabe que es un SDT que no es colección
            ["outNivel"] = new Dictionary<string, TypeInfo>(StringComparer.OrdinalIgnoreCase) { ["Output"] = new TypeInfo(TypeGroup.Sdt, "Cobranzas.outNivel.Output", exact: false, handle: "sdtOutput") },
            ["sdtOutput"] = new Dictionary<string, TypeInfo>(StringComparer.OrdinalIgnoreCase) { ["Messages"] = Messages, ["Success"] = new TypeInfo(TypeGroup.Bool, "Boolean") },
            ["Messages"] = new Dictionary<string, TypeInfo>(StringComparer.OrdinalIgnoreCase) { ["Id"] = Character20, ["Description"] = Character20 },
        };

        public readonly Dictionary<string, TypeInfo> Attributes = new Dictionary<string, TypeInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["CupNombre"] = Character20,
            ["CupId"] = Numeric6,
            ["CupEstado"] = Estado,
        };

        public TypeInfo Variable(string name) => Variables.TryGetValue(name, out TypeInfo type) ? type : null;

        public TypeInfo Attribute(string name) => Attributes.TryGetValue(name, out TypeInfo type) ? type : null;

        public TypeInfo Member(TypeInfo owner, string name, out bool missing)
        {
            missing = false;
            if (!(owner.Handle is string handle) || !Structures.TryGetValue(handle, out var members))
                return null;
            missing = !members.TryGetValue(name, out TypeInfo type);
            return type;
        }

        public IEnumerable<string> Members(TypeInfo owner) =>
            owner.Handle is string handle && Structures.TryGetValue(handle, out var members) ? members.Keys : Enumerable.Empty<string>();

        public EnumDomain Domain(string name) => name.Equals("domEstado", StringComparison.OrdinalIgnoreCase)
            ? new EnumDomain { Name = "domEstado", Type = new TypeInfo(TypeGroup.Text, "domEstado Character(3)"), Values = new List<string> { "ACTIVO", "BAJA" } }
            : null;

        // Los objetos de la KB que no son procedimientos con parm() conocido
        static readonly HashSet<string> Others = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WPCliente", "Cobranzas.WPCliente", "Calcula_submit" };

        public bool ObjectExists(string name) => Others.Contains(name) || Procedure(name) != null;

        public CallSignature Own()
        {
            var signature = new CallSignature { Name = "Propio" };
            signature.Parameters.Add(new CallParameter { Name = "&Entrada", Type = Numeric6, Direction = ParameterDirection.In });
            signature.Parameters.Add(new CallParameter { Name = "&Salida", Type = Character20, Direction = ParameterDirection.Out });
            return signature;
        }

        public CallSignature Procedure(string name)
        {
            if (name.Equals("Calcula", StringComparison.OrdinalIgnoreCase))
            {
                var calcula = new CallSignature { Name = name };
                calcula.Parameters.Add(new CallParameter { Name = "&A", Type = Numeric6, Direction = ParameterDirection.In });
                calcula.Parameters.Add(new CallParameter { Name = "&B", Type = Numeric6, Direction = ParameterDirection.In });
                return calcula;
            }
            if (name.Equals("Estado_Set", StringComparison.OrdinalIgnoreCase))
            {
                var set = new CallSignature { Name = name };
                set.Parameters.Add(new CallParameter { Name = "&Estado", Type = Estado, Direction = ParameterDirection.In });
                return set;
            }
            if (!name.Equals("Cupon_Get", StringComparison.OrdinalIgnoreCase) && !name.Equals("Cobranzas.Cupon_Get", StringComparison.OrdinalIgnoreCase))
                return null;
            var signature = new CallSignature { Name = name };
            signature.Parameters.Add(new CallParameter { Name = "&CupId", Type = Numeric6 });
            signature.Parameters.Add(new CallParameter { Name = "&sdtCupon", Type = SdtCupon });
            return signature;
        }
    }

    static List<Diagnostic> Types(string text) => TypeChecker.Run(new SourceText(text), new FakeContext()).Where(d => d.Origin == Origin.Types).ToList();

    static void MethodArguments()
    {
        // Con un argumento de más, los tipos quedan corridos: un solo error, que dice cuál sobra
        Diagnostic many = Types("&Letra = &Cerrar.CharAt(&Cerrar, 1)\n").Single();
        Equal(TypeChecker.TooMany, many.Code, "código");
        True(many.Message.Contains("CharAt() recibe 1 parámetro") && many.Message.Contains("sobra el parámetro 1 ('&Cerrar')"), many.Message);
        Diagnostic wrong = Types("&Letra = &Cerrar.CharAt(&Cerrar)\n").Single();
        Equal(TypeChecker.WrongType, wrong.Code, "tipo");
        Equal(24, wrong.Column, "marca el argumento");
        Equal(7, wrong.Length, "'&Cerrar'");
        True(wrong.Message.Contains("Numeric") && wrong.Message.Contains("Character(1)"), wrong.Message);
        Equal(Origin.Types, wrong.Origin, "origen");
    }

    static void ProcedureCall()
    {
        var wrong = Types("Cupon_Get(&Texto, &Cupon)\n").Single();
        Equal(TypeChecker.WrongType, wrong.Code, "código");
        True(wrong.Message.Contains("&CupId") && wrong.Message.Contains("'&Texto'"), wrong.Message);

        var middle = Types("Cupon_Get(&Cupon)\n").Single();
        Equal(TypeChecker.TooFew, middle.Code, "falta el primero");
        True(middle.Message.Contains("falta el parámetro 1: &CupId (Numeric(6.0))"), middle.Message);

        var few = Types("Cupon_Get.Call(&Id)\n").Single();
        Equal(TypeChecker.TooFew, few.Code, "faltan");
        True(few.Message.Contains("usalo como función"), few.Message);

        Equal(0, Types("Cobranzas.Cupon_Get.Call(&Id, &Cupon)\nCupon_Get(&Id,\n\t&Cupon)\n").Count, "bien llamado, aunque siga en otra línea");
    }

    static void ProcedureAsFunction()
    {
        Equal(0, Types("&Cupon = Cupon_Get(&Id)\n").Count, "el último parámetro es el resultado");

        var many = Types("&Cupon = Cupon_Get(&Id, &Cupon)\n").Single();
        Equal(TypeChecker.TooMany, many.Code, "sobra el resultado");
        True(many.Message.Contains("el último parámetro del parm() es el resultado"), many.Message);

        var wrong = Types("&Texto = Cupon_Get(&Id)\n").Single();
        Equal(TypeChecker.AssignMismatch, wrong.Code, "devuelve un SDT");
    }

    static void Assignment()
    {
        var wrong = Types("&Id = \"abc\"\n").Single();
        Equal(TypeChecker.AssignMismatch, wrong.Code, "código");
        Equal(Severity.Error, wrong.Severity, "severidad");
        Equal(6, wrong.Column, "marca el valor");
        Equal(5, wrong.Length, "largo del string");

        Equal(0, Types("&Id = &Id + 1\n&Texto = &Id.ToString()\n&Pos = &Texto.Length()\n&Letra = &Texto.Trim().CharAt(1)\n").Count, "sin errores");
    }

    static void UndefinedVariable()
    {
        var result = Types("&Nada = 1\n&Texto = &Pgmname\n");
        var undefined = result.Single();
        Equal(TypeChecker.Undefined, undefined.Code, "código");
        Equal(Severity.Warning, undefined.Severity, "es advertencia");
        Equal(0, undefined.Line, "línea");
    }

    static void CollectionArgument()
    {
        var wrong = Types("Cupon_Get(&Lista, &Cupon)\n").Single();
        Equal(TypeChecker.WrongType, wrong.Code, "colección");
        True(wrong.Message.Contains("colección"), wrong.Message);
    }

    static void AttributeArgument()
    {
        var wrong = Types("For Each Cupon\n\tCupon_Get(CupNombre, &Cupon)\nEndFor\n").Single();
        Equal(TypeChecker.WrongType, wrong.Code, "atributo de texto");
        Equal(1, wrong.Line, "línea");
        Equal(0, Types("For Each Cupon\n\tCupon_Get(CupId, &Cupon)\nEndFor\n").Count, "atributo numérico");
    }

    static void SdtMembers()
    {
        // Como en la especificación: "Parameter &inCrear.Cuponid (Numeric) in udp/udf ... has wrong type"
        Equal(0, Types("Cupon_Get(&Cupon.Id, &Cupon)\n").Count, "miembro numérico donde va un número");
        var wrong = Types("Cupon_Get(&Id, &Cupon.Id)\n").Single();
        Equal(TypeChecker.WrongType, wrong.Code, "miembro numérico donde va el SDT");
        True(wrong.Message.Contains("'&Cupon.Id'"), wrong.Message);

        var assign = Types("&Cupon.Id = &Texto\n").Single();
        Equal(TypeChecker.AssignMismatch, assign.Code, "asignar a un miembro");
        True(assign.Message.Contains("'&Cupon.Id' es Numeric(6.0)"), assign.Message);

        Equal(0, Types("&Cupon.Id = &Id\n&Texto = &Cupon.Nombre.Trim()\n&Pos = &Cupon.Detalle.Count\n&Cupon.Detalle = &Cupon.Detalle\n").Count, "sin errores");
        Equal(1, Types("&Cupon.Detalle = &Id\n").Count, "un nivel no es un número");
    }

    static void Collections()
    {
        // El caso real: AddRange de Messages con el sdtOutput entero en lugar de su colección
        var wrong = Types("&outCrear.Output.Messages.AddRange(&outCrearReferencias.Output)\n").Single();
        Equal(TypeChecker.WrongType, wrong.Code, "código");
        True(wrong.Message.Contains("AddRange()") && wrong.Message.Contains("colección de GeneXus.Common.Messages") && wrong.Message.Contains("Sistema.sdtOutput"), wrong.Message);
        Equal(0, Types("&outCrear.Output.Messages.AddRange(&outCrearReferencias.Output.Messages)\n").Count, "bien");
        Equal(TypeChecker.WrongType, Types("&outNivel.Output.Messages.AddRange(&outNivel.Output)\n").Single().Code, "Output como nivel del SDT");
        Equal(0, Types("&outNivel.Output.Messages.AddRange(&outNivel.Output.Messages)\n").Count, "nivel, bien");

        Equal(TypeChecker.WrongType, Types("&Mensajes.Add(&Id)\n").Single().Code, "Add con un número");
        Equal(TypeChecker.WrongType, Types("&Lista.Add(&Texto)\n").Single().Code, "Add de texto en colección numérica");
        Equal(TypeChecker.WrongType, Types("&Mensajes.Add(&Mensajes)\n").Single().Code, "Add de la colección entera");
        Equal(TypeChecker.AssignMismatch, Types("&Id = &Mensajes.Item(1)\n").Single().Code, "Item devuelve el elemento");
        Equal(TypeChecker.AssignMismatch, Types("&Id = &Mensajes.Item(1).Description\n").Single().Code, "miembro del elemento");
    }

    static void MissingMember()
    {
        var missing = Types("&Texto = &Cupon.Nombr\n").Single();
        Equal(TypeChecker.MissingMember, missing.Code, "código");
        True(missing.Message.Contains("'Nombr'") && missing.Message.Contains("'Nombre'"), missing.Message);
        Equal(TypeChecker.MissingMember, Types("&outCrear.Output.Mesages.Clear()\n").Single().Code, "en una cadena");
    }

    static void DomainValues()
    {
        var invalid = Types("&Texto = domEstado.ACTVO\n").Single();
        Equal(TypeChecker.InvalidValue, invalid.Code, "código");
        True(invalid.Message.Contains("'ACTIVO'"), invalid.Message);
        Equal(TypeChecker.AssignMismatch, Types("&Id = domEstado.BAJA\n").Single().Code, "el valor es de texto");
        Equal(0, Types("If &Texto = domEstado.baja\nEndIf\n").Count, "sin distinguir mayúsculas");
    }

    static void Comparisons()
    {
        var wrong = Types("If &Texto = 1\nEndIf\n").Single();
        Equal(TypeChecker.CompareMismatch, wrong.Code, "código");
        True(wrong.Message.Contains("'&Texto'") && wrong.Message.Contains("'1'"), wrong.Message);
        Equal(TypeChecker.CompareMismatch, Types("For Each Cupon\n\tWhere CupId = &Texto\nEndFor\n").Single().Code, "en un Where");
        Equal(TypeChecker.CompareMismatch, Types("If &Id > 0 and &Fecha = &Texto\nEndIf\n").Single().Code, "la segunda");
        Equal(TypeChecker.CompareMismatch, Types("&Texto = iif(&Id = \"a\", \"si\", \"no\")\n").Single().Code, "dentro de una función");
    }

    static void Expressions()
    {
        Equal(TypeChecker.AssignMismatch, Types("&Texto = &Id + &Pos\n").Single().Code, "número en un texto");
        Equal(TypeChecker.AssignMismatch, Types("&Id = &Texto + \"x\"\n").Single().Code, "texto en un número");
        Equal(TypeChecker.AssignMismatch, Types("&Texto = &Id > 0\n").Single().Code, "booleano en un texto");
        Equal(TypeChecker.WrongType, Types("Cupon_Get(&Texto + \"1\", &Cupon)\n").Single().Code, "parámetro calculado");
    }

    static void MisspelledMethod()
    {
        var typo = Types("&Pos = &Texto.Lenght()\n").Single();
        Equal(TypeChecker.UnknownMethod, typo.Code, "código");
        Equal(Severity.Warning, typo.Severity, "advertencia");
        True(typo.Message.Contains("'Length'"), typo.Message);
    }

    static void CallNames()
    {
        var names = TypeChecker.CallNames(new SourceText("Cupon_Get(&Id, &Cupon)\n&Texto = Modulo.Otro.Udp(&Id)\nProc3.Call()\n&Texto = Trim(&Texto)\nIf (&Id > 0)\nEndIf\n// Comentado(1)\n"));
        Equal("Cupon_Get, Modulo.Otro, Proc3", string.Join(", ", names), "nombres");
    }

    static void InputAssigned()
    {
        var input = Types("&Entrada = 5\n").Single();
        Equal(TypeChecker.InputAssigned, input.Code, "código");
        Equal(Severity.Warning, input.Severity, "advertencia, como en GeneXus");
        Equal(0, Types("&Salida = \"a\"\n&Pos = &Entrada\n").Count, "out: y usarlo, sin aviso");
    }

    static void NoReturn()
    {
        var none = Types("&Id = Calcula(&Id)\n").Single();
        Equal(TypeChecker.NoReturn, none.Code, "código");
        True(none.Message.Contains("&B") && none.Message.Contains("in:"), none.Message);
        Equal(0, Types("Calcula.Call(&Pos, &Pos)\n").Count, "con Call no devuelve nada y está bien");
    }

    static void Truncated()
    {
        var cut = Types("&Letra = \"ABC\"\n").Single();
        Equal(TypeChecker.Truncated, cut.Code, "código");
        True(cut.Message.Contains("3 caracteres") && cut.Message.Contains("guarda 1"), cut.Message);
        Equal(0, Types("&Letra = \"A\"\n&Letra = ''''\n&Cerrar = \"\"\n").Count, "entra (una comilla doble cuenta una)");
    }

    static void NotBoolean()
    {
        var condition = Types("If &Texto\nEndIf\n").Single();
        Equal(TypeChecker.NotBoolean, condition.Code, "código");
        Equal(TypeChecker.NotBoolean, Types("Do While &Cupon\nEndDo\n").Single().Code, "Do While");
        Equal(TypeChecker.NotBoolean, Types("Do Case\n\tCase &Fecha\nEndCase\n").Single().Code, "Case");
        Equal(0, Types("If &Texto.IsEmpty() or not &Cupon.Detalle.Count > 0\nEndIf\nDo Case\n\tCase &Pos = 1\nEndCase\nIf &Pos\nEndIf\n").Count, "sin errores");
    }

    static void NotFoundObject()
    {
        Diagnostic missing = Types("Cupon_Gte.Call(&Id, &Cupon)\n").Single();
        Equal(TypeChecker.NotFound, missing.Code, "código");
        Equal(Severity.Error, missing.Severity, "rompe el build");
        Equal(0, missing.Column, "marca el nombre");
        Equal(9, missing.Length, "'Cupon_Gte'");
        True(Types("&Texto = Cobranzas.Nada.Udp(&Id)\n").Single().Message.Contains("'Cobranzas.Nada'"), "con módulo, usado como función");
        Equal(TypeChecker.NotFound, Types("Nada.Submit()\n").Single().Code, "submit");
        Equal(0, Types("WPCliente.Call()\nCobranzas.WPCliente.Call(&Id)\nCalcula_submit.Submit(&Id, &Id)\n").Count, "objetos que existen y no son procedimientos");
        Equal(0, Types("&Pos = NoEsObjeto(&Id)\nExterno.Metodo(&Id)\n").Count, "sin .Call puede ser una función o un objeto externo");
    }

    static void NoFalsePositives()
    {
        string text =
            "// &Letra = &Cerrar.CharAt(\"a\")\n" +
            "&Texto = \"&Letra.CharAt('a')\"\n" +
            "Otro_Proc(&Texto, 1, 2)\n" +
            "&Texto = Format(\"%1\", &Id)\n" +
            "&Id = Val(&Texto) * 2\n" +
            "If &Texto = \"1\" and &Id > 0\n" +
            "\t&Texto.Visible = false\n" +
            "EndIf\n" +
            "For &Pos = 1 To &Id\n" +
            "EndFor\n" +
            "&Texto = &Texto.Unknown\n" +
            "&Texto = \"Total: \" + &Id.ToString()\n" +
            "&Id = &Id * (&Pos - 1)\n" +
            "&Fecha = &Fecha + 1\n" +
            "&Texto = domEstado.ACTIVO\n" +
            "&Mensajes.Add(&Mensaje)\n" +
            "&Texto = &Mensajes.Item(1).Description\n" +
            "&Texto = &Texto.PadLeft(10, \"0\")\n" +
            "&Pos = &Lista.Count\n" +
            "&Texto = iif(&Id > 0, \"si\", \"no\")\n";
        var result = Types(text);
        Equal(0, result.Count, "sin errores: " + string.Join(" | ", result));
    }
}
