namespace GalNet.Core.Text;

public enum TypewriterTokenKind { Text, Delay, Instant }

public readonly record struct TypewriterToken(TypewriterTokenKind Kind, string Text, int DelayMilliseconds = 0);

/// <summary>Parses the portable dialogue directives used by every game view.</summary>
public static class TypewriterTextParser
{
    public static IReadOnlyList<TypewriterToken> Parse(string? source)
    {
        source = source?.Replace("\r\n", "\n").Replace('\r', '\n') ?? string.Empty;
        var tokens = new List<TypewriterToken>();
        var text = new System.Text.StringBuilder();

        void FlushText()
        {
            if (text.Length == 0) return;
            tokens.Add(new(TypewriterTokenKind.Text, text.ToString()));
            text.Clear();
        }

        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] != '\\' || index + 1 >= source.Length)
            {
                text.Append(source[index]);
                continue;
            }

            if (source[index + 1] == 'n')
            {
                text.Append('\n');
                index++;
                continue;
            }

            if (source[index + 1] != 'd')
            {
                text.Append(source[index]);
                continue;
            }

            if (index + 2 < source.Length && source[index + 2] == '-')
            {
                FlushText();
                tokens.Add(new(TypewriterTokenKind.Instant, string.Empty));
                index += 2;
                continue;
            }

            var end = index + 2;
            var body = string.Empty;
            if (end < source.Length && source[end] == '{')
            {
                var close = source.IndexOf('}', end + 1);
                if (close < 0) { text.Append(source[index]); continue; }
                body = source[(end + 1)..close];
                end = close + 1;
            }
            else
            {
                var digitsStart = end;
                while (end < source.Length && char.IsDigit(source[end])) end++;
                if (digitsStart == end) { text.Append(source[index]); continue; }
                body = source[digitsStart..end];
            }

            var milliseconds = 0;
            if (!int.TryParse(body, out milliseconds) || milliseconds < 0)
            {
                text.Append(source[index]);
                continue;
            }

            FlushText();
            tokens.Add(new(TypewriterTokenKind.Delay, string.Empty, milliseconds));
            index = end - 1;
        }

        FlushText();
        return tokens;
    }

}
