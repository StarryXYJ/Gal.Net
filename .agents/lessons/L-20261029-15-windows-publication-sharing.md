# Windows 发布目录的短暂句柄冲突

- 状态：confirmed
- 来源：F-20261029-05-cross-platform-applications
- 日期：2026-10-29

Directory.Move 在 Windows 上可能因其他句柄未共享 delete 而短暂报告 Access Denied；不能仅凭项目启用了 FileSystemWatcher 就归因给 watcher。独立最小探针在带 watcher 的 100 次中失败 1 次、不带 watcher 的 100 次中失败 2 次，等待 100ms 后全部成功；具体外部持有者未定位。

发布层统一使用 FilePublication：仅 Windows HRESULT 5/32/33 重试原来的原子 Move，最多六次、等待总计 620ms；永久拒绝和取消仍失败。禁止删除旧文件后复制来规避；恢复旧输出的回滚不能继承已取消的 token。

测试使用真实 CreateFileW 目录句柄，GENERIC_READ + FILE_SHARE_READ|WRITE、不共享 DELETE，确认初次 Move 确实被阻挡。desiredAccess=0 的探针不保证产生预期冲突，不能作为测试前提。覆盖释放后成功、永久占用、等待时取消和旧文件仍可读。Storage 32/32、Editor.Shared 494/494；两种分发 CLI 连续导出 40/40 及完整 Windows DesktopSmoke 通过。

证据：out/cross-platform-validation/20261029/windows-rename-probe.jsonl、publication-storage.trx、publication-editor-shared.trx、windows-repeat-exports.json。适用范围是短暂 Windows 发布冲突；不是普遍重试所有 IO 的建议。
