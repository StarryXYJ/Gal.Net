# P6 — 集成与文档收尾

状态：`verified`，2026-10-28；前置 P5 提交 `043638d9`。实现、完整检查及发现问题的定向复验完成，机器可读汇总见 [validation-results.json](verification/validation-results.json)。

## 集成修正

- 图集覆盖后的旧存档保留逻辑图集 ID、选择请求和受保护维度，在候选恢复状态按当前图集重算派生图片。覆盖来源通过 `IAssetOverrideFile` 传入会话图集目录，存档附加可选 `UsesResourceOverride`，支持安装和移除覆盖的双向恢复；普通未覆盖图集保持严格一致性检查，不修改输入存档。
- 打包内容的内部调试 replay 快照补齐构建和剧情包身份；Sample 的进度/Group 起点调试存档也保留身份。外部缺身份或错构建存档仍拒绝，没有放宽恢复门禁。
- 资源矩阵索引使用实际文件 ID，配置反序列化后继续使用大小写无关的映射键；反射关闭 smoke 覆盖作者配置和新增 Layer 状态。
- 按仓库格式规则整理本 feature 修改的 C# 文件，正式文档同步唯一 Graph、GPAK v3、content v2、save v5、剧情包和资源覆盖契约。
- 完整架构检查发现 Editor.Shared.Tests 的安装夹具直接引用 Storage，违反测试依赖规则。将 ExportPreparationTests 和 StoryPackageExportTests 移到现有 IntegrationTests，移除该直接依赖，不放宽架构白名单；更新锁文件并重验受影响工程。
- 构建指纹纳入格式标记和物理分片布局。仅更改引擎默认切片值时，源 settings 与资源字节可能完全不变，但剧情包分片集合会变化，必须得到不同 buildId，避免复用同一不可变剧情目录。
- SchemaGenerator 的既有旧契约二进制兼容测试发现：给 FieldDescriptor 构造函数增加可选 resourceTypeId 会改变 CLR 签名。保留旧八参数构造函数，转发到新实现，不要求未使用新能力的已编译契约重新编译。

## 原生验证

Debug 构建下显式执行 `Packaged_variants_render_in_native_sample_and_close_cleanly`：1/1 通过。测试导出真实素材后删除作者目录，从安装包启动真实 Avalonia Sample，完成调试 seek/step、五幅不同渲染截图和正常关闭。进程峰值 working set 480,911,360 bytes，关闭时日志确认 Run、scope 和根服务释放。

最终源码重新构建 Debug（96 warning / 0 error）并单独选择该 Explicit 用例再次通过 1/1，峰值 457,216,000 bytes。调试快照工厂另通过 2/2。混合过滤普通与 Explicit 测试不会保证执行 Explicit，最终分别选择并核对实际执行数。

首次尝试使用默认 Release 构建，因调试桥默认关闭而等待监听超时；改用具有调试桥的 Debug 构建。随后实际暴露的内部快照身份丢失已修复并通过上述用例，未用禁用内容校验绕过。

图集资源覆盖的独立 PAK→catalog→引擎读档→呈现集成测试已通过 1/1，验证 A 衣服旧档装补丁后显示 B，B 补丁档移除补丁后显示 A，并核对输入存档未改变。

## 大资源观测与默认值

可复现程序见 [memory-probe](memory-probe/Program.cs)。152 MiB 合成不可压缩资源：本体 80 MiB、A/B 共用 20 MiB、A 独用 20 MiB、未用 32 MiB；资源模拟 PAK I/O，不代表图片解码/渲染压测。程序导出、安装并逐包核对未用资源排除以及 A/B 各带公共资源。

| 软阈值 | 最大实际 PAK | 本体/剧情分片 | 进程峰值 working set | 导出+安装耗时 |
| --- | --- | --- | --- | --- |
| 64 MiB | 62,915,346 bytes | 2 / 2 | 806,952,960 bytes | 2,412 ms |
| 32 MiB | 20,973,449 bytes | 4 / 3 | 698,605,568 bytes | 2,511 ms |

原始比较报告保存在 result-64MiB.json、result-32MiB.json。最终默认由 P3 的 64 MiB 调整为 **32 MiB**，其余切片规则不变。以上是一次本地观测，峰值约降低 13%，不能推导所有项目相同比例或严格内存上限。

Archive 当前仍以整片缓冲读写，压缩/输出会产生临时副本；即使分片也会有较高峰值。此阶段没有扩展为流式归档重写，默认值只是降低常见单片开销。超大单资源仍独占且可能超过目标。最终不传阈值参数的重跑结果见 [result.json](memory-probe/result.json)：默认 33,554,432 bytes，归属检查通过，峰值 656,039,936 bytes，耗时 2,993 ms；运行间存在波动。

## 完整检查

执行仓库统一入口 `scripts/ci/Invoke-CI.ps1 -Stage All -Configuration Release`：完成发布保护、锁定还原、格式、完整构建及全部 **15** 个测试工程。首次运行暴露上述架构依赖和构造函数二进制兼容问题，保留原始失败；修复后完成最终 Format 入口、完整 Release 复编和受影响工程定向复验，不把首次失败输出改写为成功。

| 最终证据 | 结果 |
| --- | --- |
| 发布保护 / 精确 SDK / Release locked restore / whitespace / IDE0005 | 通过；最终 Format 报告 passed=true |
| 完整 Release 解决方案构建 | 通过，386 warning / 0 error，3 分 40 秒 |
| Architecture | 52/52 复验通过，白名单未放宽 |
| SchemaGenerator / Core | 20/20、139/139 复验通过，含旧 consumer 装载当前 Core |
| Editor.Shared / Integration | 迁移后 476/476、138/138 全套通过 |
| Editor | 完整运行 345/345，最终作者操作 3/3 复验通过 |
| 其他默认工程 | Assets 103、Debugging 10、Diagnostics 1、GameHost 8、Media.Audio 26、Presentation 276、Primitives 322、Runtime 124、Storage 24，均通过 |
| 默认工程组合证据 | 15 工程，2,064 项通过；逐工程来源保存在 validation-results.json |
| Debug 原生 / 精简快照 | 分别 1/1、2/2，最终源码复编后执行 |
| ReflectionOff | 四个阶段标记均出现，reflection JSON=false |
| 大资源 / 本地文档链接 / git diff --check | 通过 |

默认 TRX 仍包含 8 项未执行的 Explicit/环境用例，其中打包原生 Sample 已在 Debug 单独执行；未声称其余媒体环境/平台用例已执行。最终复验只覆盖新增修正和迁移影响，没有为未改动的用例重复整套运行。CI 原始日志与 TRX 留在 out/ci/content-packaging-p6*；提交内保留简洁汇总及内存报告。

正式文档已同步资源、发布格式、运行时、文本边界、Graph 作者入口和操作手册。文本/人物完整在本体、保守剧情包存档、整片内存、Gallery 延期等边界与设计修订一致。没有剩余阻止本 feature 交付的已知问题。
