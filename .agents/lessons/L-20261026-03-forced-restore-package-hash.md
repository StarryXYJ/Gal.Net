# 强制重算锁图不能替代包哈希验证

状态：confirmed
来源 Feature：[引擎核心重构 R1](../../features/F-20261026-01-engine-core-simplification/phase-plan.md)
关联：[删除项目后检查锁图](L-20261010-03-lock-graph-after-project-removal.md)

## 现象与证据

R1 移除 Runtime 对 Presentation.Abstractions 的引用后，按既有流程对受影响根执行 dotnet restore --force-evaluate。Browser 使用本地包源返回成功，但其 Microsoft.NET.Sdk.WebAssembly.Pack 10.0.12 的 contentHash 从仓库既有值改为本地包的另一值；第三方版本没有改变。

在本机 .NET SDK 10.0.401 下，即使同时指定 --locked-mode，--force-evaluate 仍重写了该哈希并返回成功。恢复仓库原哈希后，使用 --locked-mode --force（不含 --force-evaluate）再次从相同本地源还原，明确复现 NU1403。日志位于忽略产物 artifacts/engine-core-audit/r1-browser-restore.log、r1-browser-locked.log。

## 原因与正确做法

强制重算用于生成新锁图；它接受当前解析结果并可能重新生成包身份，不能证明这些字节符合旧锁文件。包来源/字节差异的起因尚未确认，不能直接将其归因为恶意包或损坏。

1. 有意变更项目依赖时，强制重算后审查整个锁文件差异。
2. 本次仅移除项目依赖，第三方版本、哈希和目标框架键均应保持原值；撤回无关生成变化，而非把本地包哈希提交为修复。
3. 生成与验证分开：再执行不带 force-evaluate 的 locked restore；需要失效还原缓存时使用 --force。
4. 独立报告包验证或运行时包缺失，不能因为构建已成功或重算返回零就宣称发布验证通过。

适用范围：已有锁文件的项目引用调整和本地源/离线还原。不要手写项目依赖图、关闭哈希验证或改写包缓存来绕过错误；需要升级第三方包时另行核实来源和版本。
