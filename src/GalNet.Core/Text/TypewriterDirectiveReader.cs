namespace GalNet.Core.Text;

internal enum TypewriterDirectiveKind
{
    LineBreak,
    Delay,
    Instant,
    SkipBoundary,
    EscapedBackslash
}

internal readonly record struct TypewriterDirective(
    TypewriterDirectiveKind Kind,
    int Length,
    int DelayMilliseconds = 0);

/// <summary>Shared recognition for portable backslash directives.</summary>
internal static class TypewriterDirectiveReader
{
    public static bool TryRead(string source, int start, out TypewriterDirective directive)
    {
        directive = default;
        if (start < 0 || start + 1 >= source.Length || source[start] != '\\')
            return false;

        if (source[start + 1] == '\\')
        {
            directive = new(TypewriterDirectiveKind.EscapedBackslash, 2);
            return true;
        }
        if (source[start + 1] == 'n')
        {
            directive = new(TypewriterDirectiveKind.LineBreak, 2);
            return true;
        }
        const string skip = "\\skip";
        if (source.AsSpan(start).StartsWith(skip, StringComparison.Ordinal) &&
            IsDirectiveBoundary(source, start + skip.Length))
        {
            directive = new(TypewriterDirectiveKind.SkipBoundary, skip.Length);
            return true;
        }
        if (source[start + 1] != 'd')
            return false;
        if (start + 2 < source.Length && source[start + 2] == '-')
        {
            directive = new(TypewriterDirectiveKind.Instant, 3);
            return true;
        }

        var end = start + 2;
        string delayText;
        if (end < source.Length && source[end] == '{')
        {
            var close = source.IndexOf('}', end + 1);
            if (close < 0) return false;
            delayText = source[(end + 1)..close];
            end = close + 1;
        }
        else
        {
            var digitsStart = end;
            while (end < source.Length && char.IsDigit(source[end])) end++;
            if (digitsStart == end) return false;
            delayText = source[digitsStart..end];
        }

        if (!int.TryParse(delayText, out var milliseconds) || milliseconds < 0)
            return false;
        directive = new(TypewriterDirectiveKind.Delay, end - start, milliseconds);
        return true;
    }

    private static bool IsDirectiveBoundary(string source, int index) =>
        index >= source.Length || !(char.IsAsciiLetterOrDigit(source[index]) || source[index] == '_');
}
