# 进程树清理失败必须结束等待

- 状态：confirmed。
- 日期：2026-10-30。
- 来源：features/F-20261030-01-project-quality，P8a。

## 现象与根因

Windows 受限宿主内，taskkill /PID <本次子进程> /T /F 返回 1 和 Access denied。旧执行器忽略返回码，随后无超时地 process.wait()，导致原本两秒的超时检查持续等待。独立受限探针复现了拒绝；授权环境中的真实父子进程测试通过。

## 正确做法

清理命令检查退出码，并限制清理命令和后续等待的时长；清理失败保留错误，不能当作超时清理成功。测试需证明本次父子进程均退出且无关进程仍在运行，外部人工结束进程不能计入自动清理通过。Unix 存活检查不能只依赖 Linux 的 /proc，否则 macOS 会产生假通过。

实现与回归见 scripts/ci/process_steps.py、scripts/ci/test-application-publish.py。适用范围为脚本拥有的子进程树；不能按进程名广泛终止其他任务。
