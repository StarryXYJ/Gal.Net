using System.Globalization;

namespace GalNet.Core.I18n;

/// <summary>
/// 语言标识。内部使用标准 CultureInfo 格式（"zh-CN"），
/// 但允许 GalNet 内部约定格式（"zh_cn"）输入。
/// </summary>
public sealed class I18nLocale
{
    /// <summary>标准 .NET Culture 名称（如 "zh-CN", "en-US"）</summary>
    public string Code { get; }

    public I18nLocale(string code)
    {
        Code = Normalize(code);
    }

    /// <summary>对应的 CultureInfo 对象</summary>
    public CultureInfo ToCultureInfo() => new(Code);

    /// <summary>规范化：将 "zh_cn" 转为 "zh-CN"</summary>
    private static string Normalize(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentNullException(nameof(code));

        // 下划线转短横，确保格式一致
        code = code.Replace('_', '-');

        // 确保后缀部分大写（如 "zh-cn" → "zh-CN"）
        var dashIndex = code.IndexOf('-');
        if (dashIndex > 0 && dashIndex < code.Length - 1)
        {
            code = code[..(dashIndex + 1)] + code[(dashIndex + 1)..].ToUpperInvariant();
        }

        return code;
    }

    public override string ToString() => Code;

    public override bool Equals(object? obj) =>
        obj is I18nLocale other && Code == other.Code;

    public override int GetHashCode() => Code.GetHashCode();

    // ── 常用预设 ──
    public static readonly I18nLocale ZhCn = new("zh-CN");
    public static readonly I18nLocale ZhTw = new("zh-TW");
    public static readonly I18nLocale EnUs = new("en-US");
    public static readonly I18nLocale JaJp = new("ja-JP");
    public static readonly I18nLocale KoKr = new("ko-KR");
}
