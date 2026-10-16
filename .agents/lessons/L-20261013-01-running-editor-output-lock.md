# 运行中的 Editor 会锁定默认构建输出

状态：confirmed
来源 Feature：F-20261013-01-editor-cli-operations

## 现象、根因与证据

2026-10-13 验证契约程序集迁移时，Editor 源码编译已完成，但复制依赖到 bin/Debug/net10.0 时出现 MSB3026、MSB3027、MSB3021；日志明确指出输出 DLL 被正在运行的 .NET Host 占用。不是业务代码编译错误，禁用共享编译也不能解除运行宿主的文件锁。

## 正确做法

保留用户正在运行的 Editor，不主动结束进程。使用针对该 feature 的仓库临时目录作为 dotnet build --output 目标，例如 Temp/F-20261013-01-editor-build。等待前一个 MSBuild 进程结束，再启动后一个共享依赖构建；测试环境 SDK/缓存访问限制沿用既有受限构建 lesson。

## 适用范围与禁止做法

Windows 中验证正在运行的 .NET/Avalonia 程序。不要为构建擅自关闭用户程序，不要把输出文件锁诊断误报成代码回归，不要并行启动争用相同共享 obj 的多个 MSBuild。
