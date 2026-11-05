using System;
using System.Collections.Generic;
using System.Linq;

namespace GxCodeCheck
{
    enum Severity { Error, Warning, Info }

    /// <summary>De dónde sale un error: el parser de GeneXus (al guardar), la especificación (después de guardar o en un build), un chequeo propio del texto, el chequeo de tipos propio o el estándar del equipo.</summary>
    enum Origin { Parser, Specifier, Check, Types, Standard }

    /// <summary>Un mensaje tal como lo da GeneXus. Línea y columna empiezan en 1; columna 0 es sin columna (la especificación solo da la línea).</summary>
    sealed class RawMessage
    {
        public string Code = "";
        public string Text = "";
        public Severity Severity;
        public int Line;
        public int Char;

        public string Full => Code.Length == 0 ? Text : Code + ": " + Text;

        public override string ToString() => $"L{Line} C{Char} {Full}";
    }

    /// <summary>Un error ya ubicado en el texto (línea y columna desde 0) y explicado.</summary>
    sealed class Diagnostic
    {
        public Severity Severity;
        public Origin Origin;
        public string Code = "";
        /// <summary>La explicación que se muestra.</summary>
        public string Message = "";
        /// <summary>Los mensajes de GeneXus que lo originan, con su código.</summary>
        public readonly List<string> Original = new List<string>();
        /// <summary>Aclaración, por ejemplo que es consecuencia de otro error.</summary>
        public string Note;
        public int Line;
        public int Column;
        public int Length = 1;
        /// <summary>Las correcciones que se ofrecen en el cartel.</summary>
        public readonly List<Fix> Fixes = new List<Fix>();

        /// <summary>Una corrección que reemplaza lo marcado (o <paramref name="expected"/> desde donde empieza lo marcado).</summary>
        public Diagnostic Replacing(string title, string expected, string replacement)
        {
            Fixes.Add(new Fix { Title = title, Line = Line, Column = Column, Expected = expected, Replacement = replacement });
            return this;
        }

        /// <summary>Texto para el portapapeles: ubicación, explicación y los mensajes originales.</summary>
        public string CopyText(string objectName)
        {
            var lines = new List<string>();
            string where = $"línea {Line + 1}, columna {Column + 1}";
            lines.Add(string.IsNullOrEmpty(objectName) ? char.ToUpper(where[0]) + where.Substring(1) : objectName + ", " + where);
            lines.Add(Message);
            if (!string.IsNullOrEmpty(Note))
                lines.Add(Note);
            lines.AddRange(Original.Where(o => o != Message).Select(o => "GeneXus: " + o));
            return string.Join("\r\n", lines);
        }

        public override string ToString() => $"{Severity} L{Line + 1} C{Column + 1} len{Length} [{Code}] {Message}" + (Note != null ? " (" + Note + ")" : "");
    }

}
