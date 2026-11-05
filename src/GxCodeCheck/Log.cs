using System;
using System.IO;
using System.Text;

namespace GxCodeCheck
{
    /// <summary>
    /// Registro de diagnóstico en %APPDATA%\GxCodeCheck\log.txt (carga del package y errores). Se apaga en Tools &gt; Options &gt; GxCodeCheck; pasado 1 MB empieza de nuevo.
    /// </summary>
    static class Log
    {
        const long MaxSize = 1024 * 1024;

        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GxCodeCheck", "log.txt");

        public static void Write(string message)
        {
            if (!Settings.LogEnabled)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxSize)
                    file.Delete();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception)
            {
                // Sin log no se interrumpe nada
            }
        }
    }
}
