# 单 Graph 内容分包与独立资源覆盖

状态：`completed`，P1–P6 全部 verified，2026-10-28。各阶段独立提交。

后续面板与默认阈值调整见 [调整记录](follow-up-panel-20261028.md)：图标化、配置即时持久化、纯导出，默认阈值提高为 128 MiB。

## 实现结果

项目保持唯一 Graph。Graph 与编译 Group 作为普通 PAK 资源读取，固定 Graph 资源 ID 由清单提供，Sample 无需配置选图。章节按钮通过新游戏初始存档变量选择入口分支；默认值初始化后应用参数，新 Run 与读档各遵守既有变量生命周期。

metadata 的 `IAssetDependencies` 提供直接依赖和完整性，叶类型显式注册。编译期生成图集、Graph、Group 的有效依赖，不改作者 sidecar；自定义动态引用通过显式补根和完整性确认参与构建。只有依赖闭包进入 PAK，条件分支及图集的全部静态依赖都会纳入。

本体先行，剧情包各自扣除本体已有项；A/B 共用但本体没有的资源分别携带。包内去重，以未压缩大小软切片，首版默认 32 MiB（后续按用户要求改为 128 MiB）。普通 galpak 导出同时生成外部剧情包。未安装整包时沿正常出口跳过，部分安装、坏包、错项目/构建和依赖缺失拒绝。

资源覆盖独立导出。作者面板聚合所有 Group 引用，支持间接资源、原文件定位、补丁列与资源选择、优先级、撤销/重做、保存重开。A→B 保存明确来源并携带 B 闭包，按 priority、最终 PAK SHA-256 降序裁决，禁止隐式夹带覆盖和 Graph/Group 覆盖。寻址、解码和媒体文件租约共用冻结视图；图集替换后旧档重新解析选择意图。

## 模块与协议

| 模块 | 变化 |
| --- | --- |
| Core / Contracts | 依赖接口、类型声明、包控制 DTO、配置 section、资源字段标注、可选图集覆盖状态 |
| Assets | GPAK v3、闭包收集/规划、Graph/Group 编译适配、独立覆盖 Builder、统一覆盖 provider |
| Runtime / GameHost | 单图资源装载、缺席剧情导航、save v5 兼容校验、新游戏初始变量、内部调试快照身份 |
| Storage / Editor.Shared | content.json v2、基础/剧情整组导出与验证、布局报告、覆盖快照导出、资源引用索引 |
| Editor / Sample | Group 包名和资源补丁矩阵、四种 UI 语言、章节命令示例与既有首页 DI 扩展衔接 |

旧 GPAK/安装内容需重新导出；旧版存档不自动迁移。没有增加多 Graph、选图、按图变量隔离或第二套首页框架。

## 验证

分阶段证据见 [P1](phase-1.md)、[P2](phase-2.md)、[P3](phase-3.md)、[P4](phase-4.md)、[P5](phase-5.md)、[P6](phase-6.md)。最终发布保护、锁定还原、两项格式和完整 Release 构建通过；完整运行与修复后定向复验组成 15 工程、2,064 项默认用例通过的证据，另有 Debug 快照 2/2、原生 Sample Explicit 1/1、反射关闭和大资源观测。详见 [机器可读汇总](verification/validation-results.json)。构建仍有 386 个 warning，未宣称零告警。

首次完整 CI 暴露测试越层依赖和 FieldDescriptor 旧签名丢失，均已修复并复验；原始失败记录保留。跨层安装夹具移至 IntegrationTests，未放宽架构规则。公开 FieldDescriptor 保留旧构造函数，已编译契约可继续装载。buildId 纳入实际分片布局，避免默认切片策略变化复用旧剧情目录。

P5 实际挂载控件验证选择器、定位、矩阵多列滚动/编辑、撤销、保存重开和独立导出。P6 显式 Debug 原生测试从打包内容启动、seek/step、五组截图并正常退出；合成大资源程序验证未使用项排除和独立剧情包归属。性能数据是本机观测，不代表内存硬上限。

## 实施偏差与限制

- 按用户后续确认移除最初讨论的多 Graph，最终始终唯一 Graph。
- 文本和人物文件完整放在本体，未实施按剧情文本贡献合并；缺席剧情不参与正文引用检查。既有 External 翻译来源继续保留。
- 存档保守记录准备时所有已安装剧情包；移除其中任何一个都会阻止恢复，即使尚未播放。没有执行游标迁移或二进制差分更新。
- 默认软阈值从 P3 的 64 MiB 经 P6 观测调到 32 MiB；Archive 仍整片缓冲，不保证低内存设备。流式读写可另开后续工作。
- 资源补丁不按语言匹配，不热装卸；新建安装会话采用包变化，已持有 handle 不原地改变。依赖同 ID 不同内容须分配新 ID。
- Gallery 补丁按约定延期，既有 Gallery 资源仍属于基础根。UI/动态代码资源需显式根，不以运行时是否走到某分支裁剪。

## 当前文档

- [作者操作](../../docs/manual/editor/content-packaging.md)
- [资源契约](../../docs/spec/content/assets.md)
- [发布格式](../../docs/spec/content/file-formats.md)
- [运行时与存档](../../docs/spec/runtime/execution.md)
- [文本边界](../../docs/spec/content/localization.md)
- [Sample 章节与首页扩展](../../docs/manual/extensions/customize-sample.md)

每阶段独立 Git 提交：P1 `a3ae72bd`、P2 `a7f08962`、P3 `f97bca72`、P4 `5b2c1a74`、P5 `043638d9`；P6 为本记录所在的最终收尾提交。
