# 对白历史、语音重播与已读正文配色

状态：verified。

## 实现结果

- Sample 游戏设置提供“已读文本颜色”：默认关闭，启用显示颜色选择入口，默认 #A8B8C8，保存开关与颜色。ADV/NVL 捕获进入前已读状态，显式富文本颜色及粗体、斜体、换行保持原样，设置变化不重启打字。
- 三主题工具栏增加历史按钮，默认滚轮向上，支持原有输入设置。共享面板按顺序显示姓名、即时富文本和可选语音按钮，2000 行虚拟化验证，每次打开定位末尾，Escape/返回关闭，输入不穿透。
- 历史从当前路线的新游戏开始，重复访问分别记录，完整显示后追加。实际存档附加完整历史前缀，日常状态只保存小游标；读旧档恢复对应前缀，当前句重显示不重复。已有当前格式旧档缺少历史模块时从空历史继续，不制造此前未记录的文本。
- 复用同 Group、同 rawId 的紧邻编译身份关联 audio.play 和文本；保存语音资源/音轨/增益，本次 ticket 控制原语音结束后出现的对白重播按钮。
- 独立临时 voice 重播，使用现有后端与轨道混音；重复播放替换旧播放。有效继续（含补全文字）、选项、自动/快速/跳过、关闭历史、读档与退出运行停止，迟到事件不影响新播放。
- 原有文字/语音都结束后再计算自动间隔的行为保留。

## 模块与关键决定

Core 只新增 opaque Snapshot Attachments 和可选 dispatcher 观察协议。Runtime 保护 reading preamble 的稳定快照边界，并在执行门内同步提交已完成的宿主观察。GameHost 保存历史 journal 与临时重播生命周期；Avalonia.GameHost 观察内置执行并绑定页面；GameView/Controls/Themes 提供页面、输入、富文本默认色和设置控件。新增 ColorPicker NuGet 依赖及受影响的普通/玩家/ReadyToRun/调试锁文件。

P1/P2 合并为可编译的垂直切片后集成验证。复查发现的半句快照、历史异步完成滞后和旧运行 UI 回调问题已修正，见 [design.md](design.md)、[review.md](review.md) 与 [lesson](../../.agents/lessons/L-20261029-09-reading-preamble-checkpoints.md)。

## 验证

| 范围 | 通过 |
|---|---:|
| Core 全量 | 139 |
| Runtime 全量 | 124 |
| Presentation 全量 | 296 |
| Storage.FileSystem 全量 | 26 |
| Architecture 全量 | 52 |
| 历史/重播/设置/自动播放/存档/宿主生命周期等定向集成 | 51 |
| 合计 | 688 |

Debug Sample、Release 集成项目构建通过；Release locked restore 通过；C# whitespace、IDE0005 清理及最终格式检查通过。截图与日志在本地 `out/dialogue-history/`，集成结果为 `results/dialogue-history.trx`，三主题历史截图在 `screenshots/`。

环境/测试修正记录：Avalonia 遥测写用户目录受限，验证使用 UsedAvaloniaProducts 空值；dotnet format 的命名管道需要沙箱外执行，只处理本功能列出的文件。早期 Debug 构建用了不匹配的恢复图，按相同 Configuration/EnableGameDebugging 恢复后 Debug 构建通过。Storage 并发替换测试首次出现 Windows 临时访问失败，单项及全量重试均通过，未修改无关存储实现。两项输入用例原先假定中键默认推进、右键没有冲突，现改为夹具显式配置对应手势；AOT 序列化用例明确验证只补入新增历史动作，其余线格式保持。

## 当前限制

- 不从旧存档重新执行剧情补全过去未记录的文本。
- 手写不同源音频不自动关联；选择界面保留原有快捷键约束，可用工具栏进入历史。
- 本次未执行全仓发行打包、原生视频显式用例或人工扬声器试听；共享后端的原生能力不由 Headless 验证代替。

规范见 [control](../../docs/spec/control.md)、[runtime](../../docs/spec/runtime.md)，接入说明见 [定制 Sample](../../docs/manual/customize-avalonia-sample.md#对白历史与重播)。
