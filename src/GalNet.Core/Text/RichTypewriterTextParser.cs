namespace GalNet.Core.Text;

/// <summary>Portable rich-dialogue tokens. Hosts may render styles or ignore them.</summary>
public enum RichTypewriterTokenKind { Text, LineBreak, Delay, Instant, SkipBoundary }

public readonly record struct RichTypewriterToken(
    RichTypewriterTokenKind Kind,
    string Text = "",
    bool IsBold = false,
    bool IsItalic = false,
    string? Color = null,
    int DelayMilliseconds = 0);

/// <summary>
/// Parses portable typewriter directives plus lightweight dialogue tags. Unknown
/// tags and unknown backslash directives remain literal text.
/// </summary>
public static class RichTypewriterTextParser
{
    public static IReadOnlyList<RichTypewriterToken> Parse(string? source)
    {
        source = source?.Replace("\r\n", "\n").Replace('\r', '\n') ?? string.Empty;
        var result = new List<RichTypewriterToken>();
        var text = new System.Text.StringBuilder();
        var bold = false;
        var italic = false;
        string? color = null;
        var colors = new Stack<string?>();

        void FlushText()
        {
            if (text.Length == 0) return;
            result.Add(new(RichTypewriterTokenKind.Text, text.ToString(), bold, italic, color));
            text.Clear();
        }

        for (var index = 0; index < source.Length; index++)
        {
            if (TypewriterDirectiveReader.TryRead(source, index, out var directive))
            {
                switch (directive.Kind)
                {
                    case TypewriterDirectiveKind.EscapedBackslash:
                        text.Append('\\');
                        break;
                    case TypewriterDirectiveKind.LineBreak:
                        FlushText();
                        result.Add(new(RichTypewriterTokenKind.LineBreak));
                        break;
                    case TypewriterDirectiveKind.Delay:
                        FlushText();
                        result.Add(new(RichTypewriterTokenKind.Delay, DelayMilliseconds: directive.DelayMilliseconds));
                        break;
                    case TypewriterDirectiveKind.Instant:
                        FlushText();
                        result.Add(new(RichTypewriterTokenKind.Instant));
                        break;
                    case TypewriterDirectiveKind.SkipBoundary:
                        FlushText();
                        result.Add(new(RichTypewriterTokenKind.SkipBoundary));
                        break;
                }
                index += directive.Length - 1;
                continue;
            }

            if (source[index] == '<' && TryReadTag(source, index, out var length, out var tag, out var tagColor))
            {
                FlushText();
                switch (tag)
                {
                    case "b": bold = true; break;
                    case "/b": bold = false; break;
                    case "i": italic = true; break;
                    case "/i": italic = false; break;
                    case "color": colors.Push(color); color = tagColor; break;
                    case "/color": color = colors.Count > 0 ? colors.Pop() : null; break;
                    case "br": result.Add(new(RichTypewriterTokenKind.LineBreak)); break;
                }
                index += length - 1;
                continue;
            }

            if (source[index] == '\n')
            {
                FlushText();
                result.Add(new(RichTypewriterTokenKind.LineBreak));
                continue;
            }

            text.Append(source[index]);
        }

        FlushText();
        return result;
    }

    private static bool TryReadTag(string source, int start, out int length, out string tag, out string? color)
    {
        length = 0;
        tag = string.Empty;
        color = null;
        var end = source.IndexOf('>', start);
        if (end < 0) return false;
        var raw = source[(start + 1)..end].Trim();
        if (raw.Equals("b", StringComparison.OrdinalIgnoreCase) || raw.Equals("/b", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("i", StringComparison.OrdinalIgnoreCase) || raw.Equals("/i", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            tag = raw.ToLowerInvariant();
        }
        else if (raw.Equals("/span", StringComparison.OrdinalIgnoreCase) || raw.Equals("/color", StringComparison.OrdinalIgnoreCase))
        {
            tag = "/color";
        }
        else
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                raw,
                "^(?:span\\s+(?:color|foreground)\\s*=|color\\s*=)\\s*[\"']?([^\\s\"'>]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            tag = "color";
            color = match.Groups[1].Value;
        }

        length = end - start + 1;
        return true;
    }
}
