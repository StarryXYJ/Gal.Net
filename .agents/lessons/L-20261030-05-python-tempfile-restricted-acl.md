# Windows 受限令牌与 Python tempfile ACL

- 状态：confirmed。
- 日期：2026-10-30。
- 来源：features/F-20261030-02-documentation-structure。

## 现象与根因

本机 Python 3.14.7 的 TemporaryDirectory 能建立目录，但受限执行进程随后 mkdir 子目录、枚举和清理均报 WinError 5。在系统 Temp 和已授权仓库 Temp 内均复现；只改父目录无效。

Windows 的 tempfile 使用私有 0o700 ACL 建目录，与本执行宿主的受限令牌访问不兼容。使用普通 mkdir 的继承 ACL 后，相同的 10 项文档生成器测试通过，不需要放宽仓库权限。

## 正确做法与适用范围

在明确授权的工作区内，用 UUID 命名独立目录并以普通 mkdir 继承 ACL；仅在不需要私有敏感数据隔离的临时工作中使用。finally 清理前验证绝对路径、父目录及链接边界，避免删除其他任务目录。

不要仅因工作区路径内的 WinError 5 就申请整个用户目录权限，也不要删除陌生临时目录。本结论限于已复现的 Python/Windows 受限宿主；普通执行环境继续可使用标准 tempfile。

证据：scripts/docs/tests/test_sync_wiki.py 的 fixture 与 scripts/docs/sync_wiki.py 暂存实现；原 tempfile 测试在两个父目录均失败，继承 ACL 实现通过。
