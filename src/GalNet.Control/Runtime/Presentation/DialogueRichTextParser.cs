using Avalonia.Controls.Documents;

namespace GalNet.Control.Runtime.Presentation;

/// <summary>Parses the raw dialogue markup used by the built-in game dialogue view.</summary>
internal static class DialogueRichTextParser
{
    public static IReadOnlyList<DialogueSegment> Parse(string source)
    {
        var result = new List<DialogueSegment>();
        var buffer = new System.Text.StringBuilder();
        var bold = false;
        var italic = false;
        string? color = null;
        var colors = new Stack<string?>();

        void Flush()
        {
            if (buffer.Length == 0) return;
            result.Add(new DialogueSegment(DialogueSegmentKind.Text, buffer.ToString(), bold, italic, color));
            buffer.Clear();
        }

        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\\' && index + 1 < source.Length)
            {
                if (source[index + 1] == 'n')
                {
                    Flush(); result.Add(new DialogueSegment(DialogueSegmentKind.LineBreak)); index++; continue;
                }
                if (source[index + 1] == 'd')
                {
                    if (index + 2 < source.Length && source[index + 2] == '-')
                    {
                        Flush(); result.Add(new DialogueSegment(DialogueSegmentKind.Instant)); index += 2; continue;
                    }
                    var end = source.IndexOf('}', index + 2);
                    if (index + 2 < source.Length && source[index + 2] == '{' && end >= 0 && int.TryParse(source[(index + 3)..end], out var milliseconds) && milliseconds >= 0)
                    {
                        Flush(); result.Add(new DialogueSegment(DialogueSegmentKind.Delay, delayMilliseconds: milliseconds)); index = end; continue;
                    }
                }
            }

            if (source[index] == '<' && TryReadTag(source, index, out var length, out var tag, out var tagColor))
            {
                Flush();
                switch (tag)
                {
                    case "b": bold = true; break;
                    case "/b": bold = false; break;
                    case "i": italic = true; break;
                    case "/i": italic = false; break;
                    case "color": colors.Push(color); color = tagColor; break;
                    case "/color": color = colors.Count > 0 ? colors.Pop() : null; break;
                    case "br": result.Add(new DialogueSegment(DialogueSegmentKind.LineBreak)); break;
                }
                index += length - 1;
                continue;
            }

            buffer.Append(source[index]);
        }

        Flush();
        return result;
    }

    private static bool TryReadTag(string source, int start, out int length, out string tag, out string? color)
    {
        length = 0; tag = string.Empty; color = null;
        var end = source.IndexOf('>', start);
        if (end < 0) return false;
        var raw = source[(start + 1)..end].Trim();
        if (raw.Equals("b", StringComparison.OrdinalIgnoreCase) || raw.Equals("/b", StringComparison.OrdinalIgnoreCase) || raw.Equals("i", StringComparison.OrdinalIgnoreCase) || raw.Equals("/i", StringComparison.OrdinalIgnoreCase) || raw.Equals("br", StringComparison.OrdinalIgnoreCase)) tag = raw.ToLowerInvariant();
        else if (raw.Equals("/span", StringComparison.OrdinalIgnoreCase) || raw.Equals("/color", StringComparison.OrdinalIgnoreCase)) tag = "/color";
        else
        {
            var match = System.Text.RegularExpressions.Regex.Match(raw, "^(?:span\\s+(?:color|foreground)\\s*=|color\\s*=)\\s*[\"']?([^\\s\"'>]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            tag = "color"; color = match.Groups[1].Value;
        }
        length = end - start + 1;
        return true;
    }
}

internal enum DialogueSegmentKind { Text, LineBreak, Delay, Instant }

internal sealed class DialogueSegment(DialogueSegmentKind kind, string text = "", bool bold = false, bool italic = false, string? color = null, int delayMilliseconds = 0)
{
    public DialogueSegmentKind Kind { get; } = kind;
    public string Text { get; } = text;
    public bool Bold { get; } = bold;
    public bool Italic { get; } = italic;
    public string? Color { get; } = color;
    public int DelayMilliseconds { get; } = delayMilliseconds;
    public Run? Run { get; set; }
}
