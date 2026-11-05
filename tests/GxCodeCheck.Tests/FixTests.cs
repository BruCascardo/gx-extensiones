using System.Collections.Generic;
using System.Linq;
using GxCodeCheck;

static partial class AnalyzerTests
{
    static void RunFixTests()
    {
        Run("correcciones: nombres mal escritos (miembro, método, valor de dominio)", FixMisspelled);
        Run("correcciones: declarar la variable no definida", FixUndefined);
        Run("correcciones: punto suelto y bloque sin cerrar", FixTextChecks);
        Run("correcciones: función mal escrita (parser)", FixUnknownFunction);
        Run("correcciones: estándar del equipo", FixStandards);
        Run("correcciones: se ubican aunque el texto cambió, y no se aplican si cambió ahí", FixLocate);
    }

    /// <summary>El texto con la única corrección del único error aplicada, como si no se hubiera editado nada.</summary>
    static string ApplyOnly(string text, List<Diagnostic> found)
    {
        Diagnostic d = found.Single(x => x.Fixes.Count > 0);
        return Apply(text, d, d.Fixes.Single(), Offset(text, d.Line, d.Column));
    }

    static string Apply(string text, Diagnostic d, Fix fix, int anchor)
    {
        True(fix.TryLocate(text, d, anchor, out int offset, out string replacement), "se ubica: " + fix);
        return text.Substring(0, offset) + replacement + text.Substring(offset + fix.Expected.Length);
    }

    static int Offset(string text, int line, int column)
    {
        int at = 0;
        for (int i = 0; i < line; i++)
            at = text.IndexOf('\n', at) + 1;
        return at + column;
    }

    static void False(bool condition, string what) => True(!condition, what);

    static List<Diagnostic> All(string text) => TypeChecker.Run(new SourceText(text), new FakeContext());

    static void FixMisspelled()
    {
        const string member = "&Texto = &Cupon.Nombr\n";
        Equal("&Texto = &Cupon.Nombre\n", ApplyOnly(member, All(member)), "miembro");
        Equal("Cambiar por 'Nombre'", All(member).Single().Fixes.Single().Title, "título");
        const string method = "&Texto = &Texto.Trimm()\n";
        Equal("&Texto = &Texto.Trim()\n", ApplyOnly(method, All(method)), "método");
        const string value = "&Texto = domEstado.ACTVO\n";
        Equal("&Texto = domEstado.ACTIVO\n", ApplyOnly(value, All(value)), "valor de dominio");
        Equal(0, All("&Texto = &Cupon.Zzzzzz\n").Single().Fixes.Count, "sin parecido no hay corrección");
    }

    static void FixUndefined()
    {
        Fix fix = All("&Nada = 1\n").Single(d => d.Code == TypeChecker.Undefined).Fixes.Single();
        Equal(FixKind.DeclareVariable, fix.Kind, "declarar");
        Equal("&Nada", fix.Expected, "la variable");
        Equal("Declarar &Nada", fix.Title, "título");
        var message = new RawMessage { Code = "spc0047", Text = "Variable Nada not defined; C(40.0) assumed.", Severity = Severity.Warning };
        const string text = "&Nada = 1\n&x = &Nada\n";
        var fromSpec = Analyzer.FromUndefinedVariable(message, new SourceText(text));
        Equal(2, fromSpec.Count, "en cada lugar");
        True(fromSpec.All(d => d.Fixes.Single().Kind == FixKind.DeclareVariable), "la de la especificación también");
        Fix second = fromSpec[1].Fixes.Single();
        True(second.TryLocate(text, fromSpec[1], Offset(text, 1, 5), out int offset, out _) && offset == Offset(text, 1, 5), "ubica la variable");
    }

    static void FixTextChecks()
    {
        const string dot = "&Texto = &Cupon.\n&Pos = 1\n";
        var dots = TextChecks.Run(new SourceText(dot), structure: true, blocks: true).Where(d => d.Code == TextChecks.StrayDot).ToList();
        Equal("&Texto = &Cupon\n&Pos = 1\n", ApplyOnly(dot, dots), "punto suelto");

        const string block = "For Each\n\t&Pos = 1\n&Id = 2\n";
        var blocks = TextChecks.Run(new SourceText(block), structure: true, blocks: true).Where(d => d.Code == BlockChecks.UnclosedBlock).ToList();
        Equal("For Each\n\t&Pos = 1\nEndFor\n&Id = 2\n", ApplyOnly(block, blocks), "EndFor que falta");
        const string nested = "Sub 'X'\n\tIf &Id = 1\n\t\t&Pos = 1\n\t&Id = 2\nEndSub\n";
        var inner = TextChecks.Run(new SourceText(nested), structure: true, blocks: true).Where(d => d.Code == BlockChecks.UnclosedBlock).ToList();
        Equal("Sub 'X'\n\tIf &Id = 1\n\t\t&Pos = 1\n\tEndIf\n\t&Id = 2\nEndSub\n", ApplyOnly(nested, inner), "con la sangría del bloque");
    }

    static void FixUnknownFunction()
    {
        const string text = "&Pos = Lenn(&Texto)\n";
        var message = new RawMessage { Code = "src0294", Text = "Function 'Lenn' not found", Severity = Severity.Error, Line = 1, Char = 8 };
        var found = Analyzer.FromParser(text, new[] { message }).Where(d => d.Code == "src0294").ToList();
        Equal("&Pos = Len(&Texto)\n", ApplyOnly(text, found), "función");
    }

    static void FixStandards()
    {
        const string commit = "Commit\n";
        Equal("prcCommit.Call()\n", ApplyOnly(commit, Standards(commit)), "commit");
        const string literal = "If &Estado = \"AC\"\nEndIf\n";
        Equal("If &Estado = domEstado.ACTIVO\nEndIf\n", ApplyOnly(literal, Standards(literal)), "literal del dominio");
        Equal(0, Standards("&Estado = \"XX\"\n").Single().Fixes.Count, "un valor que no es del dominio no se adivina");
        const string trim = "&Texto = &Estado.Trim()\n";
        Equal("&Texto = &Estado\n", ApplyOnly(trim, Standards(trim)), "sin el Trim");
        const string and = "For Each\n\tWhere CupId = &Id and CupNombre = &Texto\nEndFor\n";
        Equal("For Each\n\tWhere CupId = &Id\n\tWhere CupNombre = &Texto\nEndFor\n", ApplyOnly(and, Standards(and)), "otra cláusula Where");
        Equal(0, Standards("For Each\n\tWhere CupId = &Id and CupNombre = &Texto When &Pos = 1\nEndFor\n").Single().Fixes.Count, "con When no se separa");
        Equal(FixKind.CommitOnExitNo, TypeChecker.CommitOnExitWarning(new SourceText("&Id = 1\n")).Fixes.Single().Kind, "Commit on exit");
    }

    static void FixLocate()
    {
        const string original = "&Texto = &Cupon.Nombr\n";
        Diagnostic d = All(original).Single();
        Fix fix = d.Fixes.Single();
        // Una línea nueva arriba y texto antes en la misma línea: el subrayado se corrió y la corrección también
        string edited = "// nuevo\n&Pos = 1; &Texto = &Cupon.Nombr\n";
        int anchor = edited.IndexOf("Nombr");
        Equal("// nuevo\n&Pos = 1; &Texto = &Cupon.Nombre\n", Apply(edited, d, fix, anchor), "corrida");
        // Si ahí ya no está lo que se corrige, no se toca
        False(fix.TryLocate("&Texto = &Cupon.Nombre\n", d, original.IndexOf("Nombr"), out _, out _), "ya corregido a mano");

        // En otra línea (el End que falta): se comprueba que esa línea siga igual
        const string block = "For Each\n\t&Pos = 1\n&Id = 2\n";
        Diagnostic unclosed = TextChecks.Run(new SourceText(block), structure: true, blocks: true).Single(x => x.Code == BlockChecks.UnclosedBlock);
        Fix end = unclosed.Fixes.Single();
        False(end.TryLocate("For Each\n\t&Pos = 1\n&Id = 3\n", unclosed, 0, out _, out _), "la línea cambió");
        string crlf = "For Each\r\n\t&Pos = 1\r\n&Id = 2\r\n";
        Equal("For Each\r\n\t&Pos = 1\r\nEndFor\r\n&Id = 2\r\n", Apply(crlf, unclosed, end, 0), "con el salto de línea del documento");
    }
}
