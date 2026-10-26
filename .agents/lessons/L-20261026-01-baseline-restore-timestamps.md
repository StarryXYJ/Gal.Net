# 基线对照后恢复源码必须使增量构建失效

状态：confirmed
日期：2026-10-26
来源：F-20261015-02-editor-authoring-workspace / GC1

现象：隔离快照先成功编译新 AuthorChanges API，再替换旧服务源码重现一个基线失败。恢复备份的候选源码后，测试编译报告 UndoRedoHistory 没有 AuthorChanges；磁盘上的候选源码明明已有该成员。

根因：File.Copy 恢复了备份的旧时间戳，而基线 DLL 的生成时间更晚。MSBuild 增量检查复用了基线 DLL。源码字节恢复不等于构建图已经失效。

发现方式：gc1-baseline-character.trx 独立复现原有角色引用测试失败；随后 editor-final.log 出现 CS1061，核对恢复源码与 DLL 时序后确认不一致。对快照内候选文件显式 SetLastWriteTimeUtc 或执行 Rebuild 后才能继续验证。

正确做法：基线与候选优先使用独立源/输出目录。若复用快照进行对照，恢复候选后必须 Rebuild，或刷新所有被替换源码的时间戳并核对生成结果；验证最后核对主工作区与快照的源字节一致。不要修改主工作区源码来迎合旧 DLL。

适用：复制源码备份进行回归对照、测试旧实现、热切换隔离验证快照。不能把仅恢复文件或一次 no-build 成功当作当前候选证据。构建日志、首次失败和恢复方式留在阶段记录。
