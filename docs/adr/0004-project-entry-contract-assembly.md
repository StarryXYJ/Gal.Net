# ADR-0004：项目 Entry 契约程序集与平台执行独立组合

状态：Accepted（2026-10-13）

## 上下文

ADR-0002 已将 IEntryModule/schema/composite 与 IPrimitiveModule 执行注册分离。当前内置契约和执行仍位于同一程序集，CLI/Editor 固定使用全部内置 schema，无法表达每个游戏项目的实际支持集合。

开发者需要一个独立契约项目，游戏工程直接引用它；构建产物也能独立部署给工具。AI 可以直接编辑内容文件，工具通过同一契约校验/编译。单平台执行模块允许直接操作视图。

## 决定

- 内置 schema、参数类型、纯校验及 Composite 展开抽到仅依赖 Core 的新契约项目，提供组件选择和全量组合入口；执行模块引用契约。
- 每个内容项目在固定目录部署固定名称契约 DLL，以确定的静态只读模块集合声明项目支持范围。CLI/Editor 的外层加载器构造项目 catalog，不扫描并加入未选择能力。
- 游戏通过普通项目引用调用同一契约集合，另行注册执行与平台实现；工具动态加载不要求游戏发布也采用动态加载。
- Editor catalog 及其消费者归项目生命周期；框架 Core 只认识 IEntryCatalog，不认识 DLL 路径或平台资源。
- 契约 DLL 不创建执行模块、捕获会话状态或引用平台实现。执行与契约按完整 typeId 连接，平台展示通过接口或位于平台外层的直接调用接入。
- 校验/编译/build/export 共用选定 catalog；缺契约/缺执行明确失败，不回退全内置或静默跳过。JSON 描述是派生查询结果。

## 取舍与后果

固定入口降低发现复杂度，静态列表复用现有模块协议；代价是开发者必须部署与框架兼容的契约及依赖。加载器需要共享公共类型身份并隔离同名项目程序集。

程序集拆分会改变内置作者类型的命名空间和引用；迁移一次性完成，不复制两套参数规则。工具无需平台执行依赖，单平台扩展也无需被强制改写成通用 presenter。

schema 可声明编辑/编译契约，无法自行证明实际媒体/视觉后端可用；宿主需要检查执行覆盖和平台端口。契约版本与游戏构建应保持一致，不承诺任意旧 DLL 二进制兼容。

实施已验证独立内置模块、两个同名项目 DLL 的隔离与共享类型身份、CLI/Editor 项目 catalog 生命周期、使用相同契约的编译/导出，以及单平台截图执行。固定文件为 `Contracts/GalNet.Project.Contracts.dll`，入口为 `GalNet.Project.Contracts.ProjectSchemas.Modules`。模块 ID 可重复，仅完整 entry ID 排他。实现结果和环境边界见关联 feature；不提供旧接口或旧 DLL 的兼容 fallback。

## 关联

- [Feature](../../features/F-20261013-01-editor-cli-operations/feature.md)
- [设计](../../features/F-20261013-01-editor-cli-operations/design.md)
- [阶段计划](../../features/F-20261013-01-editor-cli-operations/phase-plan.md)
- [ADR-0002](0002-entry-schema-execution-registration.md)
