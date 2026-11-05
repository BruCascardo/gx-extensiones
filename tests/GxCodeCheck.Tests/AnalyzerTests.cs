using System;
using System.Collections.Generic;
using System.Linq;
using GxCodeCheck;

static partial class AnalyzerTests
{
    static int s_Failed;

    static int Main()
    {
        Run("una letra de más al final de la instrucción", ExtraWord);
        Run("comentario sin //: un solo error para la línea", ProseLine);
        Run("paréntesis de más: el error va donde está y lo demás es consecuencia", ExtraParenthesis);
        Run("paréntesis sin cerrar", UnclosedParenthesis);
        Run("comillas sin cerrar", UnclosedString);
        Run("línea que termina en operador", TrailingOperator);
        Run("If sin EndIf", MissingEndIf);
        Run("función mal escrita sugiere la correcta", Misspelled);
        Run("strings y comentarios no cuentan", IgnoresStringsAndComments);
        Run("mensajes repetidos quedan una vez", Duplicates);
        Run("especificación: tipos en la asignación", SpecTypeMismatch);
        Run("especificación: parámetro de tipo incorrecto marca la llamada", SpecWrongParameter);
        Run("especificación: variable no definida marca sus usos", SpecUndefinedVariable);
        Run("texto para copiar", CopyText);
        Run("línea que no corresponde: va donde está el nombre", Relocate);
        Run("nombres entre comillas duplicadas", DoubledQuotes);
        Run("For sin EndFor: marca el For, y lo demás de GeneXus es consecuencia", MissingEndFor);
        Run("bloques anidados: la sangría dice a cuál le falta el End", NestedMissingEnd);
        Run("End de más", ExtraEnd);
        Run("si GeneXus guardó, no se inventan errores de estructura", NoStructureWhenSaved);
        RunCascadeTests();
        RunTypeCheckerTests();
        RunStandardTests();
        RunFixTests();
        Console.WriteLine(s_Failed == 0 ? "OK: todas las pruebas pasaron" : $"FALLARON {s_Failed}");
        return s_Failed == 0 ? 0 : 1;
    }

    // ---- Casos con los mensajes que devuelve el parser de GeneXus 18 U15 (IParserEngine.Validate)

    static void ExtraWord()
    {
        string text = "If &Flag\n\t\t&Numero = 5 x\nEndIf\n";
        var result = Analyzer.FromParser(text, new[]
        {
            Raw("src0256", "Expecting assignment expression, e.g. 'x = <value>'", 2, 15),
            Raw("src0265", "Invalid attribute 'x'", 2, 15),
            Raw("src0256", "Expecting assignment expression, e.g. 'x = <value>'", 2, 15),
            Raw("src0265", "Invalid attribute 'x'", 2, 15),
        });
        Equal(1, result.Count, "un solo error");
        var d = result[0];
        Equal(Severity.Error, d.Severity, "severidad");
        Equal(1, d.Line, "línea");
        Equal(14, d.Column, "columna (el tab cuenta como un carácter)");
        Equal(1, d.Length, "largo");
        True(d.Message.StartsWith("Sobra 'x'"), d.Message);
        Equal(2, d.Original.Count, "conserva los dos mensajes originales sin repetir");
    }

    static void ProseLine()
    {
        string text = "&Texto = \"a\"\nCreo el lote con 2 detalles\n&Numero = 1\n";
        var result = Analyzer.FromParser(text, new[]
        {
            Raw("src0054", "'2' is an unexpected expression.", 2, 18),
            Raw("src0054", "'detalles' is an unexpected expression.", 2, 20),
            Raw("src0054", "'{0}' is an unexpected expression.", 3, 1),
        });
        Equal(2, result.Count, "la línea y su consecuencia");
        True(result[0].Message.Contains("//"), result[0].Message);
        Equal(1, result[0].Line, "línea del comentario");
        Equal(Severity.Error, result[0].Severity, "la línea es el error");
        Equal(Severity.Info, result[1].Severity, "lo de la línea siguiente es consecuencia");
        True(result[1].Note.Contains("línea 2"), result[1].Note);
    }

    static void ExtraParenthesis()
    {
        string text = "&Texto = Trim(&Texto))\n&Numero = Len(&Texto)\n&Flag = True\n";
        var result = Analyzer.FromParser(text, new[]
        {
            Raw("src0102", "Parameter separator (',') was expected.", 2, 1),
            Raw("src0051", "Unexpected expression", 3, 1),
        });
        var main = result.Single(d => d.Severity == Severity.Error);
        Equal(TextChecks.ExtraClose, main.Code, "código");
        Equal(0, main.Line, "en la línea del paréntesis");
        Equal(21, main.Column, "en el paréntesis");
        True(result.Where(d => d != main).All(d => d.Severity == Severity.Info && d.Note.Contains("línea 1")), "los demás son consecuencia");

        // Un error más abajo no tiene que ver con el paréntesis
        text = "&Texto = Trim(&Texto))\n&Numero = 1\n&Flag = True\n&Numero = 5 x\n";
        result = Analyzer.FromParser(text, new[]
        {
            Raw("src0257", "Expecting assignment operator, e.g. 'x = <value>'.", 4, 13),
            Raw("src0265", "Invalid attribute 'x'", 4, 13),
        });
        Equal(Severity.Error, result.Single(d => d.Line == 3).Severity, "el error de la línea 4 es propio");
    }

    static void UnclosedParenthesis()
    {
        string text = "&Numero = Len(&Texto\n&Flag = True\n";
        var result = Analyzer.FromParser(text, new[] { Raw("src0102", "Parameter separator (',') was expected.", 2, 1) });
        var main = result.Single(d => d.Severity == Severity.Error);
        Equal(TextChecks.Unclosed, main.Code, "código");
        Equal(0, main.Line, "línea");
        Equal(13, main.Column, "columna del '('");
    }

    static void UnclosedString()
    {
        string text = "&Texto = \"a\n&Numero = 1\n";
        var result = Analyzer.FromParser(text, new[] { Raw("src0053", "'\"' is an invalid character.", 1, 10) });
        Equal(1, result.Count, "el parser y el chequeo propio marcan lo mismo");
        Equal(TextChecks.UnclosedString, result[0].Code, "código");
        Equal(9, result[0].Column, "en la comilla");
        Equal(1, result[0].Original.Count, "suma el mensaje de GeneXus");
    }

    static void TrailingOperator()
    {
        // El parser lo acepta: une las dos líneas en "&Numero = 5 + &Fecha = Today()"
        string text = "&Numero = 5 +\n&Fecha = Today()\n&Texto = \"a\" +\n\t\"b\"\n";
        var result = Analyzer.FromParser(text, new RawMessage[0]);
        Equal(1, result.Count, "solo cuando la línea siguiente es una instrucción nueva");
        Equal(Severity.Warning, result[0].Severity, "advertencia");
        Equal(0, result[0].Line, "línea");
        Equal(12, result[0].Column, "en el '+'");
    }

    static void MissingEndIf()
    {
        string text = "For &Numero = 1 to 10\n\tIf &Flag\n\t\t&Numero = 2\n\tEndFor\nEndIf\n";
        var result = Analyzer.FromParser(text, new[]
        {
            Raw("src0057", "Expecting 'EndIf' command to close the 'If' block", 2, 2),
            Raw("src0052", "Unexpected command 'EndIf'", 5, 1),
        });
        Equal(2, result.Count, "dos errores");
        Equal(2, result[0].Length, "marca el If");
        True(result[0].Message.Contains("EndIf"), result[0].Message);
        True(result[1].Message.StartsWith("Sobra este 'EndIf'"), result[1].Message);
    }

    static void Misspelled()
    {
        var result = Analyzer.FromParser("&Texto = &Texto.Trimm()\n", new[] { Raw("src0294", "Unknown function 'Trimm'", 1, 17) });
        True(result[0].Message.Contains("¿Quisiste decir 'Trim'?"), result[0].Message);
        Equal(5, result[0].Length, "marca el nombre");
        Equal(null, Explainer.Suggest("Xyzzy"), "sin sugerencia si no se parece a nada");
    }

    static void IgnoresStringsAndComments()
    {
        string text = "&Texto = \"(\" // )\n/* ( */\n&Texto = ')' + \"[\"\n";
        Equal(0, Analyzer.FromParser(text, new RawMessage[0]).Count, "sin errores");
        var unclosed = Analyzer.FromParser("&a = 1\n/* sin cerrar\n&b = 2\n", new[] { Raw("src0108", "Missing comment block end '*/'.", 3, 7) });
        Equal(TextChecks.UnclosedComment, unclosed.First(d => d.Severity == Severity.Error).Code, "comentario sin cerrar");
    }

    static void Duplicates()
    {
        var raw = Raw("src0287", "Program 'PrbNoexiste' does not exist or is inaccessible.", 1, 1);
        var result = Analyzer.FromParser("PrbNoexiste()\n", new[] { raw, raw });
        Equal(1, result.Count, "uno solo");
        Equal(1, result[0].Original.Count, "un mensaje original");
    }

    // ---- Especificación: el formato que trae el Output (Background Build), solo con la línea

    static void SpecTypeMismatch()
    {
        var source = new SourceText("\n\t&inWebSerLog_Response.Estado = \"OK\" // listo\n");
        var d = Analyzer.FromSpecifier(Raw("spc0010",
            "Type mismatch in assignment: &inWebSerLog_Response.Estado = \"OK\" (ServicioLogEstado=Character).", 2, 0), source);
        Equal(1, d.Line, "línea");
        Equal(1, d.Column, "desde el código, sin la sangría");
        Equal("&inWebSerLog_Response.Estado = \"OK\"".Length, d.Length, "hasta el final del código, sin el comentario");
        True(d.Message.Contains("ServicioLogEstado") && d.Message.Contains("Character"), d.Message);
    }

    static void SpecWrongParameter()
    {
        var source = new SourceText("Sistema.Global.prcLog.Call(\"Error\", &Mensaje)\n");
        var d = Analyzer.FromSpecifier(Raw("spc0023",
            "Parameter \"Error\" (Character) calling Sistema.Global.prcLog has wrong type (expecting LogTipo).", 1, 0), source);
        Equal(15, d.Column, "en el nombre del objeto llamado");
        True(d.Message.Contains("LogTipo"), d.Message);
    }

    static void SpecUndefinedVariable()
    {
        var source = new SourceText("&CuponTexto = \"a\"\n&Otra = &cupontexto + \"&CuponTexto\"\n&CuponTextoLargo = 1\n");
        var result = Analyzer.FromUndefinedVariable(Raw("spc0047", "Variable Cupontexto not defined; C(40.0) assumed.", 0, 0), source);
        Equal(2, result.Count, "dos usos (no el del string ni la otra variable)");
        Equal(1, result[1].Line, "segundo uso");
        Equal(8, result[1].Column, "columna del segundo uso");
        True(result[0].Message.Contains("&Cupontexto"), result[0].Message);
    }

    static void CopyText()
    {
        var result = Analyzer.FromParser("&Numero = 5 x\n", new[]
        {
            Raw("src0257", "Expecting assignment operator, e.g. 'x = &Fecha = Today()'.", 1, 13),
            Raw("src0265", "Invalid attribute 'x'", 1, 13),
        });
        string copy = result[0].CopyText("Procedure 'PrbProc' Source");
        True(copy.StartsWith("Procedure 'PrbProc' Source, línea 1, columna 13"), copy);
        True(copy.Contains("GeneXus: src0265: Invalid attribute 'x'"), copy);
    }

    static void Relocate()
    {
        // GeneXus informa la línea 2, pero 'wdqd' está solo en la 3
        string text = "&a = 1\n&b = 2\nwdqd\n&c = 3\n";
        var result = Analyzer.FromParser(text, new[] { Raw("src0265", "Invalid attribute 'wdqd'", 2, 1) });
        Equal(2, result.Single().Line, "línea de 'wdqd'");
        Equal(4, result.Single().Length, "marca el nombre");

        // Un If sin EndIf cita 'EndIf', que justamente no está: no se mueve
        result = Analyzer.FromParser("If &a\n&b = 1\nEndIf\nIf &c\n", new[] { Raw("src0057", "Expecting 'EndIf' command to close the 'If' block", 4, 1) });
        Equal(3, result.Single().Line, "queda en el If");
    }

    static void DoubledQuotes()
    {
        Equal("LeerCupon", Explainer.Arg("''LeerCupon'' is an undefined subroutine.", 0), "sin las comillas de más");
        Equal("x", Explainer.Arg("Invalid attribute 'x'", 0), "comillas simples");
        True(Explainer.Explain("src0304", "''LeerCupon'' is an undefined subroutine.").Contains("LeerCupon"), "explicación con el nombre");
    }

    static void MissingEndFor()
    {
        // Lo que informó GeneXus 18 U15 con un EndFor borrado: el End en cualquier lado y subrutinas "no definidas"
        string text = string.Join("\n", new[]
        {
            "Do 'ArmarRequest'",                 // 1
            "Do 'Consumir'",                     // 2
            "Sub 'ArmarRequest'",                // 3
            "\tFor &Item in &Lista",             // 4
            "\t\t&Texto += &Item.Nombre",        // 5
            "\t&Numero = 1",                     // 6
            "EndSub",                            // 7
            "Sub 'Consumir'",                    // 8
            "\t&Numero = 2",                     // 9
            "EndSub",                            // 10
        });
        var result = Analyzer.FromParser(text, new[]
        {
            Raw("src0059", "Expecting 'EndFor' command to close the 'For Each' block", 6, 13),
            Raw("src0062", "Expecting 'EndSub' command to close subroutine definition.", 8, 1),
            Raw("src0304", "''ArmarRequest'' is an undefined subroutine.", 1, 4),
            Raw("src0304", "''Consumir'' is an undefined subroutine.", 2, 4),
        });
        var errors = result.Where(d => d.Severity == Severity.Error).ToList();
        Equal(1, errors.Count, "un solo error: el For");
        Equal(BlockChecks.UnclosedBlock, errors[0].Code, "código");
        Equal(3, errors[0].Line, "línea del For");
        Equal(1, errors[0].Column, "en el For");
        Equal(3, errors[0].Length, "marca 'For'");
        True(errors[0].Message.Contains("EndFor") && errors[0].Message.Contains("línea 6"), errors[0].Message);
        True(result.Where(d => d != errors[0]).All(d => d.Severity == Severity.Info), "lo de GeneXus es consecuencia");
    }

    static void NestedMissingEnd()
    {
        string text = string.Join("\n", new[]
        {
            "For each Cliente",                  // 1
            "\tIf &Flag",                        // 2
            "\t\t&Numero = 1",                   // 3
            "\tEndIf",                           // 4
            "\tFor &i = 1 to 10",                // 5  <- le falta el EndFor
            "\t\t&Numero += 1",                  // 6
            "EndFor",                            // 7  cierra el For each por la sangría
        });
        var result = Analyzer.FromParser(text, new[] { Raw("src0059", "Expecting 'EndFor' command to close the 'For Each' block", 7, 7) });
        var error = result.Single(d => d.Severity == Severity.Error);
        Equal(4, error.Line, "el For de adentro");
        True(error.Message.Contains("línea 7"), error.Message);
    }

    static void ExtraEnd()
    {
        string text = "If &Flag\n\t&Numero = 1\nEndIf\nEndIf\n";
        var result = Analyzer.FromParser(text, new[] { Raw("src0052", "Unexpected command 'EndIf'", 4, 1) });
        var error = result.Single(d => d.Severity == Severity.Error);
        Equal(BlockChecks.ExtraEnd, error.Code, "código");
        Equal(3, error.Line, "el segundo EndIf");
    }

    static void NoStructureWhenSaved()
    {
        // Código que GeneXus aceptó: aunque los chequeos propios se confundan, no marcan nada
        string text = "For &i = 1 to 10\n\tDo 'Algo'\n&Texto = \"(\"\n";
        Equal(0, Analyzer.FromParser(text, new RawMessage[0]).Count, "sin errores");
    }

    // ---- Utilidades

    static RawMessage Raw(string code, string text, int line, int ch) =>
        new RawMessage { Code = code, Text = text, Line = line, Char = ch, Severity = Severity.Error };

    static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("  ok   " + name);
        }
        catch (Exception ex)
        {
            s_Failed++;
            Console.WriteLine("  FALLA " + name + ": " + ex.Message);
        }
    }

    static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what}: se esperaba {expected}, salió {actual}");
    }

    static void True(bool condition, string what)
    {
        if (!condition)
            throw new Exception(what);
    }
}
