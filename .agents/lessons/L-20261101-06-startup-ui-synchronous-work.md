# 开场并行必须覆盖同步构造和 await 后的 CPU 工作

- 状态：confirmed
- 来源：Sample 开场仍卡顿的原生诊断（独立分析，未实施新优化）
- 范围：App、AvaloniaPlayerHost、LibVlcRuntime、AvaloniaGamepadInput、AvaloniaGameResources、PAK 资源处理

现象：外层 GameResourceScope.OpenAsync 和 Bound 校验已 Task.Run，但 Logo 动画仍冻结。单元测试通过不能证明 UI 响应性。

发现：相同 dev.2 契约/内容/native 依赖的 Release 自包含 Player 诊断，用 10ms UI 心跳和同步阶段标记测量。完整一次：创建选项约 224ms，new LibVLC 523ms，SDL_Init 144ms，11 张 sprite 预载 529ms；Avalonia 和 Skia 各解码 11 次，共约 177ms，均在线程 2。该次 UI 心跳间隔 1347ms/687ms；另三次 8 秒观测在 LibVLC 构造未返回时终止。不能推断 native 内部究竟卡在插件、设备还是杀毒软件。

根因：AvaloniaPlayerHost.CreateAsync 正常构造路径无实际 await，解析依赖会同步构造 LibVlcRuntime；BindWindow 同步创建 SDL source。资源的异步读取完成后，整包校验/解析、资源解压校验和图像解码仍可沿 UI SynchronizationContext 执行。Task.WhenAll 不自动把同步工作搬到线程池。ResourceOverlay 的逐资源读取会重新打开 PakFileProvider，后者整包读取和校验；不能把整段预载耗时都称作 PNG 解码。

正确做法：先测原生 UI 心跳和具体阶段，分别设计纯后台准备与 UI 绑定；native 初始化显式可等待且有取消/所有权，不能把整个宿主（含控件）Task.Run。SDL 现有契约约束同一 UI 线程初始化、轮询、释放，必须单独分析线程/初始化时机。包复用不能跳过完整性与并发生命周期约束。

证据：output/startup-analysis/analysis.md、results.json、traces.log（临时、不提交）。10 个计时源码文件已按字节恢复；保留原有语言修复，未修改 dev.2 包。本记录只确认定位，不宣称已消除卡顿。

后续修正已验证：宿主选项及 LibVLC 构造后台化；会话使用单一后台准备流程与 UI 结果发布，纹理按顺序取得。取消等待 native 工厂实际结束再释放其 scope；UI 图构建分阶段让出调度。SDL 仍在 UI 上，于开场/会话结束后启用。57 项回归及三次 Release 自包含原生就绪通过；外部 WM_NULL 采样最长连续超时约 128–139ms，与上面的内部心跳不属同口径，不换算改善比例，也不宣称所有帧无卡顿。正式边界见 docs/spec/presentation/game-host.md 和 docs/manual/extensions/customize-sample.md。