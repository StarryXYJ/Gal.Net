# ReadyToRun 与严格契约字节身份

状态：confirmed

现象：ReadyToRun 发布会转换输入 IL 程序集；即使反射/schema 语义相同，发布 DLL 的 SHA256 也可能变化。

根因：本仓契约采用严格二进制身份，Core、Contracts.Builtins 及 GalNet.Project.Contracts 参与契约闭包；预编译是二进制重写，不能当作透明复制。

发现方式：F-20261025-01 的同源码 IL/R2R 发布对照，比较 SHA256 与 PE ManagedNativeHeaderDirectory；最终两个非 Benchmark 发布再次核对编译输入与发布输出，并实际加载 Editor 默认契约 45 项。

正确做法：PublishReadyToRun=true 时通过 PublishReadyToRunExclude 排除上述三个 DLL。必须验证实际部署字节与编译输入一致、native header 为空，并执行真实契约加载/宿主启动；其他程序集可正常 R2R。命名发布配置的 RID restore 使用隔离 bin/obj/lock，相关原则见 L-20261025-02。

适用范围：R2R 或其他会重写程序集的发布转换；新增契约闭包依赖需重新核对排除项。

禁止做法：为发布加速放宽严格契约哈希；仅检查编译通过或 schema 语义相同就宣称身份安全；把 Benchmark 与普通构建的契约哈希跨模式比较。

来源：features/F-20261025-01-cpu-startup-benchmark/runtime-profile.md。
正式操作：docs/manual/publishing/ready-to-run.md。
