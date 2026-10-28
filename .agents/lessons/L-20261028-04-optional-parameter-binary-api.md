# 可选参数也会改变公开方法的 CLR 签名

状态：confirmed，2026-10-28。

现象：FieldDescriptor 新增可选 resourceTypeId 后，当前源码可编译，但既有 SchemaGenerator 二进制兼容测试抛 MissingMethodException。

根因：C# 可选参数只影响调用方源码的默认值补齐，不能保留旧 CLR 构造函数签名。旧契约 DLL 仍调用原八参数构造函数。

正确做法：保留旧签名的转发重载，再提供新签名；保留不会造成源调用歧义的参数规则。用旧 API 独立编译 consumer，再让它装载当前 Core 验证，不以重新编译当前源码替代二进制兼容证据。

验证：FieldSchemaGeneratorTests.AConsumerCompiledAgainstTheOldDescriptorApiLoadsAgainstCurrentCore，以及最终 SchemaGenerator 全套 20/20 通过。

适用：Core/Contracts 的公开构造函数、插件契约、公开扩展方法和宿主加载的预编译模块。格式版本升级不自动授权破坏无关 CLR API。

来源：features/F-20261028-01-content-packaging/phase-6.md。
