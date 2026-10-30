# 启动日志之前的调试校验失败

- 状态：confirmed
- 来源：F-20261021-03-external-game-debugging，2026-10-23 启动失败复现。
- 现象：Editor 启动 App 后一直等待连接，没有游戏日志。
- 根因：Sample 在日志初始化前解析参数并校验实际 Contracts SHA256；项目默认契约与 Sample 契约不同，抛异常退出；Editor 原先立即释放 Process，未收集 stderr/退出码。
- 发现方式：读取本地启动配置，对比两个实际 DLL 的哈希，以相同预期契约参数启动独立 Sample 进程，复现 InvalidDataException。
- 正确做法：组合根顶层捕获日志初始化前的异常，输出 stderr 并以非零码退出；Editor 监控自己启动的进程，持续排空并限制保留的 stderr，按会话隔离错误，断开后取消监控而不杀进程。
- 适用范围：外部 App 调试启动及其他先校验后初始化日志的 CLI/GUI 入口。
- 禁止：将没有日志视为没有错误；直接信任预期哈希；为方便连接取消真实契约校验。
- 文档：docs/manual/editor/preview-and-debugging.md。
