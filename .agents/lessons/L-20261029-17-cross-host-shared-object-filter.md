# Windows 主机筛选 Linux so 文件使用精确后缀

- 状态：confirmed
- 来源：F-20261029-05-cross-platform-applications
- 日期：2026-10-29

Windows Directory.EnumerateFiles 的 *.so* 会按大小写不敏感规则匹配 System.Net.Sockets.dll。Linux 源码发布成功后，封装器因此把托管 PE 送入 ELF 校验并错误拒绝。

不要用主机通配符判断目标平台文件类型。以 Ordinal 检查文件名以 .so 结尾或包含 .so.，再验证 ELF64、小端、EM_X86_64；保留原生验证，不把所有 .dll 当作原生，也不跳过错误架构。Windows 上的回归夹具包含托管 System.Net.Sockets.dll 与真实头格式的 so，覆盖完整准备/合包路径。

证据：out/cross-platform-validation/20261029-build-compose/actual-linux-before-so-fix.json；DistributionComposeTests.LinuxPublishDoesNotTreatManagedSocketsDllAsASharedObjectOnWindows。适用于在 Windows 为 Linux 打包的工具，不要求改变 Linux 的文件大小写规则。
