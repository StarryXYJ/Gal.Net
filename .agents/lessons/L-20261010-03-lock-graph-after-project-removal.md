# 删除项目后检查依赖锁图

状态：confirmed
来源：Entry 注册重构，删除 Presentation.Defaults

## 现象与根因

删除 csproj 和 ProjectReference 后，普通 restore 与后续 locked restore 返回成功，但外层 packages.lock.json 仍保存已删除项目及其旧依赖。还原缓存没有重新生成锁图；成功返回不能替代检查锁文件内容。

## 证据与正确做法

本轮用 rg 检查仍存在的 galnet.presentation.defaults 锁图条目。对 Integration、Editor.Tests、Presentation.Tests 的具体 csproj 执行 dotnet restore --force-evaluate 后，所有受影响生产与测试 lock 文件移除了旧项目；再执行 --locked-mode 验证。不要手写锁文件或仅依据退出码认定依赖图已同步。

适用范围：项目删除、依赖移除或跨项目引用变化。边界：此记录不建议无条件删除 bin/obj；强制重算只针对已确认受影响的 restore roots。
