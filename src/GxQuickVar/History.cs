using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GxQuickVar
{
    /// <summary>
    /// Tipos usados recientemente y el último tipo elegido para cada nombre de variable
    /// (vale entre objetos y KBs: si siempre declarás &amp;Mensaje como VarChar(200), lo sugiere).
    /// Se guarda por usuario en %APPDATA%\GxQuickVar\history.txt.
    /// </summary>
    static class History
    {
        const int MaxRecent = 12;
        const int MaxNames = 2000;

        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GxQuickVar", "history.txt");

        static List<string> s_Recent;
        // Orden de inserción = antigüedad, para recortar los más viejos
        static Dictionary<string, string> s_ByName;
        static List<string> s_NameOrder;

        public static IReadOnlyList<string> Recent
        {
            get { Load(); return s_Recent; }
        }

        public static string TypeFor(string variableName)
        {
            Load();
            return s_ByName.TryGetValue(variableName.ToLowerInvariant(), out string type) ? type : null;
        }

        public static void Record(string variableName, string typeText)
        {
            Load();
            s_Recent.RemoveAll(t => string.Equals(t, typeText, StringComparison.OrdinalIgnoreCase));
            s_Recent.Insert(0, typeText);
            if (s_Recent.Count > MaxRecent)
                s_Recent.RemoveRange(MaxRecent, s_Recent.Count - MaxRecent);

            string key = variableName.ToLowerInvariant();
            s_NameOrder.Remove(key);
            s_NameOrder.Add(key);
            s_ByName[key] = typeText;
            while (s_NameOrder.Count > MaxNames)
            {
                s_ByName.Remove(s_NameOrder[0]);
                s_NameOrder.RemoveAt(0);
            }
            Save();
        }

        public static void Clear()
        {
            Load();
            s_Recent.Clear();
            s_ByName.Clear();
            s_NameOrder.Clear();
            Save();
        }

        static void Load()
        {
            if (s_Recent != null)
                return;
            s_Recent = new List<string>();
            s_ByName = new Dictionary<string, string>();
            s_NameOrder = new List<string>();
            try
            {
                if (!File.Exists(FilePath))
                    return;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length == 2 && parts[0] == "R")
                    {
                        s_Recent.Add(parts[1]);
                    }
                    else if (parts.Length == 3 && parts[0] == "N")
                    {
                        if (!s_ByName.ContainsKey(parts[1]))
                            s_NameOrder.Add(parts[1]);
                        s_ByName[parts[1]] = parts[2];
                    }
                }
            }
            catch (Exception)
            {
                // Historial ilegible: se arranca vacío
            }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var lines = s_Recent.Select(t => "R\t" + t)
                    .Concat(s_NameOrder.Select(n => "N\t" + n + "\t" + s_ByName[n]));
                File.WriteAllLines(FilePath, lines, Encoding.UTF8);
            }
            catch (Exception)
            {
                // No poder guardar el historial no debe molestar al declarar
            }
        }
    }
}
