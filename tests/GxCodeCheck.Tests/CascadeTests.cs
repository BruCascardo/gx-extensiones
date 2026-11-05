using System.Linq;
using GxCodeCheck;

static partial class AnalyzerTests
{
    static void RunCascadeTests()
    {
        Run("punto suelto al final de la línea: un solo error, lo demás es consecuencia", StrayDotAtEnd);
        Run("comando inesperado al principio: el problema está en la línea anterior", UnexpectedCommandAtStart);
        Run("puntos entre nombres y en números no son errores", ValidDots);
    }

    // Lo que informa GeneXus 18 U15 cuando la línea anterior al If termina en un '.' (un procedimiento real,
    // con los mensajes reducidos a este código): un error por cada comando hasta el final del procedimiento
    static readonly string CascadeText =
        "&Texto = &Otro{0}\n" +
        "If &Flag\n" +
        "\tFor Each Tabla\n" +
        "\t\tWhere TabId = &Id\n" +
        "\t\tDo Case\n" +
        "\t\t\tCase &Id = 1\n" +
        "\t\t\t\t&Texto = \"x\"\n" +
        "\t\t\tOtherwise\n" +
        "\t\t\t\t&Texto = \"y\"\n" +
        "\t\tEndCase\n" +
        "\tEndFor\n" +
        "EndIf\n";

    static readonly RawMessage[] CascadeMessages =
    {
        Raw("src0054", "'If' is an unexpected expression.", 2, 1),
        Raw("src0054", "'{0}' is an unexpected expression.", 2, 4),
        Raw("src0054", "'Tabla' is an unexpected expression.", 3, 11),
        Raw("src0052", "Unexpected command 'Where'", 4, 3),
        Raw("src0054", "'{0}' is an unexpected expression.", 4, 9),
        Raw("src0052", "Unexpected command 'Case'", 6, 4),
        Raw("src0054", "'{0}' is an unexpected expression.", 7, 5),
        Raw("src0052", "Unexpected command 'Otherwise'", 8, 4),
        Raw("src0052", "Unexpected command 'EndCase'", 10, 3),
        Raw("src0052", "Unexpected command 'EndFor'", 11, 2),
        Raw("src0052", "Unexpected command 'EndIf'", 12, 1),
    };

    static void StrayDotAtEnd()
    {
        var result = Analyzer.FromParser(string.Format(CascadeText, "."), CascadeMessages);
        var errors = result.Where(d => d.Severity == Severity.Error).ToList();
        Equal(1, errors.Count, "un solo error: " + string.Join(" | ", errors));
        Diagnostic dot = errors[0];
        Equal(TextChecks.StrayDot, dot.Code, "código");
        Equal(0, dot.Line, "línea");
        Equal(14, dot.Column, "el punto");
        True(dot.Message.Contains("línea 2 ('If')"), dot.Message);
        True(result.Where(d => d != dot).All(d => d.Severity == Severity.Info && d.Note.Contains("línea 1")), "lo demás, consecuencia de la línea 1");
    }

    static void UnexpectedCommandAtStart()
    {
        // Lo mismo con algo al final de la línea que no se detecta por sí solo (una coma)
        var result = Analyzer.FromParser(string.Format(CascadeText, ","), CascadeMessages);
        Diagnostic first = result.First(d => d.Severity == Severity.Error);
        Equal(1, first.Line, "el If");
        True(first.Message.Contains("Revisá el final de la línea 1"), first.Message);
        True(result.Where(d => d.Original.Any(o => o.Contains("Unexpected command"))).All(d => d.Severity == Severity.Info),
            "los comandos de bloque que GeneXus no esperaba, consecuencia");
    }

    static void ValidDots()
    {
        string text = "&Texto = &Sdt.Campo.Trim()\n&Texto = \"x\".Trim()\n&Numero = 1.5\n&Texto = Modulo.Objeto(&Lista.Item(1).Nombre)\n&Numero = 5 x\n";
        var result = Analyzer.FromParser(text, new[] { Raw("src0265", "Invalid attribute 'x'", 5, 13) });
        Equal(0, result.Count(d => d.Code == TextChecks.StrayDot), "sin puntos sueltos: " + string.Join(" | ", result));
    }
}
