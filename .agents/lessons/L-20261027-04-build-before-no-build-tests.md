# 同一输出根的构建和测试必须串行

状态：confirmed
日期：2026-10-27
来源：F-20261027-01-audio-mixer / 音频效果创作

现象：隔离构建尚未结束时启动 --no-build 的 Editor/Editor.Shared 测试，SDK 在 Copy 阶段出现 MSB3026/MSB3027/MSB3021。被占用路径均为同一 artifacts/audio-isolated/bin 下的测试依赖 DLL；正常运行的 Editor 不是这次占用来源。

根因：write_stdin 返回 session_id 表示构建仍在运行，不是已成功完成。测试提前加载了该输出目录的程序集，Windows 阻止构建覆盖；同时测试可能读取旧程序集，结果不能作为新源码验证证据。

发现方式：artifacts/audio-authoring-overlap-build.log 记录了重复复制重试和 30 个锁定错误；两个测试进程退出后，同源码的解决方案构建在约 6 秒内以 0 错误通过。

正确做法：明确收到 build 的 exit_code=0 后，再复制同次构建的契约产物并启动 --no-build 测试。独立测试进程可以并行，同一输出根的构建和测试不能重叠。保存源码指纹并在验证结束复核；重叠产生的日志保留但不作为最终验收。

适用：Windows 输出目录、隔离 artifacts、多项目构建和任何需要复用已有 DLL 的验证。禁止把 yielded/session_id 结果当作编译完成，也不要通过停止用户正在运行的 Editor 来修复自己测试进程造成的锁定。

相关：[隔离契约产物](L-20261019-03-isolated-contract-artifacts.md)。
