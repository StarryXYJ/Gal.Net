# NativeAOT 发布属性与编译工具隔离

- 状态：confirmed
- 日期：2026-10-27
- 适用范围：以 ProjectReference 挂载 netstandard 源生成器的 NativeAOT 宿主
- 来源：引擎 AOT 实现与用户要求复用 Headless Sample
- 文档：docs/manual/native-aot.md

## 现象与根因

全局 `-p:PublishAot=true` 会沿 restore 项目图进入 GalNet.SchemaGenerator，使 netstandard2.0 项目触发 NETSDK1207。仅在 ProjectReference 上配置 GlobalPropertiesToRemove 能控制构建引用，不能单独解决本仓库此 restore 路径。独立 AOT 项目并非所需修复。

## 正确做法与验证

编译工具项目显式通过 TreatAsLocalProperty 接管 PublishAot、SelfContained、RuntimeIdentifier，并将其分别设为 false、false、空。玩家仍使用标准 PublishAot 选项。实际 Headless NativeAOT 发布并运行通过，dynamicCode=False、reflectionJson=False，最终 ILLink/ILC 警告按错误处理。

RID/ILCompiler 恢复图另外隔离至 .galnet，设置 RestorePackagesWithLockFile=false 和单独 NuGetLockFilePath；已有开发 lock 文件时仅前者不足以避免恢复图冲突。禁止为了发布修改或删除普通开发锁文件，禁止吞掉最终链接警告。
