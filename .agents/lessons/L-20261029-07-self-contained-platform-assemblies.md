# 自包含部署的业务 DLL 不能按 corelib 目录排除

状态：confirmed
来源 Feature：F-20261029-04-new-project-application

## 现象与根因

识别契约依赖闭包时，若把 TRUSTED_PLATFORM_ASSEMBLIES 中与 System.Private.CoreLib 同目录的条目全部视为平台程序集，框架依赖部署可以工作，自包含部署却会把同目录的 GalNet.Core、Contracts.Builtins 等业务依赖一起排除。目录位置不表示程序集归属。

## 发现与证据

当前自包含发行的 lib 同时包含 framework 与应用 DLL；GalNet.Editor.deps.json 的 targets 含 runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.12，其 runtime 节点单独列出平台资产。用自包含 ContractSmoke 实际调用 ProjectContractSource，SchemaA 依赖闭包保留 Core、Builtins、SchemaDependency，读取真实 Sample Debug 输出只得到 Core、Builtins、Project.Contracts 三个 DLL，保持原始字节且释放临时目录。

## 正确做法与边界

自包含部署从运行时 deps 清单的 runtimepack.Microsoft.NETCore.App.Runtime.* runtime 资产识别平台程序集；框架依赖部署才使用独立框架目录中的 TPA 条目。不得直接按自包含 lib 目录排除程序集，也不得把全部 TPA 当作纯 BCL。此规则用于依赖收集；已有 ProjectSchemaContext 的平台回退逻辑未在本 feature 中修改，不据此宣称它的所有部署边界已修复。

证据：out/new-project-app-smoke/published/ContractSmoke.exe；实现：src/Editor/GalNet.Editor.Shared/Services/ProjectContractSource.cs；设计与总结：features/F-20261029-04-new-project-application/。

## 2026-10-29 补充：契约解析必须先复用宿主框架程序集

F-20261029-05 的真实自包含 CLI 从 Sample 的 lib 加载契约时，ScreenshotSchema.CreateModule 抛 ForDto(JsonElement) 的 MissingMethodException，尽管两边 Core/Builtins hash 相同且方法确实存在。根因是 SchemaLoadContext 先从相邻 lib 加载 System.Text.Json.dll，造成不同上下文中的 JsonElement 类型身份分裂。

现已把同一 runtimepack 平台分类用于 SchemaLoadContext：共享 Core/Builtins 后，平台程序集在查询项目私有解析器之前返回默认上下文；业务依赖仍在项目上下文中解析。真实 CLI 重新执行 project create、export、compose 成功；FrameworkDllBesideContractUsesTheHostsJsonElementIdentity 用相邻框架 DLL 和自定义 Composite 覆盖此问题。前文“尚未修改平台回退逻辑”的限制已由本次修订取代。

不得通过重编译所有契约、放宽签名检查或再加载一份 Core 来掩盖类型身份分裂；检查原生/自包含部署时，必须覆盖含完整 framework 的实际 lib，而不只测精简 Contracts 目录。
