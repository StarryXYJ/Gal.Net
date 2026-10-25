# P4 原生图形 profile 与简单并行准备

状态：verified。2026-10-25，用户授权两三个并行任务即可，不建立依赖图/调度系统；图形初始化自行 profile 并按收益决定保留或回退。

## 保留：两个数据准备任务

Editor 的 Main 在启动 Avalonia 前创建 EditorStartupPreparation，仅包含两个 Task.Run：读取 EditorSettingsService 设置，以及读取/解析嵌入本地化资源。框架初始化完成时等待任务，向 DI 提交同一设置实例及准备好的资源；CultureService 构造、Current 发布、文化切换、主题应用、导航和控件仍在 UI 线程执行。

参数为空的设计器入口及默认 App 保留同步回退。设置服务不与消费者并发访问；准备任务完成后才发布，主程序退出时观察两个任务（包括平台初始化提前失败路径）。没有通用调度器或长期后台线程。新增 Benchmark 阶段 PrepareSettings、PrepareLocalization、WaitStartupPreparation；Ready 定义不变。

两版均为同源范围的 Release/win-x64/framework-dependent R2R 固定发布，基线 Temp/r2r-experiment/r2r，候选 Temp/p4/parallel；测量关闭 WPR/EventPipe，先各预热一次，再 AB/BA 交错。

| 轮次 | 对数 | 基线 Median | 候选 Median | 基线 P95 | 候选 P95 | 说明 |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| 初轮 | 10 | 1007.99ms | 926.88ms | 2225.96ms | 1041.57ms | 基线有明显异常，不能仅凭此轮宣称约 8% 收益 |
| 独立确认 | 20 | 900.11ms | 862.88ms | 949.41ms | 921.10ms | 16/20 对更快，中位数降低 4.1% |

确认轮配对差值中位数 59.78ms；固定 seed=25、10,000 次 bootstrap 的配对中位数 95% 区间 [10.63, 78.41]ms。两个版本的边际中位数之差不等于配对差值中位数。证据：artifacts/benchmarks/p4-parallel、p4-parallel-confirm（comparison.csv、summary.csv、assessment.json、逐次 trace/日志/SHA256）。

确认轮候选的后台准备阶段中位数：设置 43.95ms、本地化 24.84ms；UI 等待中位数 0.11ms（最大 0.19ms），UI 设置/文化/主题应用中位数 1.62ms。准备工作与图形/XAML 启动重叠；这些区间存在并行，不能相加。收益是本机预热缓存新进程的 Main→ready，不外推系统冷缓存、GPU 首帧或其他机器，也不与 P3 跨轮绝对时间叠加计算。

## 原生诊断

最初 GeneralProfile 录制约 2 GiB，WPR 报告丢失 61,372 个事件，明确拒绝用它推断细分时间。改用 scripts/performance/startup-native.wprp：只采进程/模块、原生 CPU 栈和 GalNet-CPU 阶段，不采上下文切换、文件/磁盘事件。

profile-startup-native.ps1 在 WPR 明确空闲时启动自己的录制和子进程，就绪后正常关闭、停止/合并 trace；无法识别状态或已有录制会拒绝。Editor 与 Sample 都实测成功，退出码 0；分析器对缺 Ready、丢事件直接失败。原生分析按 OS 线程 ID 对齐，Sample 无 RunAvalonia 时使用 Ready 回退。可选下载微软系统符号，第三方驱动多数仍是模块/地址。采样计数不是精确耗时，没有采集线程等待或 GPU 首帧。

两份有效 Editor trace（事件丢失均 0）：

| Capture | RunAvalonia→XAML wall | UI CPU 样本 | 包含 D3D11CreateDevice | 包含 ImageGetCertificateDataEx |
| --- | ---: | ---: | ---: | ---: |
| native-editor-02，原 R2R 基线 | 455.17ms | 383 | 238 | 92 |
| native-editor-03，两个准备任务 | 297.55ms | 280 | 182 | 74 |

上表用于定位，不用于比较性能：采样干扰与机器状态不同。典型链路为 D3D11CreateDevice → DXGI CUMDAdapter/OpenAdapter10_2 → nvldumdx → CryptQueryObject → CryptSIPGetSignedDataMsg → ImageGetCertificateDataEx → FindCertificate / RabinKarpFindPatternInBuffer。说明本机图形启动的一部分 CPU 工作落在 NVIDIA 驱动加载期间的系统证书读取/检查；不能把所有设备创建耗时归因于证书，也不能外推到其他驱动。

没有证据显示应用重复创建设备；暂不修改 Avalonia 生命周期，不关闭系统校验或任意指定显卡。诊断工具独立提交 e54b147；复现方式见 docs/manual/cpu-benchmark.md。Editor 第二次、Sample 与 WGL 验证文件位于 artifacts/benchmarks/native-*；原始大文件被 Git 忽略。

## 拒绝：WGL 后端探测

仅在实验输出中将优先级改为 Wgl、AngleEgl、Software，其他设置/并行准备完全相同；发布到 Temp/p4/wgl 后立即从逐字节快照恢复 Program.cs。与默认 ANGLE 的固定 Temp/p4/parallel 进行 10 对比较：中位数 917.14→905.68ms，但只有 4/10 对更快，配对差值中位数 -6.52ms，bootstrap 区间 [-35.38, 58.02]ms 跨零。P95 1185.76→1024.29ms 也不足以证明稳定收益。

未达到收益门槛，不保留图形后端切换；也未以这项启动探测替代视频/特效/透明窗口和持续渲染兼容性验证。

额外 native-wgl-01 trace（EventsLost=0）确认没有悄悄回退：前置间隔 254 个样本中，184 个包含 opengl32、189 个包含 nvoglv64，D3D11 帧为 0；OpenGL 后端确实进入 NVIDIA OpenGL 驱动，仍未形成稳定启动收益。源码恢复后的最终产物继续使用默认 ANGLE。

## 验证

- Release Editor 相关测试 7/7：新增后台准备不发布全局本地化、不改调用线程文化，保存 locale 正确消费与缺 key 回退；原本四语资源/本地化、两项首屏导航和真实视图主题回归。日志 artifacts-benchmark-p4-tests.log。
- 普通 WindowsReadyToRun/Benchmark=false 发布成功，deps 无 Diagnostics；实际窗口进入空闲后正常关闭，退出码 0。Core/Builtins/DefaultContracts 的发布字节与同模式编译输入一致，默认契约实际加载 45 项。日志 artifacts-benchmark-p4-normal-publish.log、normal-smoke.log；输出 Temp/p4/normal。
- 默认 ANGLE 源码恢复后，最终 Benchmark 发布和真实启动/导出/退出 smoke 成功：Temp/p4/final、artifacts/benchmarks/p4-final-smoke。单次 945.03ms 仅验证链路，不并入收益统计。
- 原生采集脚本在 Editor/Sample 实测成功，分析器在两者均成功且 EventsLost=0；PowerShell 语法、git diff --check 通过。没有终止用户已有 Editor。只运行相关测试，不声称全仓测试通过。

当前保留的产品改动仅为两个 Editor 数据准备任务；Sample 本轮只验证了诊断工具复用，未修改其初始化策略。图形后端仍保持 ANGLE/D3D11；后续若要继续改善，需要针对具体驱动/设备创建证据选择实验。
