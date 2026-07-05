using GalNet.Core.Settings;
using GalNet.Runtime.View;

namespace GalNet.Headless;

/// <summary>
/// 可交互的控制台 IGameView 实现。
/// 继承 NullGameView，重写交互方法：
/// - 打字机效果（支持 \d 延迟占位符），速度由 GameSettings.TextSpeed 控制
/// - 对话按 Enter 继续
/// - 选项输入 1, 2, 3... 选择
/// </summary>
public sealed class HeadlessGameView : NullGameView
{
    private readonly GameSettings _settings;
    private TaskCompletionSource? _typewriterDone;

    public HeadlessGameView(bool verbose = true, GameSettings? settings = null) : base(verbose)
    {
        _settings = settings ?? new GameSettings();
        _typewriterDone = new TaskCompletionSource();
        _typewriterDone.TrySetResult(); // 初始已完成
    }

    /// <summary>
    /// 逐字输出文本，支持 \d{ms} 延迟占位符。
    /// 完成后通知 WaitForClickAsync 可以显示提示了。
    /// </summary>
    public override async Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        var old = Interlocked.Exchange(ref _typewriterDone, tcs);

        var prefix = string.IsNullOrEmpty(speaker) ? "" : $"{speaker}: ";

        try
        {
            // 同步输出，先完成所有打印再通知
            Console.Write(prefix);
            await TypewriterPrint(text, ct);
            Console.WriteLine();
            tcs.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            tcs.TrySetResult();
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
            throw;
        }
        finally
        {
            old?.TrySetResult(); // 清理旧信号
        }
    }

    /// <summary>
    /// 等待打字机效果完成，然后等待用户按 Enter 继续。
    /// </summary>
    public override async Task WaitForClickAsync(CancellationToken ct)
    {
        // 等待打字机效果完成
        await (_typewriterDone?.Task ?? Task.CompletedTask);

        Console.Write("\n  >> ");
        try
        {
            await Task.Run(() => Console.ReadLine(), ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// 显示选项（1, 2, 3...），等待用户输入数字选择。
    /// </summary>
    public override async Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct)
    {
        Console.WriteLine();
        for (var i = 0; i < options.Length; i++)
            Console.WriteLine($"  [{i + 1}] {options[i]}");

        while (true)
        {
            Console.Write($"\n  请选择 (1-{options.Length}): ");

            try
            {
                var input = await Task.Run(() => Console.ReadLine(), ct);
                if (input == null) return 0;

                input = input.Trim();
                if (int.TryParse(input, out var selected) && selected >= 1 && selected <= options.Length)
                    return selected - 1;

                Console.WriteLine($"  无效输入，请输入 1-{options.Length} 之间的数字。");
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// 跳过打字机效果（静默，无日志）。
    /// </summary>
    public override void SkipTypewriter(string widgetInstanceId) { }

    // ── 打字机实现 ──

    /// <summary>字符显示间隔（毫秒）</summary>
    private int BaseDelayMs => _settings.TextSpeed > 0 ? (int)(1000f / _settings.TextSpeed) : 0;

    /// <summary>
    /// 逐字输出，解析 \d{ms} 和 \n 指令。
    /// </summary>
    private async Task TypewriterPrint(string text, CancellationToken ct)
    {
        var i = 0;
        var instant = BaseDelayMs <= 0;

        while (i < text.Length)
        {
            ct.ThrowIfCancellationRequested();

            if (text[i] == '\\' && i + 1 < text.Length)
            {
                if (text[i + 1] == 'd')
                {
                    // \d{ms} 延迟指令
                    i += 2;
                    if (i < text.Length && text[i] == '-')
                    {
                        // \d- ：剩余文本立即显示
                        instant = true;
                        i++;
                    }
                    else
                    {
                        var numStr = "";
                        while (i < text.Length && char.IsDigit(text[i]))
                        {
                            numStr += text[i];
                            i++;
                        }
                        if (numStr.Length > 0 && int.TryParse(numStr, out var delayMs))
                        {
                            if (delayMs < 0)
                                instant = true;
                            else
                                await Task.Delay(delayMs, ct);
                        }
                    }
                    continue;
                }

                if (text[i + 1] == 'n')
                {
                    Console.WriteLine();
                    i += 2;
                    continue;
                }
            }

            Console.Write(text[i]);
            if (!instant && BaseDelayMs > 0)
                await Task.Delay(BaseDelayMs, ct);
            i++;
        }
    }
}
