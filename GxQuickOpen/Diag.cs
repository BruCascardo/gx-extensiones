using System;
using System.IO;
using System.Text;

namespace GxQuickOpen
{
    /// <summary>
    /// Registro de diagnóstico en %APPDATA%\GxQuickOpen\debug.log: qué pasó con el atajo y con la
    /// ventana (solo eventos del atajo, no cada tecla). Se recorta solo para no crecer sin límite.
    /// </summary>
    static class Diag
    {
        const long MaxBytes = 256 * 1024;

        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GxQuickOpen", "debug.log");

        static readonly object s_Lock = new object();

        public static void Log(string message)
        {
            try
            {
                lock (s_Lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    var file = new FileInfo(FilePath);
                    if (file.Exists && file.Length > MaxBytes)
                        file.Delete();
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // El diagnóstico nunca debe molestar
            }
        }
    }
}
