# 保留 TraceEvent 迭代结果必须 Clone

状态：confirmed

现象：原生 ETL 内已收到 17 个 GalNet-CPU 标记，直接把 log.Events.Where(...).ToArray() 的事件保存后，却无法找到 Editor.RunAvalonia，或读取到后续事件的 payload。

根因：TraceEvent 迭代器复用事件对象。延后读取保留下来的引用，不能保证它仍对应当次事件。

发现方式：F-20261025-01 的 native-editor-02 无丢事件 trace；改成逐项 Clone 后，同一 ETL 成功对齐阶段并获取 383 个目标区间主线程 CPU 样本，重复分析及 Editor/Sample 实测通过。

正确做法：立即在 foreach 内消费并提取不可变字段；确实需要保存 TraceEvent（含 CallStack/EventIndex）供后续筛选时，先 Select(item => item.Clone())。当前 scripts/analyze-startup-native.cs 使用后一种方式。

适用范围：TraceEvent/TraceLog 迭代器事件的延后处理；scripts/analyze-startup-trace.cs 原有逐项消费方式不需要改成全量保留。

禁止做法：把 ToArray/ToList 当作事件快照；因延后读取失败就判定 provider 没发标记，或凭空补齐时间轴。

来源：features/F-20261025-01-cpu-startup-benchmark/parallel-native.md。
