# 高频参数比较避免接口枚举装箱

状态：confirmed。
来源：features/F-20261030-01-project-quality/phase-plan.md，P4a/P4b。

现象：ProjectContentBaseline 已不序列化 live 内容，但 1k/10k Entry 的 dirty 查询仍分别分配约 63/626 KiB。500 Entry 连续查询 20 次的回归实测分配 654,080 B。

原因：Entry.Parameters 为 Dictionary，但经通用 Map 的 IReadOnlyDictionary 参数 foreach，枚举器装箱；每个 Entry 一次，形成约 64 B/Entry 的分配。不能仅凭没有 Clone/Serialize 就认定内容比较低分配。

修正：Entry 身份/参数数量先比较，再直接遍历具体参数字典并执行原来的 TryGetValue + JsonElement.DeepEquals。保留逐字段比较，不增加基于历史事件的缓存或第二套 dirty 状态。通用 Map 继续服务低频外层映射，不复制泛型框架。

验证：将现有 500 Entry × 20 次 dirty 查询测试的分配上限由 2 MB 收紧至 30 KB，旧实现实测失败，修正后 Editor.Shared 全套 515 项通过；1k/10k 基准夹具验证直接晚修改/还原与快照隔离。前后基准与日志索引见 feature/benchmarks/editor-baseline-notes.md。

适用：集合实现已知且按大量条目重复调用的比较热点。先测量再局部优化；不要全仓替换接口，也不要通过漏掉未知 JSON 字段或仅查历史游标换取性能。
