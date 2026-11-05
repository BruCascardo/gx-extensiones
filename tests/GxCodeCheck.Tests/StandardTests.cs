using System.Collections.Generic;
using System.Linq;
using GxCodeCheck;

static partial class AnalyzerTests
{
    static void RunStandardTests()
    {
        Run("estándar: literal donde va un valor de dominio", DomainLiterals);
        Run("estándar: .Clone() en vez de new()", CloneInstead);
        Run("estándar: Commit y Rollback directos", DirectCommit);
        Run("estándar: submit directo", DirectSubmit);
        Run("estándar: funciones y 'and' en un Where", WhereClauses);
        Run("estándar: .Trim() sobre un dominio", DomainTrim);
        Run("estándar: If dentro del Else", ElseIfChain);
        Run("estándar: Commit on exit", CommitOnExit);
        Run("estándar: el código que sigue el estándar no se marca", StandardClean);
    }

    static List<Diagnostic> Standards(string text) =>
        TypeChecker.Run(new SourceText(text), new FakeContext()).Where(d => d.Origin == Origin.Standard).ToList();

    static void DomainLiterals()
    {
        Diagnostic assign = Standards("&Estado = \"AC\"\n").Single();
        Equal(TypeChecker.DomainLiteral, assign.Code, "código");
        Equal(Severity.Warning, assign.Severity, "advertencia");
        True(assign.Message.Contains("usá domEstado.ACTIVO en vez de \"AC\""), assign.Message);
        Equal(10, assign.Column, "marca el literal");
        Equal(4, assign.Length, "\"AC\"");
        True(Standards("If &Estado = 'BA'\nEndIf\n").Single().Message.Contains("domEstado.BAJA"), "comparación");
        True(Standards("If \"AC\" <> &Estado\nEndIf\n").Single().Message.Contains("domEstado.ACTIVO"), "literal a la izquierda");
        True(Standards("&Estado = \"XX\"\n").Single().Message.Contains("no es uno de sus valores (ACTIVO, BAJA)"), "valor que no es del dominio");
        True(Standards("For Each\n\tWhere CupEstado = \"AC\"\nEndFor\n").Single().Message.Contains("domEstado.ACTIVO"), "atributo en un Where");
        Diagnostic parameter = Standards("Estado_Set(\"BA\")\n").Single();
        True(parameter.Message.Contains("parámetro 1 de Estado_Set (&Estado)") && parameter.Message.Contains("domEstado.BAJA"), parameter.Message);
        Equal(0, Standards("&Estado = \"\"\nIf &Estado = domEstado.ACTIVO\nEndIf\n&Texto = \"AC\"\n").Count, "vacío, valor del dominio o una variable sin dominio");
    }

    static void CloneInstead()
    {
        Diagnostic clone = Standards("&Mensajes.Add(&Mensaje.Clone())\n").Single();
        Equal(TypeChecker.CloneInstead, clone.Code, "código");
        Equal(23, clone.Column, "marca Clone");
    }

    static void DirectCommit()
    {
        Diagnostic commit = Standards("&Id = 1\nCommit\n").Single();
        Equal(TypeChecker.DirectCommit, commit.Code, "código");
        True(commit.Message.Contains("prcCommit.Call()"), commit.Message);
        True(Standards("rollback\n").Single().Message.Contains("prcRollback.Call()"), "rollback");
        Equal(0, Standards("prcCommit.Call()\nprcRollback.Call()\n").Count, "con los procedimientos");
    }

    static void DirectSubmit()
    {
        Diagnostic submit = Standards("Calcula.Submit(&Id, &Pos)\n").Single();
        Equal(TypeChecker.DirectSubmit, submit.Code, "código");
        True(submit.Message.Contains("creá Calcula_submit"), submit.Message);
        Equal(0, Standards("Calcula_submit.Submit(&Id, &Pos)\nCalcula.Call(&Id, &Pos)\n").Count, "por el _submit o sin submit");
    }

    static void WhereClauses()
    {
        var functions = Standards("For Each\n\tWhere CupNombre = &Texto.Trim()\n\tWhere CupId = Val(&Texto)\nEndFor\n");
        Equal(2, functions.Count, "un método y una función");
        True(functions.All(d => d.Code == TypeChecker.WhereFunction), "código");
        True(functions[0].Message.Contains("métodos") && functions[1].Message.Contains("funciones"), functions[0].Message + " / " + functions[1].Message);
        Equal(0, Standards("For Each\n\tWhere CupId = &Id When not &Texto.IsEmpty()\nEndFor\n").Count, "lo del When se calcula antes");

        Diagnostic and = Standards("For Each\n\tWhere CupId = &Id and CupNombre = &Texto\nEndFor\n").Single();
        Equal(TypeChecker.WhereAnd, and.Code, "and");
        Equal(19, and.Column, "marca el and");
        Equal(0, Standards("For Each\n\tWhere CupId = &Id and CupNombre = &Texto or CupId = 0\n\tWhere (CupId = &Id and CupNombre = &Texto) or CupId = 0\nEndFor\n").Count, "con un or no se puede separar");
    }

    static void DomainTrim()
    {
        Diagnostic trim = Standards("&Texto = &Estado.Trim()\n").Single();
        Equal(TypeChecker.DomainTrim, trim.Code, "código");
        True(trim.Message.StartsWith("'&Estado' es del dominio domEstado"), trim.Message);
        Equal(0, Standards("&Texto = &Texto.Trim()\n").Count, "un texto sin dominio");
    }

    static void ElseIfChain()
    {
        const string chain = "If &Id = 1\n\t&Pos = 1\nElse\n\tIf &Id = 2\n\t\t&Pos = 2\n\tElse\n\t\tIf &Id = 3\n\t\t\t&Pos = 3\n\t\tEndIf\n\tEndIf\nEndIf\n";
        Diagnostic elseIf = Standards(chain).Single();
        Equal(TypeChecker.ElseIf, elseIf.Code, "una sola advertencia por cadena");
        Equal(0, elseIf.Line, "en el primer If");
        True(elseIf.Message.Contains("2 If") && elseIf.Message.Contains("Do Case"), elseIf.Message);
        const string more = "If &Id = 1\n\t&Pos = 1\nElse\n\tIf &Id = 2\n\t\t&Pos = 2\n\tEndIf\n\t&Pos = 0\nEndIf\n";
        Equal(0, Standards(more).Count, "el Else hace algo más que el If");
        Equal(0, Standards("If &Id = 1\n\tIf &Pos = 2\n\t\t&Pos = 3\n\tEndIf\nEndIf\n").Count, "If dentro de If, sin Else");
    }

    static void CommitOnExit()
    {
        Diagnostic warning = TypeChecker.CommitOnExitWarning(new SourceText("\n// Comentario\n\t&Id = 1\n"));
        Equal(TypeChecker.CommitOnExit, warning.Code, "código");
        Equal(2, warning.Line, "la primera línea con código");
        Equal(1, warning.Column, "sin la sangría");
        Equal(7, warning.Length, "'&Id = 1'");
    }

    static void StandardClean()
    {
        const string code =
            "&Estado = domEstado.ACTIVO\n" +
            "&Cupon = new()\n" +
            "For Each\n" +
            "\tWhere CupId = &Id\n" +
            "\tWhere CupEstado = &Estado\n" +
            "\tDo Case\n" +
            "\t\tCase &Pos = 1\n" +
            "\t\t\t&Texto = \"uno\"\n" +
            "\t\tOtherwise\n" +
            "\t\t\t&Texto = \"otro\"\n" +
            "\tEndCase\n" +
            "EndFor\n" +
            "prcCommit.Call()\n";
        var found = Standards(code);
        Equal(0, found.Count, string.Join(" | ", found.Select(d => d.ToString())));
    }
}
