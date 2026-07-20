using GalNet.Core.Serialization;

namespace GeneralTest.Serialization;

public class GalgroupParserTests
{
    [Test]
    public void Parse_Empty_Should_Return_Empty()
    {
        var result = GalgroupParser.Parse("");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Parse_SingleLine_Should_Extract_Type_And_Params()
    {
        var result = GalgroupParser.Parse("显示图像 : id:bg; 文件引用:bg_classroom; z:0; 转场:fade");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].EntryType, Is.EqualTo("显示图像"));
        Assert.That(result[0].Params["id"], Is.EqualTo("bg"));
        Assert.That(result[0].Params["文件引用"], Is.EqualTo("bg_classroom"));
        Assert.That(result[0].Params["z"], Is.EqualTo("0"));
        Assert.That(result[0].Params["转场"], Is.EqualTo("fade"));
    }

    [Test]
    public void Parse_MultipleLines_Should_Return_All()
    {
        var content = """
            播放音频 : 文件引用:bgm_01; 轨道:bgm; 播放形式:repeat
            显示图像 : id:bg; 文件引用:bg_classroom
            表达式求值 : 目标变量:score; 表达式:[score] + 10
            """;

        var result = GalgroupParser.Parse(content);

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].EntryType, Is.EqualTo("播放音频"));
        Assert.That(result[1].EntryType, Is.EqualTo("显示图像"));
        Assert.That(result[2].EntryType, Is.EqualTo("表达式求值"));
    }

    [Test]
    public void Parse_Should_Skip_Comments_And_Empty_Lines()
    {
        var content = """
            // 这是注释
            等待 : 持续时间:2.0

            变量设置 : 变量:player.score; 值:100; 类型:int
            """;

        var result = GalgroupParser.Parse(content);
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].EntryType, Is.EqualTo("等待"));
        Assert.That(result[1].EntryType, Is.EqualTo("变量设置"));
    }

    [Test]
    public void Parse_TypeOnly_Should_Have_Empty_Params()
    {
        var result = GalgroupParser.Parse("停止视频");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].EntryType, Is.EqualTo("停止视频"));
        Assert.That(result[0].Params, Is.Empty);
    }

    [Test]
    public void Parse_LineNumbers_Should_Be_Correct()
    {
        var content = "line1\nline2\n\n// comment\nline4";
        var result = GalgroupParser.Parse(content);

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].LineNumber, Is.EqualTo(1));
        Assert.That(result[1].LineNumber, Is.EqualTo(2));
        // line 3 skipped (empty), line 4 skipped (comment)
        Assert.That(result[2].LineNumber, Is.EqualTo(5));
    }

    // ── Escape 测试 ──

    [Test]
    public void Escape_Should_Protect_Colon()
    {
        var escaped = GalgroupParser.Escape("hello: world");
        Assert.That(escaped, Is.EqualTo(@"hello\: world"));
    }

    [Test]
    public void Escape_Should_Protect_Semicolon()
    {
        var escaped = GalgroupParser.Escape("a; b");
        Assert.That(escaped, Is.EqualTo(@"a\; b"));
    }

    [Test]
    public void Escape_Should_Protect_Backslash()
    {
        // C:\path\to\file → C\:\\path\\to\\file（: 和 \ 都会被转义）
        var escaped = GalgroupParser.Escape(@"C:\path\to\file");
        Assert.That(escaped, Does.Contain(@"\\path\\to\\file"));
    }

    [Test]
    public void Unescape_Should_Restore_Original()
    {
        var original = @"hello: world; C:\path";
        var escaped = GalgroupParser.Escape(original);
        var unescaped = GalgroupParser.Unescape(escaped);

        Assert.That(unescaped, Is.EqualTo(original));
    }

    [Test]
    public void Parse_Should_Handle_Escaped_Colon_In_Value()
    {
        // 内容中含有 : ，转义后为 \:
        var line = @"单行文本 : 名称:Alice; 内容:你好\:世界";
        var result = GalgroupParser.Parse(line);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Params["内容"], Is.EqualTo("你好:世界"));
    }

    [Test]
    public void Parse_Should_Handle_Escaped_Semicolon_In_Value()
    {
        // 内容中含有 ; ，转义后为 \;
        var line = @"单行文本 : 名称:Alice; 内容:a\;b\;c";
        var result = GalgroupParser.Parse(line);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Params["内容"], Is.EqualTo("a;b;c"));
    }

    [Test]
    public void Parse_Should_Handle_Escaped_Colon_In_TypeName()
    {
        // 条目类型不含 : 无需转义，但若理论上有，不应破坏
        var line = @"显示图像 : id:bg; 特效参数:intensity\:5";
        var result = GalgroupParser.Parse(line);

        Assert.That(result[0].Params["特效参数"], Is.EqualTo("intensity:5"));
    }

    [Test]
    public void Serialize_Should_Escape_Special_Chars()
    {
        var parameters = new Dictionary<string, string>
        {
            ["名称"] = "Alice",
            ["内容"] = "你好:世界;再见"
        };

        var line = GalgroupParser.Serialize("单行文本", parameters);

        // 序列化后内容中的 : 和 ; 应该被转义
        Assert.That(line, Does.Contain(@"你好\:世界\;再见"));

        // 往返测试
        var parsed = GalgroupParser.Parse(line);
        Assert.That(parsed[0].Params["内容"], Is.EqualTo("你好:世界;再见"));
    }

    [Test]
    public void Escape_Then_Unescape_Should_Be_Idempotent()
    {
        var testCases = new[]
        {
            "",
            "hello",
            "a:b",
            "a;b",
            @"C:\path",
            ":start",
            "end;",
            @"\:\;",
        };

        foreach (var original in testCases)
        {
            var escaped = GalgroupParser.Escape(original);
            var restored = GalgroupParser.Unescape(escaped);
            Assert.That(restored, Is.EqualTo(original), $"Failed for: '{original}'");
        }
    }

    [Test]
    public void Serialize_Should_Store_Physical_Newlines_As_Explicit_Escape()
    {
        var line = GalgroupParser.Serialize("text", new() { ["content"] = "first\r\nsecond" });

        Assert.That(line, Does.Contain(@"first\nsecond"));
        Assert.That(GalgroupParser.Parse(line)[0].Params["content"], Is.EqualTo(@"first\nsecond"));
    }
}
