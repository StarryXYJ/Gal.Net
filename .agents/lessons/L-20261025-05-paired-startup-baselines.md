# 启动优化使用固定二进制交错对照

状态：confirmed
来源：features/F-20261025-01-cpu-startup-benchmark/experiments.md

证据：同一个提前准备欢迎页的 exe 快照，在主题探测中作为基线 median 1907.65ms，在后续 Acrylic 探测中作为基线 median 1473.09ms；这段变化没有基线源码优化。前后顺序跑两批、或拿最后一次 smoke 与早期基线相比，会把缓存和机器负载漂移归功于代码。
做法：固定可运行快照和程序集 SHA256，两版各预热，再 AB/BA 配对交错，使用同一个 ready 定义；一次改变一个因素，边缘结果补反向验证，提前规定保留门槛。编译和重负载测试不与测量并行。
验证：提前准备欢迎页 20 对 median 1938.59→1834.01ms，14 对更快，行为检查通过后提交。主题与 Acrylic 探测未达门槛，逐字节恢复原 XAML。原始 trace 和 metadata 在 artifacts/benchmarks，结论保存在 feature 文档。
禁止：跨轮拼接绝对时间当作优化趋势；只凭单次最快值提交；更改 ready 定义后宣称启动变快。
