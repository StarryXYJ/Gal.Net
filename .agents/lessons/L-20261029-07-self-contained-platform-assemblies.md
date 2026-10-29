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
