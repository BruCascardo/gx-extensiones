// Instalador de packages de GeneXus: trae InstalarPackages.ps1 como recurso, lo escribe en %TEMP%
// y lo ejecuta con PowerShell tomando los zip de la carpeta donde está el exe.
// Se compila con Compilar.ps1 (usa el csc.exe de .NET Framework, no hace falta el SDK).
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

[assembly: AssemblyTitle("Instalador de packages de GeneXus")]
[assembly: AssemblyDescription("Instala los packages de GeneXus 18 U15 de esta carpeta, eligiendo cuáles")]
[assembly: AssemblyProduct("GxInstalador")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class Program
{
    const string ScriptResource = "InstalarPackages.ps1";
    const string OrigenParameter = "-Origen";

    static int Main(string[] args)
    {
        Console.Title = "Instalador de packages de GeneXus";
        int exitCode;
        try
        {
            exitCode = Run(args);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.Message);
            Console.ResetColor();
            exitCode = 1;
        }
        // Con doble clic la ventana se cerraría sin dejar leer el resultado
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.Write("Presioná una tecla para salir...");
            Console.ReadKey(true);
        }
        return exitCode;
    }

    static int Run(string[] args)
    {
        string script = Path.Combine(Path.GetTempPath(), "GxInstalador-" + Guid.NewGuid() + ".ps1");
        using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ScriptResource))
        using (FileStream file = File.Create(script))
        {
            resource.CopyTo(file);
        }

        try
        {
            string arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script);
            // Los zip están al lado del exe, salvo que se indique otra carpeta
            if (!args.Any(a => a.Equals(OrigenParameter, StringComparison.OrdinalIgnoreCase)))
            {
                arguments += " " + OrigenParameter + " " + Quote(AppDomain.CurrentDomain.BaseDirectory);
            }
            arguments += string.Concat(args.Select(a => " " + Quote(a)));

            ProcessStartInfo startInfo = new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), arguments);
            startInfo.UseShellExecute = false;
            using (Process process = Process.Start(startInfo))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }
        finally
        {
            File.Delete(script);
        }
    }

    // Comillas para la línea de comandos; una \ final se duplica para que no escape la comilla
    static string Quote(string value)
    {
        string escaped = value.Replace("\"", "\\\"");
        if (escaped.EndsWith("\\")) escaped += "\\";
        return "\"" + escaped + "\"";
    }
}
