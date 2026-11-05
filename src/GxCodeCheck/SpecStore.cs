using System;
using System.Collections.Generic;
using System.Linq;

namespace GxCodeCheck
{
    /// <summary>
    /// Los errores que GeneXus informó en el Output para cada objeto (por nombre calificado), por parte: los
    /// del parser al guardar (src) y los de la última especificación (spc). Se usa solo desde el hilo de la
    /// interfaz.
    /// </summary>
    /// <remarks>
    /// Al guardar se borra todo lo del objeto. La especificación en segundo plano termina varios segundos
    /// después: si mientras tanto se volvió a guardar, lo que informa es del código anterior y se descarta.
    /// </remarks>
    static class SpecStore
    {
        public sealed class Entry
        {
            /// <summary>Tipo de la parte (Source, Events, Rules...), o Guid.Empty si el mensaje es del objeto entero.</summary>
            public Guid Part;
            /// <summary>
            /// Nombre de la parte según el mensaje ("Source", "Events"...), o null. Al guardar un objeto que
            /// modifica WorkWithPlus GeneXus informa "Source (by WorkWithPlus)", con otro tipo de parte.
            /// </summary>
            public string PartName;
            public RawMessage Message;
        }

        static readonly Dictionary<string, List<Entry>> s_ByObject = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
        // Cuántas veces se guardó cada objeto, y en qué guardado empezó su última especificación
        static readonly Dictionary<string, int> s_Saves = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> s_SpecifiedAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> s_FinishedAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, DateTime> s_SavedAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Cambiaron los errores del objeto (nombre calificado).</summary>
        public static event Action<string> Changed;

        /// <summary>Se guarda el objeto: lo anterior ya no vale (GeneXus valida y escribe los errores nuevos enseguida).</summary>
        public static void SaveStarted(string objectName)
        {
            s_Saves[objectName] = Saves(objectName) + 1;
            s_SavedAt[objectName] = DateTime.Now;
            s_ByObject.Remove(objectName);
            Changed?.Invoke(objectName);
        }

        /// <summary>Empieza la especificación del objeto: reemplaza los errores de la anterior.</summary>
        public static void SpecificationStarted(string objectName)
        {
            s_SpecifiedAt[objectName] = Saves(objectName);
            if (s_ByObject.TryGetValue(objectName, out List<Entry> entries) && entries.RemoveAll(e => IsSpecifier(e.Message)) > 0)
                Changed?.Invoke(objectName);
        }

        /// <summary>Terminó la especificación del objeto.</summary>
        public static void SpecificationFinished(string objectName)
        {
            if (!IsCurrentSpecification(objectName))
                return;
            s_FinishedAt[objectName] = Saves(objectName);
            Changed?.Invoke(objectName);
        }

        /// <summary>Si ya terminó la especificación de lo guardado por última vez: lo que informó es lo que vale.</summary>
        public static bool IsSpecified(string objectName) =>
            s_FinishedAt.TryGetValue(objectName, out int at) && at == Saves(objectName);

        /// <summary>Si la especificación en curso del objeto es del código guardado por última vez.</summary>
        public static bool IsCurrentSpecification(string objectName) =>
            !s_SpecifiedAt.TryGetValue(objectName, out int at) || at == Saves(objectName);

        public static void Add(string objectName, Guid part, string partName, RawMessage message)
        {
            if (IsSpecifier(message) && !IsCurrentSpecification(objectName))
                return;
            if (!s_ByObject.TryGetValue(objectName, out List<Entry> entries))
                s_ByObject[objectName] = entries = new List<Entry>();
            // El mismo mensaje puede llegar por más de un Output (Build y Background Build)
            if (entries.Any(e => e.Part == part && e.Message.Line == message.Line && e.Message.Full == message.Full))
                return;
            entries.Add(new Entry { Part = part, PartName = partName, Message = message });
            Changed?.Invoke(objectName);
        }

        public static IReadOnlyList<Entry> For(string objectName) =>
            s_ByObject.TryGetValue(objectName, out List<Entry> entries) ? entries : (IReadOnlyList<Entry>)Array.Empty<Entry>();

        /// <summary>Cuánto pasó desde que se guardó el objeto, para el log ("" si no se guardó en esta sesión).</summary>
        public static string SinceSave(string objectName) =>
            s_SavedAt.TryGetValue(objectName, out DateTime at) ? $"{(DateTime.Now - at).TotalSeconds:0.0} s después de guardar" : "";

        public static int ErrorCount(string objectName) => For(objectName).Count(e => e.Message.Severity == Severity.Error);

        public static void Clear()
        {
            var names = s_ByObject.Keys.ToList();
            s_ByObject.Clear();
            s_Saves.Clear();
            s_SpecifiedAt.Clear();
            s_FinishedAt.Clear();
            s_SavedAt.Clear();
            foreach (string name in names)
                Changed?.Invoke(name);
        }

        static int Saves(string objectName) => s_Saves.TryGetValue(objectName, out int saves) ? saves : 0;

        static bool IsSpecifier(RawMessage message) => message.Code.StartsWith("spc");
    }
}
