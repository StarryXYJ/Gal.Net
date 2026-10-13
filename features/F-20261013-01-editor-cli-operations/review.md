# Feature 审核

范围：F-20261013-01-editor-cli-operations，按需求/设计/Phase 与实际提交重新检查；本轮为主 agent 的单独审核步骤，未另行创建 review 子 agent。用户已授权继续实现，因此审核发现的本 feature 问题在同一阶段修正。

结论：pass-with-follow-up。本 feature 纳入范围的代码、协议、实物验收及文档没有剩余交付阻塞；既有主题 async Dispatch 测试审核作为独立后续事项保留。

## 已修正的问题

- P1：EditorSaveCoordinator 把 Group ID 直接拼成输出文件名，原始作者 ID 中的路径片段可越过 preview 目录。改用 ID 的确定性 SHA-256 文件名，Graph 引用同步，原始 ID 保持不变；同时拒绝链接输出目录。新增真实文件路径与链接拒绝验收。
- P2：ProjectSchemaContext 要求模块 ID 唯一，与 Core/ADR-0002 的完整 entry ID 排他契约冲突。允许同名模块贡献不同 entry，SchemaB fixture 同名 flow 模块分别贡献 wait 和 custom.marker；重复完整 entry 负面测试保留。
- P2：项目根的祖先目录可能是链接，原始 reader/契约加载只能拒绝根本身。补齐祖先链检查，并验证经链接祖先访问 DLL/原始读均失败。
- P2：命令帮助把必需选项显示为可选，nullable 无默认构造参数 schema 也与严格反序列化不同。必需选项移出方括号，required/nullability 与真实解析选项同步，nested schema 使用同一设置。共享命令目录默认严格解析未知字段和必需参数，JSON operation ID 精确匹配；CLI execute 参数错误返回已公布的退出码 2。
- P2：project.rename 文字详情遗漏 execute JSON 输入、build/export 没有明确 data schema，export 错误只放在 data.error。补齐描述及统一顶层诊断。
- P2：Editor 打开成功通知之后若最近记录保存失败，内存 recent 列表可能残留失败打开项。失败恢复原列表并释放已初始化 scope，新增 DI 验收。

## 已确认的边界

契约 DLL 是可信项目 C# 代码，不是沙箱。契约与执行按完整 typeId 对接；没有契约不回退内置，工具有 schema 不代表平台已有执行实现。引用分析只有静态 JSON 候选，完整性明确为 false；--force 只确认可能留下引用，不替用户修复自定义代码。作者保存有失败恢复，不承诺跨进程/崩溃整体原子性。回收站无永久删除 fallback，当前 Windows 适配仅支持本地固定磁盘；CLI 不关闭另一进程的 Editor。

## 最终验证

阶段证据已包含真实 CLI 自定义 Composite 编译/导出、两个契约切换、Windows 回收站可见且恢复 5 文件哈希一致、真实 640×360 PNG 视觉与像素验收、截图契约 CLI→Headless 明确失败。最终回归共享 162、Editor 35、Runtime 80、Builtins 64、Architecture 30、Integration 101 全通过；最后资源 ID 输出变更追加查询/校验 33/33，通过真实 GameTestCase validate/find。whitespace 与 IDE0005/import 验证通过，排序修正后的消费者构建通过。日志位于 Temp/F-20261013-01-final-*、metadata-*、sample-*；截图 CLI negative 的 Headless 退出码 1 是预期失败，证明文件已确认所有正向操作成功。

## 后续跟进

本次不新增 UI 操作按钮或契约目录选择布局，Editor 的 API 和 DI 已接通。未知 entry 可通过原始查询保留/检查，类型化编辑明确拒绝以防有损写回。既有 Theme Headless 测试存在无结果 async Dispatch 的审核需求，见 L-20261013-04；本 feature 截图测试已使用显式 Dispatch<bool>，未把既有主题测试的绿色结果当作本次截图实物证据。

审核新增 P1 已修正：资源查询和校验曾按 Id 精确读取，真实 AssetMetaCodec 默认产物为 id，导致实际样例全部 GUID 误报、查找漏报。共享 MetadataIdentity 对齐现行 codec 读取规则，返回真实 JSON pointer；冲突身份字段报错，GUID 按值查询但保留原 ID 拼写。fixture 改用实际 codec 生成，并增加 3 项验收。资源载荷仍明确不在原始校验范围内。
