namespace GalNet.Core.Text;

/// <summary>Portable rich-dialogue tokens. Hosts may render styles or ignore them.</summary>
public enum RichTypewriterTokenKind { Text, LineBreak, Delay, Instant, SkipBoundary }

public readonly record struct RichTypewriterToken(
    RichTypewriterTokenKind Kind,
    string Text = "",
    bool IsBold = false,
    bool IsItalic = false,
    string? Color = null,
    int DelayMilliseconds = 0)
{
    /// <summary>Run size = control base size * scale + offset. Defaults preserve inherited size.</summary>
    public double FontSizeScale { get; init; } = 1;
    public double FontSizeOffset { get; init; }
}

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
        var size = (Scale: 1d, Offset: 0d);
        var sizes = new Stack<(double Scale, double Offset)>();

        void FlushText()
        {
            if (text.Length == 0) return;
            result.Add(new(RichTypewriterTokenKind.Text, text.ToString(), bold, italic, color)
                { FontSizeScale = size.Scale, FontSizeOffset = size.Offset });
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

            if (source[index] == '<' && TryReadSizeTag(source, index, size, sizes.Count > 0, out var sizeLength, out var nextSize, out var closing))
            {
                FlushText();
                if (closing) size = sizes.Pop();
                else { sizes.Push(size); size = nextSize; }
                index += sizeLength - 1;
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

    private static bool TryReadSizeTag(string source, int start, (double Scale, double Offset) parent,
        bool canClose, out int length, out (double Scale, double Offset) size, out bool closing)
    {
        length = 0;
        size = parent;
        closing = false;
        var end = source.IndexOf('>', start);
        if (end < 0) return false;
        var raw = source[(start + 1)..end].Trim();
        if (raw.Equals("/size", StringComparison.OrdinalIgnoreCase))
        {
            if (!canClose) return false;
            closing = true;
        }
        else
        {
            var match = System.Text.RegularExpressions.Regex.Match(raw,
                "^size\\s*=\\s*(?:\"(?<value>[^\"]+)\"|'(?<value>[^']+)'|(?<value>[^\\s\"']+))$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            var value = match.Groups["value"].Value.Trim();
            var em = value.EndsWith("em", StringComparison.OrdinalIgnoreCase);
            var percent = value.EndsWith('%');
            var relative = value.StartsWith('+') || value.StartsWith('-');
            var number = em ? value[..^2] : percent ? value[..^1] : value;
            if (!double.TryParse(number, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                    System.Globalization.CultureInfo.InvariantCulture, out var amount) || !double.IsFinite(amount)) return false;
            if (em || percent)
            {
                if (amount <= 0) return false;
                var factor = percent ? amount / 100 : amount;
                size = (parent.Scale * factor, parent.Offset * factor);
            }
            else if (relative) size = (parent.Scale, parent.Offset + amount);
            else
            {
                if (amount <= 0) return false;
                size = (0, amount);
            }
            if (!double.IsFinite(size.Scale) || !double.IsFinite(size.Offset)) return false;
        }
        length = end - start + 1;
        return true;
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
