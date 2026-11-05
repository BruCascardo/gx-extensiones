using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GxModuleCache
{
    /// <summary>
    /// Ejecuta <c>nuget locals all -clear</c> con el nuget.exe que usa el Module Manager de GeneXus
    /// (%USERPROFILE%\.gxmodules\.tools, lo descarga GeneXus la primera vez) o, si no está, el que trae la
    /// instalación de GeneXus.
    /// </summary>
    static class NuGetCache
    {
        const int TimeoutMs = 120000;

        public struct Result
        {
            public bool Success;
            public string Output;
        }

        public static Result Clear()
        {
            string nuget = FindNuGet();
            if (nuget == null)
                return new Result { Output = "No encontré nuget.exe en " + ModulesToolsPath + " ni en la carpeta de GeneXus." };

            var info = new ProcessStartInfo(nuget, "locals all -clear")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(nuget),
            };
            using (Process process = Process.Start(info))
            {
                // Las dos salidas a la vez: si se llena una mientras se espera la otra, nuget se traba
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(TimeoutMs))
                {
                    try { process.Kill(); } catch (Exception) { }
                    return new Result { Output = nuget + " no terminó en " + TimeoutMs / 1000 + " segundos." };
                }
                string text = string.Join(" ", new[] { output.Result, error.Result }
                    .Select(t => t.Trim()).Where(t => t.Length > 0)).Replace(Environment.NewLine, " ");
                return new Result { Success = process.ExitCode == 0, Output = text };
            }
        }

        static string ModulesToolsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gxmodules", ".tools");

        static string FindNuGet() =>
            new[] { ModulesToolsPath, AppDomain.CurrentDomain.BaseDirectory }
                .Select(dir => Path.Combine(dir, "nuget.exe"))
                .FirstOrDefault(File.Exists);
    }
}
