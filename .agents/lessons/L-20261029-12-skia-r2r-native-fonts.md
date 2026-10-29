# 原生字体验收不能由 R2R 元数据检查替代

状态：confirmed
来源 Feature：F-20261029-05-cross-platform-applications

## 现象与定位

Windows x64、.NET 10.0.12 / SDK 10.0.401、SkiaSharp 3.119.4、Avalonia 12.0.5 的真实自包含 Sample 在窗口初始化时出现 0xC0000005，栈位于 SkiaSharp.SKStream.Read / SkiaTypeface.TryGetStream / TryCreateSyntheticGlyphTypeface。原始纯运行包与合包产物均复现，不能归因于合包；也不能把无 SDK PATH 本身认作根因。

禁用 R2R 执行的对照通过。随后只用 NuGet 中同版本 net10.0 SkiaSharp 原始 IL 替换诊断副本，其他程序集显式启用 R2R，连续三次原生启动、会话初始化、正常退出通过。原始 R2R 副本随后再测仍失败；原始副本偶尔也能通过，单次成功不能证明解决。此证据支持当前版本组合下的选择性缓解，不证明底层 CLR/Skia 的具体内存错误机制。

## 做法与边界

win-x64 R2R 发布只排除 SkiaSharp.dll 的交叉生成，应用和其他依赖继续 R2R。生成清单后重新解包、核对 SkiaSharp 为 IL/主程序仍为 R2R，再执行原生启动与正常退出；不把禁用所有 R2R 的诊断命令当作正式产物通过。不由此推断其他系统或后续版本也有相同问题。

日志索引：out/cross-platform-p2-results/p2-native-original-1.trx、p2-native-no-r2r-diagnostic.trx、p2-native-skia-il-{1,2,3}.trx。最终正常发布、合包且未替换 DLL 的产物见 out/cross-platform-p2-smoke/game final.zip，连续三次通过 p2-native-final-{1,2,3}.trx；主程序仍为 R2R，SkiaSharp 为 IL。
