using System.Collections.Generic;
using System.Text;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>
    /// Splits pretty-printed JSON into lines and colors keys, strings and numbers with rich
    /// text tags using the theme's colors; finds the lines that mention an entity id so the
    /// inspector can highlight them. Pure text logic.
    /// </summary>
    public static class JsonHighlighter
    {
        public struct Line
        {
            public int Number;
            public string Raw;
            public string Rich;
            public bool Highlight;
        }

        public static List<Line> Lines(string json, string highlightId, string keyColor, string stringColor, string numberColor)
        {
            var result = new List<Line>();
            if (json == null) return result;
            var lines = json.Replace("\r\n", "\n").Split('\n');
            var needle = highlightId != null ? "\"" + highlightId + "\"" : null;
            for (int i = 0; i < lines.Length; i++)
            {
                result.Add(new Line
                {
                    Number = i + 1,
                    Raw = lines[i],
                    Rich = Colorize(lines[i], keyColor, stringColor, numberColor),
                    Highlight = needle != null && lines[i].Contains(needle),
                });
            }
            return result;
        }

        public static int FirstHighlight(List<Line> lines)
        {
            for (int i = 0; i < lines.Count; i++) if (lines[i].Highlight) return i;
            return -1;
        }

        /// <summary>Rich-text markup for one JSON line. Angle brackets in content are escaped.</summary>
        public static string Colorize(string line, string keyColor, string stringColor, string numberColor)
        {
            var sb = new StringBuilder(line.Length + 40);
            int i = 0;
            // leading whitespace
            while (i < line.Length && line[i] == ' ') { sb.Append(' '); i++; }
            // key?
            if (i < line.Length && line[i] == '"')
            {
                int end = ClosingQuote(line, i);
                if (end > 0 && end + 1 < line.Length && line[end + 1] == ':')
                {
                    sb.Append("<color=").Append(keyColor).Append('>').Append(Esc(line.Substring(i, end - i + 1))).Append("</color>:");
                    i = end + 2;
                }
            }
            // value
            var rest = line.Substring(i);
            var trimmed = rest.TrimStart();
            var pad = rest.Length - trimmed.Length;
            sb.Append(rest.Substring(0, pad));
            var body = trimmed.TrimEnd(',');
            var comma = trimmed.EndsWith(",") ? "," : "";
            if (body.Length >= 2 && body[0] == '"' && body[body.Length - 1] == '"')
                sb.Append("<color=").Append(stringColor).Append('>').Append(Esc(body)).Append("</color>").Append(comma);
            else if (body.Length > 0 && (char.IsDigit(body[0]) || body[0] == '-' || body == "true" || body == "false" || body == "null"))
                sb.Append("<color=").Append(numberColor).Append('>').Append(Esc(body)).Append("</color>").Append(comma);
            else
                sb.Append(Esc(trimmed));
            return sb.ToString();
        }

        private static int ClosingQuote(string s, int open)
        {
            for (int j = open + 1; j < s.Length; j++)
            {
                if (s[j] == '\\') { j++; continue; }
                if (s[j] == '"') return j;
            }
            return -1;
        }

        private static string Esc(string s) => s.Replace("<", "‹").Replace(">", "›");
    }
}
