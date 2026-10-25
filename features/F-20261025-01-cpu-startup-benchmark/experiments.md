# Editor 启动优化实验

状态：in-progress。用户授权每个有效优化单独提交，无效说明并按需回退。

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
