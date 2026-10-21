using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GxQuickOpen
{
    /// <summary>
    /// Objetos abiertos últimamente, por KB (de cualquier forma: Ctrl+O, KB Explorer, F12...).
    /// Aparecen al abrir la búsqueda sin texto y suben en los resultados.
    /// Se guardan por usuario en %APPDATA%\GxQuickOpen\recent.txt.
    /// </summary>
    static class Recents
    {
        const int MaxPerKb = 40;

        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GxQuickOpen", "recent.txt");

        static readonly object s_Lock = new object();
        // KB (ruta en minúsculas) → Guids, el más reciente primero
        static Dictionary<string, List<Guid>> s_ByKb;

        public static List<Guid> For(string kb)
        {
            lock (s_Lock)
            {
                Load();
                return s_ByKb.TryGetValue(kb, out List<Guid> list) ? new List<Guid>(list) : new List<Guid>();
            }
        }

        public static void Record(string kb, Guid guid)
        {
            if (string.IsNullOrEmpty(kb) || guid == Guid.Empty)
                return;
            lock (s_Lock)
            {
                Load();
                if (!s_ByKb.TryGetValue(kb, out List<Guid> list))
                    s_ByKb[kb] = list = new List<Guid>();
                if (list.Count > 0 && list[0] == guid)
                    return;
                list.Remove(guid);
                list.Insert(0, guid);
                if (list.Count > MaxPerKb)
                    list.RemoveRange(MaxPerKb, list.Count - MaxPerKb);
                Save();
            }
        }

        public static void Clear()
        {
            lock (s_Lock)
            {
                Load();
                s_ByKb.Clear();
                Save();
            }
        }

        static void Load()
        {
            if (s_ByKb != null)
                return;
            s_ByKb = new Dictionary<string, List<Guid>>();
            try
            {
                if (!File.Exists(FilePath))
                    return;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length != 2 || !Guid.TryParse(parts[1], out Guid guid))
                        continue;
                    if (!s_ByKb.TryGetValue(parts[0], out List<Guid> list))
                        s_ByKb[parts[0]] = list = new List<Guid>();
                    if (!list.Contains(guid))
                        list.Add(guid);
                }
            }
            catch (Exception)
            {
                // Archivo ilegible: se arranca vacío
            }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath,
                    s_ByKb.SelectMany(kv => kv.Value.Select(guid => kv.Key + "\t" + guid.ToString("D"))),
                    Encoding.UTF8);
            }
            catch (Exception)
            {
                // No poder guardar los recientes no debe molestar al abrir
            }
        }
    }
}
