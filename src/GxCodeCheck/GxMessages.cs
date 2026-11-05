using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Artech.Architecture.Common.Location;
using Artech.Common.Diagnostics;
using Artech.Common.Location;

namespace GxCodeCheck
{
    /// <summary>Lee los mensajes de error que GeneXus escribe en el Output.</summary>
    static class GxMessages
    {
        static readonly Regex CodedText = new Regex(@"^\s*(?:error|warning)?\s*(?<code>[a-z]{3}\d{4})\s*:\s*(?<text>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        /// <summary>
        /// Un mensaje de GeneXus con su código, texto y posición (línea y columna, la primera que tenga), o null si
        /// no es un error ni una advertencia. <paramref name="objectPosition"/> es el objeto y la parte a los que se
        /// refiere, si los dice.
        /// </summary>
        public static RawMessage ToRaw(BaseMessage message, out IKBObjectPosition objectPosition)
        {
            objectPosition = null;
            if (message == null || (message.Level != MessageLevel.Error && message.Level != MessageLevel.Warning))
                return null;
            var raw = new RawMessage
            {
                Severity = message.Level == MessageLevel.Error ? Severity.Error : Severity.Warning,
                Text = message.Text ?? "",
            };
            if (message is OutputError error)
            {
                raw.Code = error.ErrorCode ?? "";
                List<IPosition> positions = error.Positions?.ToList() ?? new List<IPosition>();
                objectPosition = positions.OfType<IKBObjectPosition>().FirstOrDefault();
                TextPosition text = positions.OfType<TextPosition>().FirstOrDefault(p => p.Line != 0);
                if (text != null)
                {
                    // En los objetos que modifica WorkWithPlus ("Source (by WorkWithPlus)") la línea viene en negativo
                    raw.Line = Math.Abs(text.Line);
                    raw.Char = text.Char;
                }
            }
            // El especificador manda el código dentro del texto ("spc0010: Type mismatch...")
            Match coded = CodedText.Match(raw.Text);
            if (coded.Success)
            {
                if (raw.Code.Length == 0)
                    raw.Code = coded.Groups["code"].Value;
                raw.Text = coded.Groups["text"].Value.Trim();
            }
            raw.Code = raw.Code.ToLowerInvariant();
            return raw;
        }
    }
}
