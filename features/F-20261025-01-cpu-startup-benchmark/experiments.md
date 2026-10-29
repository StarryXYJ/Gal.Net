# Editor 启动优化实验

状态：verified。用户授权每个有效优化单独提交，无效说明并按需回退。

先将已验证 CPU 系统提交为 49e2713，再细化日志/框架配置/窗口/欢迎页里程碑。保持 Startup.Ready 定义不变。

方法：Release，固定 A/B 程序快照；项目自身程序集复制，未变化的第三方依赖共享文件数据；每个版本先运行一次预热，然后 10 组配对，AB/BA 交替顺序。保存每次 trace、源码状态与程序集 SHA256。每项只改变一个因素；实验期间不并行编译或执行重负载测试。

保留标准：中位数改善约 5% 以上，多数配对改善，P95 无明显回退，并通过相关行为验证。边缘结果增加反向对照；无效代码恢复到本轮实验前版本，仅保留结果记录。

候选：1. 主窗口显示前准备欢迎页；2. 首屏延迟工作区相关主题（同时验证首次打开工作区）；3. Acrylic 窗口效果 A/B。先测后决定，不预设结果。

P2 退出条件：每项有测量与处置结论；有效改动具备独立提交和验证证据；无效实验不残留产品代码。

## 实验 1：显示窗口前准备欢迎页 — verified，保留

基线 A 在 MainWindow.Loaded 后导航；候选 B 在构造完成时导航。Startup.Ready 仍为欢迎页 Loaded 后的 Background 回调。

第一次 10 对：A median 1769.28ms，B 1721.87ms（2.7%），7/10 更快，收益不够明确，因此增加反向顺序验证。第二轮交换标签运行 10 对：实际 A median 2026.29ms，实际 B 1936.94ms（4.4%）。

合并 20 对：A median 1938.59ms，B 1834.01ms，改善 5.39%；14/20 配对更快，配对差值中位数 134.91ms。以 seed=25 对配对差值中位数 bootstrap 20,000 次，描述性 95% 区间约 [5.22, 191.78]ms；这是本机探索性估计，不是通用性能保证。两轮方向一致，符合约 5% 的预设门槛；保留。合并 nearest-rank P95 从 2321.63ms 降至 2124.10ms。

证据目录：artifacts/benchmarks/experiment-early-welcome、experiment-early-welcome-confirm；合并统计 early-welcome-combined.json。构建之间的源码哈希比对确认仅 MainWindow.axaml.cs 发生预期变化。两版程序集快照不同，第三方依赖保持相同。

行为验证：Release 下新增的 2 个首屏/首次显示导航用例与既有真实视图主题用例全部通过（3/3），证据 artifacts-benchmark-startup-behavior-release.log。Debug 尝试因用户运行中的 Editor 锁定输出而失败，保留用户进程后改用 Release 验证。

## 实验 2：首屏不加载 Dock/ColorPicker 主题 — rejected，已回退

这是仅用于测量潜在收益的理想化探测，不是可交付的延迟加载实现；候选只暂时移除两个主题，基线均保留实验 1 的提前导航。

10 对：A median 1907.65ms，B 1845.32ms，改善 3.27%；7/10 更快，配对差值中位数 62.05ms，描述性 bootstrap 95% 区间约 [-80.94, 273.50]ms。XAML 阶段中位数 452.78 → 384.67ms，但总启动收益不足预设门槛，且不确定范围覆盖零；完整延迟实现还需处理首次工作区加载成本及浮动 Dock/ColorPicker 样式生命周期，故不继续引入复杂度。

证据：artifacts/benchmarks/experiment-theme-probe。App.axaml 已逐字节恢复，产品不保留缺失主题或延迟主题的代码；无需为被拒绝方案扩大实现范围。

## 实验 3：关闭 Acrylic 并改为不透明背景 — rejected，已回退

基线保留实验 1 的提前导航和完整主题；候选仅将主窗口 AcrylicBlur/透明背景换为 None/主题不透明背景。

10 对：A median 1473.09ms，B 1477.35ms，候选约慢 0.29%；P95 1588.47 → 1559.65ms。总启动未显示收益，不为此改变窗口外观。证据：artifacts/benchmarks/experiment-acrylic，详细配对统计在 assessment.json。

MainWindow.axaml 已逐字节恢复。三轮比较处于不同缓存/机器负载状态，不能跨轮把绝对毫秒数串成优化趋势；只比较同轮交错 A/B。

## 处置

- 49e2713：已验证的 CPU benchmark 系统独立提交。
- 828bf9f：追加启动里程碑、固定 exe/SHA256 和 AB/BA 比较脚本。
- 4bb1dbc：唯一保留的产品优化，首屏显示前准备欢迎页。
- 主题与 Acrylic 探测均不保留产品改动；最终恢复后的构建/启动再验证结果见 summary.md。
