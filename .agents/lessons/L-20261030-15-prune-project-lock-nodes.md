# 删除工程后核对锁图中的旧 Project 节点

- 状态：confirmed
- 来源：F-20261029-05-cross-platform-applications / P6c
- 范围：本仓库 SDK 10.0.401 的 NuGet 项目锁图迁移

现象：删除 Sample.Player 工程和所有 ProjectReference、改为直接引用其原有底层依赖后，普通 restore 和 locked restore 都成功，真实构建也成功，但部分源 packages.lock.json 仍保留 `galnet.sample.player` Project 节点。

发现：先检查源码/解决方案引用，再结构化读取所有源锁图，发现旧节点仍存在；单独 `dotnet restore --force-evaluate` 对 CrossPlatform 共享工程重新计算后，旧节点消失。对 Desktop/R2R 各既有参数图逐项重新计算后再 locked 校验，验证清理有效。

正确做法：涉及删除/扁平化项目依赖时，同时检查源锁图的 Project 节点；必要时用正确 profile/RID 的 `--force-evaluate` 重新计算，再用 `--locked-mode` 验证。另核对既有包 resolved/contentHash、目标框架和 RID 键未意外变化。

禁止做法：仅依据 locked restore 成功宣称旧工程完全删除，或手工删节点却不验证真实还原。不要把这次行为泛化为所有 NuGet 版本；普通版本升级与保留项目节点的优化是不同问题。

证据：`out/cross-platform-p6c/force-evaluate-probe.log`、`force-final-*`、`locked-final-*`。Architecture 的 SampleCompositionRoots 检查已增加源锁图 JSON 的旧节点断言。
