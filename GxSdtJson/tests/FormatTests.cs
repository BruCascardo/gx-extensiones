using System;
using System.Collections.Generic;
using System.Linq;
using GxSdtJson;

static class FormatTests
{
    static int s_Failed;

    static int Main()
    {
        Run("ida y vuelta conserva todo", RoundTrip);
        Run("acepta el arreglo de ítems solo", BareArray);
        Run("copiar una parte (Ctrl+C) y volver a leerla", PartialRoundTrip);
        Run("sufijo collection y []", CollectionSuffix);
        Run("nivel vacío sigue siendo nivel", EmptyLevel);
        Run("errores con mensaje útil", Errors);
        Run("cuenta miembros", Count);
        Console.WriteLine(s_Failed == 0 ? "OK: todas las pruebas pasaron" : $"FALLARON {s_Failed}");
        return s_Failed == 0 ? 0 : 1;
    }

    static SdtNode Sample() => new SdtNode
    {
        Name = "SDTCliente",
        Description = "Cliente del sistema",
        Items = new List<SdtNode>
        {
            new SdtNode { Name = "Id", Type = "Numeric(8.0)" },
            new SdtNode { Name = "Nombre", Type = "VarChar(40)", Description = "Nombre completo", JsonName = "nombre" },
            new SdtNode { Name = "Saldo", Type = "Domain:Importe", BaseType = "Numeric(12.2-)" },
            new SdtNode { Name = "Tags", Type = "VarChar(20)", Collection = true, CollectionItemName = "Tag" },
            new SdtNode { Name = "Contacto", Type = "SDTContacto, Sistema.Global" },
            new SdtNode
            {
                Name = "Telefonos", Collection = true, CollectionItemName = "Telefono",
                Items = new List<SdtNode>
                {
                    new SdtNode { Name = "Numero", Type = "Character(20)" },
                    new SdtNode { Name = "Extra", Items = new List<SdtNode> { new SdtNode { Name = "Nota", Type = "LongVarChar(2M)" } } },
                },
            },
        },
    };

    static void RoundTrip()
    {
        SdtNode original = Sample();
        string json = SdtJsonFormat.Write(original);
        SdtNode back = SdtJsonFormat.Read(json);
        Equal(Dump(original), Dump(back), "estructura");
        Equal(json, SdtJsonFormat.Write(back), "JSON idéntico al reescribir");
        True(!json.Contains("baseType\": null") && !json.Contains("\"type\": null"), "sin nulls");
        Console.WriteLine(json);
    }

    static void BareArray()
    {
        SdtNode node = SdtJsonFormat.Read("[{\"name\":\"A\",\"type\":\"Boolean\"},{\"name\":\"L\",\"items\":[{\"name\":\"B\",\"type\":\"Date\"}]}]");
        Equal(null, node.Name, "sin nombre");
        Equal(2, node.Items.Count, "ítems");
        True(node.Items[1].IsLevel, "L es nivel");
    }

    static void PartialRoundTrip()
    {
        // Lo que copia Ctrl+C con un miembro colección y una subestructura colección seleccionados
        List<SdtNode> selected = Sample().Items.Where(i => i.Name == "Tags" || i.Name == "Telefonos").ToList();
        string json = SdtJsonFormat.WriteItems(selected);
        True(json.TrimStart().StartsWith("["), "es un arreglo");
        SdtNode back = SdtJsonFormat.Read(json);
        Equal(string.Concat(selected.Select(i => Dump(i))), string.Concat(back.Items.Select(i => Dump(i))), "ítems");
    }

    static void CollectionSuffix()
    {
        SdtNode node = SdtJsonFormat.Read("{\"items\":[{\"name\":\"A\",\"type\":\"SDTX collection\"},{\"name\":\"B\",\"type\":\"VarChar(10)[]\"},{\"name\":\"C\",\"type\":\"Date\"}]}");
        True(node.Items[0].Collection && node.Items[0].Type == "SDTX", "collection");
        True(node.Items[1].Collection && node.Items[1].Type == "VarChar(10)", "[]");
        True(!node.Items[2].Collection, "sin sufijo");
    }

    static void EmptyLevel()
    {
        SdtNode node = SdtJsonFormat.Read("{\"items\":[{\"name\":\"L\",\"items\":[]}]}");
        True(node.Items[0].IsLevel && node.Items[0].Items.Count == 0, "nivel vacío");
        True(SdtJsonFormat.Write(node).Contains("\"items\": []"), "se escribe como nivel");
    }

    static void Errors()
    {
        Throws("", "Pegá");
        Throws("{nope", "JSON inválido");
        Throws("{\"name\":\"X\"}", "Falta \"items\"");
        Throws("{\"items\":[]}", "no tiene ítems");
        Throws("{\"items\":[{\"type\":\"Date\"}]}", "sin \"name\"");
        Throws("{\"items\":[{\"name\":\"A\"}]}", "Falta \"type\" en A");
        Throws("{\"items\":[{\"name\":\"L\",\"items\":[{\"name\":\"B\"}]}]}", "Falta \"type\" en L.B");
        Throws("{\"items\":[{\"name\":\"A\",\"type\":\"Date\"},{\"name\":\"a\",\"type\":\"Date\"}]}", "repetido");
        Throws("{\"items\":[{\"name\":\"A\",\"type\":\"Date\",\"collection\":\"si\"}]}", "true o false en A");
        Throws("{\"items\":[1]}", "tienen que ser objetos");
        Throws("42", "tiene que ser un objeto");
    }

    static void Count() => Equal(7, Sample().CountMembers(), "miembros (hojas)");

    static string Dump(SdtNode n, string indent = "") =>
        $"{indent}{n.Name}|{n.Type}|{n.BaseType}|{n.Description}|{n.JsonName}|{n.Collection}|{n.CollectionItemName}|{n.IsLevel}\n"
        + (n.Items == null ? "" : string.Concat(n.Items.Select(i => Dump(i, indent + "  "))));

    static void Throws(string json, string expected)
    {
        try
        {
            SdtJsonFormat.Read(json);
            Fail($"no tiró error para {json}");
        }
        catch (FormatException ex)
        {
            True(ex.Message.Contains(expected), $"mensaje \"{ex.Message}\" contiene \"{expected}\"");
        }
    }

    static void Run(string name, Action test)
    {
        int before = s_Failed;
        try { test(); }
        catch (Exception ex) { Fail("excepción: " + ex); }
        Console.WriteLine((s_Failed == before ? "ok    " : "FALLA ") + name);
    }

    static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            Fail($"{what}: esperaba\n{expected}\npero fue\n{actual}");
    }

    static void True(bool condition, string what)
    {
        if (!condition) Fail(what);
    }

    static void Fail(string message)
    {
        s_Failed++;
        Console.WriteLine("  ✗ " + message);
    }
}
