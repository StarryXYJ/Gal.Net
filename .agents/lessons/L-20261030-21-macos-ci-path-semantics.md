# L-20261030-21-macos-ci-path-semantics

- 状态：confirmed
- 来源：features/F-20261029-05-cross-platform-applications/release-integration-20261030.md

macOS 默认临时目录经 /var 系统符号链接，资源 no-links 检查会正确拒绝；CI 测试进程应使用解析后的真实 TMPDIR，不能放宽产品检查。修正后 Assets 仅剩一条错误的平台断言：.NET Path.GetRelativePath 在 Windows 和 macOS 都使用不区分大小写比较，测试不能只判断 IsWindows。远端两轮日志和本地 9 项路径回归为证；不由此宣称 macOS 应用/设备验收通过。
