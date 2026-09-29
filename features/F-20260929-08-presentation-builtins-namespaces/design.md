# Presentation 与 Builtins 命名空间归位设计

## 目标命名空间

- `GalNet.Presentation.Abstractions.View`：拥有 `IGameView`、细分 presenter、展示 request DTO 与 `CompositeGameView`。它与同程序集已有的 `GalNet.Presentation.Abstractions.Navigation`、`GalNet.Presentation.Abstractions.Runtime` 保持同一根命名空间。
- `GalNet.Primitives.Builtins`：拥有内置 entry DTO、primitive instance、module 和 presentation replay。Core 继续拥有 `Entry`、`PrimitiveEntry`、`CompositeEntry`、`EntrySchema` 与 catalog 基础设施。

不额外引入 `.Entry` 子命名空间：Builtins 当前大多数公开类型及 transition entries 已位于程序集根命名空间，统一到根可避免同一扩展模块形成两套导入规则。

## 兼容策略

采用直接迁移。项目未配置 NuGet 打包或公共 API 基线，也没有已证明的仓库外消费者；保留旧命名空间会继续制造错误所有权，并为每个公开 class 引入继承或包装限制。稳定文件格式依赖 entry `TypeId`，不序列化 CLR 类型全名，因此本次迁移不改变内容兼容性。

## 自动化门禁

架构测试读取两个项目的源码 namespace 声明，分别要求以程序集所有的根命名空间开头。该测试直接覆盖本次问题类型，同时不对其他尚未纳入路线图的程序集扩大规则。
