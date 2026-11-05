using System;
using System.Collections.Generic;
using System.Linq;

namespace GxCodeCheck
{
    /// <summary>
    /// Convierte los mensajes de GeneXus en errores ubicados en el texto y los ordena para que se entiendan:
    /// junta los que caen en el mismo lugar, una línea que no es código (un comentario sin //) queda como un
    /// solo error, y lo que viene después de un paréntesis o unas comillas mal cerrados se marca como
    /// consecuencia de ese error.
    /// </summary>
    static class Analyzer
    {
        // Errores del parser que aparecen cuando algo anterior quedó mal cerrado
        static readonly HashSet<string> CascadeCodes = new HashSet<string>
        {
            "src0051", "src0052", "src0054", "src0069", "src0072", "src0079", "src0084", "src0102", "src0105",
            "src0114", "src0256", "src0257", "src0258", "src0265",
        };

        // Mensajes que citan lo que está escrito en su posición (un nombre desconocido, una función...)
        static readonly HashSet<string> NamesAtPosition = new HashSet<string>
        {
            "src0054", "src0210", "src0212", "src0216", "src0233", "src0234", "src0244", "src0246", "src0262",
            "src0265", "src0287", "src0294", "src0299", "src0301",
        };

        // Lo que GeneXus informa cuando un bloque quedó sin cerrar: el End que falta (en cualquier lado), los End
        // que sobran después, y las subrutinas que quedaron adentro del bloque y por eso "no existen"
        static readonly HashSet<string> BlockConsequenceCodes = new HashSet<string>
        {
            "src0052", "src0057", "src0058", "src0059", "src0060", "src0062", "src0063", "src0064", "src0080",
            "src0086", "src0089", "src0231", "src0255", "src0302", "src0304", "src0305", "src0306", "src0307",
        };

        static readonly HashSet<string> UnexpectedCodes = new HashSet<string> { "src0051", "src0054" };

        // Comandos que delimitan bloques o cláusulas: si los bloques del texto cierran bien y GeneXus dice que no
        // los esperaba, es que se perdió por un error anterior
        static readonly HashSet<string> BlockKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "If", "Else", "EndIf", "For", "Each", "EndFor", "Do", "While", "EndDo", "Case", "Otherwise", "EndCase",
            "Sub", "EndSub", "Event", "EndEvent", "New", "EndNew", "Where", "When", "Defined", "Order", "Unique",
            "Using", "Blocking", "Exit", "Return",
        };

        /// <summary>
        /// Los errores del parser sobre <paramref name="text"/>, más los chequeos propios del texto (los de
        /// bloques For/If/Sub..., con <paramref name="blocks"/>: en el código de procedimientos y eventos).
        /// </summary>
        public static List<Diagnostic> FromParser(string text, IEnumerable<RawMessage> messages, bool blocks = true)
        {
            var source = new SourceText(text);
            messages = messages.ToList();
            bool failed = messages.Any(m => m.Severity == Severity.Error);
            var diagnostics = messages
                .Select(m => Relocate(m, source))
                .Where(m => m.Line >= 1 && m.Line <= source.LineCount)
                .GroupBy(m => (m.Line, m.Char))
                .Select(g => Merge(g.GroupBy(m => m.Full).Select(d => d.First()).ToList()))
                .ToList();

            CollapseUnexpected(diagnostics, source);
            ExplainUnexpectedCommand(diagnostics, source);

            List<Diagnostic> checks = TextChecks.Run(source, structure: failed, blocks: blocks);
            foreach (Diagnostic check in checks)
            {
                // El parser a veces marca el mismo lugar (por ejemplo, unas comillas sin cerrar): queda la explicación propia
                Diagnostic same = diagnostics.FirstOrDefault(d => d.Line == check.Line && d.Column == check.Column);
                if (same != null)
                {
                    check.Original.AddRange(same.Original);
                    diagnostics.Remove(same);
                }
            }

            var structural = checks.Where(c => TextChecks.IsStructural(c.Code)).ToList();
            Diagnostic firstBlock = checks.Where(c => TextChecks.IsBlock(c.Code)).OrderBy(c => c.Line).FirstOrDefault();
            Diagnostic global = checks.Where(c => TextChecks.IsGlobal(c.Code)).OrderBy(c => c.Line).ThenBy(c => c.Column).FirstOrDefault();
            Diagnostic firstError = diagnostics.Where(d => d.Severity == Severity.Error).OrderBy(d => d.Line).ThenBy(d => d.Column).FirstOrDefault();
            foreach (Diagnostic d in diagnostics.OrderBy(d => d.Line).ThenBy(d => d.Column))
            {
                if (global != null && !IsAfter(global, d))
                {
                    // Desde ahí GeneXus no entiende nada: hasta el final todo es consecuencia
                    MarkConsequence(d, $"Probablemente es consecuencia del error de la línea {global.Line + 1} ({Short(global.Message)}): corregí ese primero.");
                    continue;
                }
                if (firstBlock == null && firstError != null && d != firstError && IsAfter(d, firstError) && IsUnexpectedBlockCommand(d))
                {
                    // Los bloques del texto cierran bien: GeneXus se perdió por el primer error
                    MarkConsequence(d, $"Probablemente es consecuencia del error de la línea {firstError.Line + 1}: corregí ese primero.");
                    continue;
                }
                if (firstBlock != null && BlockConsequenceCodes.Contains(d.Code))
                {
                    // Puede estar en cualquier lado, incluso antes del bloque (las llamadas a las subrutinas)
                    MarkConsequence(d, $"Probablemente es consecuencia del error de la línea {firstBlock.Line + 1} ({Short(firstBlock.Message)}): corregí ese primero.");
                    continue;
                }
                // El parser se recupera en la instrucción siguiente: lo que está más lejos es otro error
                Diagnostic cause = structural
                    .Where(s => IsAfter(d, s) && d.Line <= CascadeEnd(source, s.Line))
                    .OrderByDescending(s => s.Line).ThenByDescending(s => s.Column).FirstOrDefault();
                if (cause != null && CascadeCodes.Contains(d.Code))
                {
                    MarkConsequence(d, $"Probablemente es consecuencia del error de la línea {cause.Line + 1} ({Short(cause.Message)}): corregí ese primero.");
                }
                else if (d.Original.Any(o => o.Contains("'{0}'")))
                {
                    // Mensaje que GeneXus deja sin completar: siempre aparece después de otro error
                    Diagnostic previous = diagnostics.Concat(checks)
                        .Where(o => o != d && o.Severity != Severity.Info && IsAfter(d, o))
                        .OrderByDescending(o => o.Line).ThenByDescending(o => o.Column).FirstOrDefault();
                    if (previous != null)
                        MarkConsequence(d, $"Probablemente es consecuencia del error de la línea {previous.Line + 1}: corregí ese primero.");
                }
            }

            var result = diagnostics.Concat(checks).ToList();
            foreach (Diagnostic d in result)
            {
                FitToToken(d, source);
                AddSuggestionFix(d, source);
            }
            return result.OrderBy(d => d.Line).ThenBy(d => d.Column).ToList();
        }

        /// <summary>Un error de la especificación (solo trae la línea), o null si la línea no está en el texto.</summary>
        public static Diagnostic FromSpecifier(RawMessage message, SourceText source)
        {
            if (message.Line < 1 || message.Line > source.LineCount)
                return null;
            int line = message.Line - 1;
            var d = new Diagnostic
            {
                Severity = message.Severity,
                Origin = Origin.Specifier,
                Code = message.Code,
                Message = Explainer.Explain(message.Code, message.Text) ?? message.Text,
                Line = line,
            };
            d.Original.Add(message.Full);
            if (message.Char > 0)
            {
                d.Column = message.Char - 1;
                d.Length = 0;
                FitToToken(d, source);
            }
            else
            {
                (d.Column, d.Length) = Explainer.SpecTarget(message.Code, message.Text, source, line);
            }
            return d;
        }

        /// <summary>
        /// Una variable no definida (la especificación la autodefine y avisa sin decir dónde): se marcan todos
        /// los lugares del texto donde aparece.
        /// </summary>
        public static List<Diagnostic> FromUndefinedVariable(RawMessage message, SourceText source)
        {
            var result = new List<Diagnostic>();
            string name = Explainer.UndefinedVariable(message.Code, message.Text);
            if (name == null)
                return result;
            string explanation = Explainer.Explain(message.Code, message.Text) ?? message.Text;
            for (int line = 0; line < source.LineCount; line++)
            {
                for (int at = source.FindWord(line, "&" + name); at >= 0; at = source.FindWord(line, "&" + name, at + 1))
                {
                    var d = new Diagnostic
                    {
                        Severity = Severity.Warning,
                        Origin = Origin.Specifier,
                        Code = message.Code,
                        Message = explanation,
                        Line = line,
                        Column = at,
                        Length = name.Length + 1,
                    };
                    d.Original.Add(message.Full);
                    d.Fixes.Add(TypeChecker.DeclareFix("&" + name, line, at));
                    result.Add(d);
                }
            }
            return result;
        }

        /// <summary>
        /// Si el nombre que cita el mensaje no está en la línea informada pero sí en una sola otra, el mensaje va
        /// ahí. Resguardo para las líneas que GeneXus informa raro (en negativo en los objetos con WorkWithPlus).
        /// </summary>
        static RawMessage Relocate(RawMessage message, SourceText source)
        {
            if (!NamesAtPosition.Contains(message.Code))
                return message;
            string name = Explainer.Arg(message.Text, 0);
            if (name.Length == 0 || !name.All(c => SourceText.IsNameChar(c) || c == '&'))
                return message;
            if (message.Line >= 1 && message.Line <= source.LineCount && source.FindWord(message.Line - 1, name) >= 0)
                return message;
            var lines = Enumerable.Range(0, source.LineCount).Where(l => source.FindWord(l, name) >= 0).Take(2).ToList();
            if (lines.Count != 1)
                return message;
            return new RawMessage
            {
                Code = message.Code,
                Text = message.Text,
                Severity = message.Severity,
                Line = lines[0] + 1,
                Char = source.FindWord(lines[0], name) + 1,
            };
        }

        static Diagnostic Merge(List<RawMessage> group)
        {
            RawMessage primary = group[0];
            var d = new Diagnostic
            {
                Severity = group.Min(m => m.Severity),
                Origin = Origin.Parser,
                Code = primary.Code,
                Line = primary.Line - 1,
                Column = Math.Max(0, primary.Char - 1),
                Length = 0,
            };
            d.Original.AddRange(group.Select(m => m.Full));

            // "&Numero = 5 x": GeneXus pide una asignación ("x = ...") y dice que x no es un atributo. Lo que pasa es que sobra la x.
            RawMessage unknown = group.FirstOrDefault(m => m.Code == "src0265");
            if (unknown != null && group.Any(m => m.Code == "src0256" || m.Code == "src0257"))
            {
                string word = Explainer.Arg(unknown.Text, 0);
                d.Code = unknown.Code;
                d.Message = $"Sobra '{word}': lo que está antes ya es una instrucción completa. Si '{word}' es otra instrucción, va en otra línea.";
                return d;
            }
            d.Message = Explainer.Explain(primary.Code, primary.Text) ?? primary.Text;
            return d;
        }

        /// <summary>Varias "expresión inesperada" en una línea son una sola cosa: la línea entera no es código.</summary>
        static void CollapseUnexpected(List<Diagnostic> diagnostics, SourceText source)
        {
            foreach (var line in diagnostics.Where(d => UnexpectedCodes.Contains(d.Code) && !d.Original.Any(o => o.Contains("'{0}'")))
                                            .GroupBy(d => d.Line).Where(g => g.Count() >= 2).ToList())
            {
                var items = line.OrderBy(d => d.Column).ToList();
                Diagnostic last = items[items.Count - 1];
                int end = last.Column + Math.Max(1, source.TokenLength(last.Line, last.Column));
                string code = source.CodeLine(line.Key);
                bool looksLikeText = code.IndexOf('&') < 0 && code.IndexOf('=') < 0;
                var merged = new Diagnostic
                {
                    Severity = Severity.Error,
                    Origin = Origin.Parser,
                    Code = items[0].Code,
                    Line = line.Key,
                    Column = items[0].Column,
                    Length = end - items[0].Column,
                    Message = looksLikeText
                        ? $"Esta línea no es código GeneXus ({items.Count} palabras inesperadas). Si es un comentario, empezala con '//'."
                        : $"En esta línea sobran o faltan cosas desde '{Explainer.Arg(items[0].Original[0], 0)}' ({items.Count} expresiones inesperadas).",
                };
                foreach (Diagnostic item in items)
                {
                    merged.Original.AddRange(item.Original);
                    diagnostics.Remove(item);
                }
                diagnostics.Add(merged);
            }
        }

        /// <summary>"Unexpected command 'EndFor'", "'Where' is an unexpected expression": un comando de bloque fuera de lugar.</summary>
        static bool IsUnexpectedBlockCommand(Diagnostic d)
        {
            if (d.Code != "src0052" && d.Code != "src0054")
                return false;
            string word = d.Original.Count > 0 ? Explainer.Arg(d.Original[0], 0) : "";
            return word == "{0}" || BlockKeywords.Contains(word);
        }

        /// <summary>
        /// Un comando al principio de la línea que GeneXus "no esperaba" ('If' is an unexpected expression): el
        /// problema está al final de la línea anterior, que quedó sin terminar y GeneXus la une con esta.
        /// </summary>
        static void ExplainUnexpectedCommand(List<Diagnostic> diagnostics, SourceText source)
        {
            foreach (Diagnostic d in diagnostics.Where(d => d.Code == "src0054" && d.Original.Count == 1))
            {
                string word = Explainer.Arg(d.Original[0], 0);
                var extent = source.CodeExtent(d.Line);
                if (!BlockKeywords.Contains(word) || extent == null || extent.Value.Start != d.Column)
                    continue;
                int previous = d.Line - 1;
                while (previous >= 0 && source.IsBlank(previous))
                    previous--;
                if (previous < 0)
                    continue;
                d.Message = $"GeneXus no esperaba '{word}' acá: la línea {previous + 1} quedó sin terminar y la une con esta. " +
                            $"Revisá el final de la línea {previous + 1}: ¿sobra un '.', un operador o una coma?";
            }
        }

        static void MarkConsequence(Diagnostic d, string note)
        {
            d.Severity = Severity.Info;
            d.Note = note;
        }

        /// <summary>Hasta qué línea llegan los errores que provoca uno de estructura: la suya y las dos siguientes con código.</summary>
        static int CascadeEnd(SourceText source, int line)
        {
            int end = line;
            for (int found = 0; found < 2 && end + 1 < source.LineCount; )
            {
                end++;
                if (!source.IsBlank(end))
                    found++;
            }
            return end;
        }

        static bool IsAfter(Diagnostic d, Diagnostic other) =>
            d.Line > other.Line || (d.Line == other.Line && d.Column > other.Column);

        /// <summary>Una función que no existe y se parece a una que sí (src0294): cambiarla por esa.</summary>
        static void AddSuggestionFix(Diagnostic d, SourceText source)
        {
            if (d.Code != "src0294" || d.Severity == Severity.Info || d.Original.Count == 0)
                return;
            string line = source.Line(d.Line);
            if (d.Column + d.Length > line.Length)
                return;
            string word = line.Substring(d.Column, d.Length);
            string suggestion = Explainer.Suggest(word);
            if (suggestion != null && word.All(SourceText.IsNameChar))
                d.Replacing($"Cambiar por '{suggestion}'", word, suggestion);
        }

        /// <summary>Si el error no tiene largo, marca lo que hay en su columna; al final de la línea, lo último que tiene.</summary>
        static void FitToToken(Diagnostic d, SourceText source)
        {
            if (d.Length > 0)
                return;
            int length = source.TokenLength(d.Line, d.Column);
            if (length > 0)
            {
                d.Length = length;
                return;
            }
            var extent = source.CodeExtent(d.Line);
            if (extent == null)
            {
                d.Length = 1;
            }
            else if (d.Column > extent.Value.End)
            {
                d.Column = extent.Value.End;
                d.Length = 1;
            }
            else
            {
                // Sobre un espacio: hasta lo siguiente que tenga
                int next = d.Column;
                string text = source.Line(d.Line);
                while (next < text.Length && char.IsWhiteSpace(text[next]))
                    next++;
                d.Column = next;
                d.Length = Math.Max(1, source.TokenLength(d.Line, next));
            }
        }

        static string Short(string message)
        {
            int cut = message.IndexOf(':');
            string text = cut > 0 ? message.Substring(0, cut) : message;
            return text.TrimEnd('.');
        }
    }
}
