namespace GalNet.Core.Text;

public enum TypewriterTokenKind { Text, Delay, Instant, SkipBoundary }

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
            if (!TypewriterDirectiveReader.TryRead(source, index, out var directive))
            {
                text.Append(source[index]);
                continue;
            }

            switch (directive.Kind)
            {
                case TypewriterDirectiveKind.EscapedBackslash:
                    text.Append('\\');
                    break;
                case TypewriterDirectiveKind.LineBreak:
                    text.Append('\n');
                    break;
                case TypewriterDirectiveKind.Delay:
                    FlushText();
                    tokens.Add(new(TypewriterTokenKind.Delay, string.Empty, directive.DelayMilliseconds));
                    break;
                case TypewriterDirectiveKind.Instant:
                    FlushText();
                    tokens.Add(new(TypewriterTokenKind.Instant, string.Empty));
                    break;
                case TypewriterDirectiveKind.SkipBoundary:
                    FlushText();
                    tokens.Add(new(TypewriterTokenKind.SkipBoundary, string.Empty));
                    break;
            }
            index += directive.Length - 1;
        }

        FlushText();
        return tokens;
    }
}
