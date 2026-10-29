# P3 — 本体与剧情包

状态：`verified`，2026-10-28；前置 P2 提交 `a7f08962`。

## 已实现

- 本体闭包 B 与各剧情包独立的 C(P)−B；稳定排序、软阈值切片、超大项独占、未引用资源裁剪和布局报告。
- primitive schema、Timeline 事件、shader texture、粒子纹理递归依赖。未知结构/custom.invoke 要求额外根和完整性声明。基础 Gallery 与默认视觉绑定作为根。
- galnet.packaging：maxPakBytes（缺省 64 MiB）、includeResources、declaredCompleteResources。后者可填 Group 节点 ID 或资源 ID，只声明覆盖，不自动补根。
- 同一私有阶段生成基础及剧情 PAK，逐片落盘。content.json v2 保存项目/构建/分片校验和。ZIP 外部 StoryPatches/buildId 目录整体发布，目录导出整体发布；取消保留已有 galpak。
- 安装选择冻结并校验用途、身份、哈希、完整性、重复资源一致性和独立依赖。provider 后续打开复验所选字节；分片改名仍识别，完整缺席沿正常出口跳过，部分安装/损坏拒绝。
- GameSnapshot v5 保存构建和准备时已安装的全部剧情包；不同构建或缺包拒绝恢复。未播放的已安装包也属于恢复要求，这是首版保守策略。

## 验证

| 检查 | 结果 |
| --- | --- |
| 完整 Release 解决方案构建 | 通过，261 warning / 0 error；后续受影响工程复编通过 |
| Assets 全套 | 94/94，含独立扣除、顺序、阈值、未知依赖、包边界 |
| Editor.Shared 全套最终快照 | 480/480，含四种安装组合、改名、损坏/部分安装、缺包读档、ZIP 外部发布、重复导出 hash、取消及嵌套依赖 |
| Integration 全套最终快照 | 130/130；Explicit 原生窗口用例未执行 |
| Runtime 全套 | 123/123 |
| ReflectionOff | P1/P2 smoke 均通过 |
| git diff --check | 通过 |

## 边界与设计修订

settings、I18n 和人物文件仍完整留在基础内容，首版不按剧情拆分文本贡献；正文文本校验只检查已加载 Group。Gallery 保持旧基础聚合。此为明确修订，不声称完成最初的文本分片计划。

64 MiB 为软目标，不是内存上限。Archive 仍以内存打开单个 PAK；代表性大资源峰值观测留给 P6。源目录仍完整校验，未引用的坏资源可能阻止导出。override 与矩阵分别待 P4/P5。
