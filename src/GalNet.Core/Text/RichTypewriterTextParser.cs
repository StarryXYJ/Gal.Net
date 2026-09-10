namespace GalNet.Core.Text;

/// <summary>Portable dialogue markup parser. Hosts may render styles or ignore them.</summary>
public enum RichTypewriterTokenKind { Text, LineBreak, Delay, Instant }

public readonly record struct RichTypewriterToken(
    RichTypewriterTokenKind Kind,
    string Text = "",
    bool IsBold = false,
    bool IsItalic = false,
    string? Color = null,
    int DelayMilliseconds = 0);

/// <summary>
/// Parses typewriter directives plus the lightweight dialogue tags <c>b</c>, <c>i</c>,
/// <c>color</c>, <c>span color</c>, and <c>br</c>. Unknown tags remain literal text.
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
            if (source[index] == '\\' && index + 1 < source.Length)
            {
                if (source[index + 1] == 'n')
                {
                    FlushText();
                    result.Add(new(RichTypewriterTokenKind.LineBreak));
                    index++;
                    continue;
                }

                if (source[index + 1] == 'd')
                {
                    if (index + 2 < source.Length && source[index + 2] == '-')
                    {
                        FlushText();
                        result.Add(new(RichTypewriterTokenKind.Instant));
                        index += 2;
                        continue;
                    }

                    var end = index + 2;
                    var delayText = string.Empty;
                    if (end < source.Length && source[end] == '{')
                    {
                        var close = source.IndexOf('}', end + 1);
                        if (close < 0)
                        {
                            text.Append(source[index]);
                            continue;
                        }
                        delayText = source[(end + 1)..close];
                        end = close + 1;
                    }
                    else
                    {
                        var digitsStart = end;
                        while (end < source.Length && char.IsDigit(source[end])) end++;
                        if (digitsStart == end)
                        {
                            text.Append(source[index]);
                            continue;
                        }
                        delayText = source[digitsStart..end];
                    }

                    if (int.TryParse(delayText, out var milliseconds) && milliseconds >= 0)
                    {
                        FlushText();
                        result.Add(new(RichTypewriterTokenKind.Delay, DelayMilliseconds: milliseconds));
                        index = end - 1;
                        continue;
                    }
                }
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
