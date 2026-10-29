# P4 — 独立资源补丁与覆盖

状态：`verified`，2026-10-28；前置 P3 提交 `f97bca72`。

## 实现

- ResourceOverrideDefinition 保存稳定 ID、名称、priority、A→B、动态补根和完整性声明。Builder 完整收集替换闭包；Exporter 独立截取 Assets 私有阶段、编译图集、验证并发布一个 PAK，无需 galpak/剧情编译。
- override 绑定项目、buildId 为空；story 绑定基础构建。映射和资源稳定排序，同语义输入 hash 不变。ResourcePatches 装载准备期校验全部 payload、用途/项目与闭包。
- 固定 priority 降序、全文件 SHA-256 ordinal 降序，同 hash 去重。ResourceOverlayProvider 明确绑定来源，保留原 ID/路径，使用替换 metadata/payload；查询、Acquire、路径加载、媒体实体化共用视图。
- 一次请求只映射一次；夹带 B 不改变本体对 B 的独立查询。递归依赖同 ID 不同内容时拒绝，作者须使用新 ID，避免隐式替换。图集保留原维度键和变体键。
- 原资源不在当前安装时映射不激活，不能替缺席剧情补正文。会话包集合冻结，旧 handle 保持值，新安装会话采用新包。

## 证据

| 检查 | 结果 |
| --- | --- |
| 完整 Release 解决方案构建 | 通过，261 warning / 0 error |
| Assets 全套 | 102/102，含 sprite/audio/video 查询、ID、路径、实体化，图集闭包、单次映射、优先级/hash/重复文件、类型/缺目标/依赖冲突、稳定 hash |
| Editor.Shared 全套 | 481/481，含先于基础导出、改名/重复包、缺席剧情、冻结/重开会话、坏 payload 准备期拒绝 |
| Integration 全套 | 130/130；Explicit 原生窗口用例未执行 |
| ReflectionOff | P4_OVERRIDE_REFLECTION_OFF_OK，P1/P2 smoke 保持通过 |

矩阵 GUI 与作者定义的持久化/历史属于 P5。无热装卸、语言条件或 Gallery 补丁行为。
