# P6a：共享 DTO UI 构建

日期：2026-10-17。状态：verified。用户在两子系统设计确认后授权开始，因此将不依赖 P5 交互的共享 UI 基础提前独立实施；场景状态查询仍是后续切片。

## 实现

- Avalonia.Controls 中新增类型化三层注册、默认 SchemaForm 的 DtoType 成员解析、DtoEditorHost 与按稳定项 Id 更新的 DtoPanel。整 DTO 覆盖可以通过 Context.CreateDefaultForm 排除负责字段并复用默认表单；相同 DTO 类型多项互不冲突，移动保留控件和草稿。
- DTO 上下文只借用字段 binding，支持字段快照、局部控件及宿主原子 patch。Context 取消和 lease 归属独立，动态预览/选项继续转发；失败创建、旧提交、重复身份和释放错误有反馈。
- Editor 的项目 scope registry 传入 Dock 的 Group 页；无旧专属字段的生成 DTO 参数区改由共享 Host 分派。多字段 patch 先验证候选再一次替换 Parameters，整次操作一条历史，不反序列化构造完整 DTO。
- Sample 默认注册入口冻结；内置设置主题普通字段接入 Host 并保留原皮肤与设置提交链。Action 和已有 ICustomSetting 独立行为保留。

## 验证过程

最初共享聚焦测试 27/29 通过，两项订阅计数异常揭示 scoped binding 在字段 Context 释放和自身释放时重复退订；增加真实订阅状态后 29/29 通过。该包装器的释放流程也改为继续释放全部资源并保留异常。新测试包括 JSON 字段重命名、直接成员覆盖、整 DTO/默认表单混合、重复类型多项重排、草稿保留、创建失败隔离、注册冻结、原子回调及取消。

Editor 聚焦首轮 13/14 通过：测试误用仅 Dialogue 的 SchemaA 契约验证 layer.move。改为自带 DefaultContracts 新建项目后 14/14 通过，整 DTO 覆盖实际挂载于 Group 页，多字段修改一次 Undo 恢复、Redo 后保存成功，关闭后旧上下文拒绝提交。期间一次未提供必需 contractsDirectory 的测试编译错误已修正，没有改动产品创建协议。

## 门禁与范围

最终隔离解决方案构建成功（76 警告、0 错误）；Presentation 237、Editor 137、Shared 191、Architecture 45，共 610 项通过、0 失败。Presentation 另有 6 项 LibVLC/视频环境用例为 NotExecuted，终端摘要未计入 skipped，按 TRX 的 UnitTestResult 如实单列，不宣称零跳过。whitespace 与 IDE0005 verify 通过，源 SHA-256 随脚本生成后再次核对。证据目录：C:/Users/Starry/.codex/visualizations/2026/10/15/01a13e35-d53c-7270-b753-7b67a4eee31c/dto-ui-validation。聚焦证据位于其相邻 p4b-validation 的 dto-*.log/trx。

当前只支持直接生成 DTO 属性的成员映射；嵌套成员路径、通用对象/数组结构 UI、全部旧资源/表达式字段迁移、跨控件草稿协调、手势历史合并、Editor 状态查询与 timeline/曲线均未完成。无原子宿主回调时拒绝多字段提交，不能宣称任意 Sample DTO 多字段写入已经具备事务能力。

保留并排除并行修改的 PlayerInputTests、GameTestCase、ComplexPrimitiveOptions、Shader lesson 与 F-20261017-01-sample-shader-effect-showcase；不使用 git add .。已验证事实同步 docs/spec/control.md 与 docs/manual/customize-avalonia-sample.md。
