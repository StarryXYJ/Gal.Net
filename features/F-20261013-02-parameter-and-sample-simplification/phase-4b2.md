# Phase 4b2：清理不短路

2026-10-15，状态：verified。

SampleCleanup 只提供显式顺序的同步/异步尝试和错误聚合，留在 Sample；不拥有资源、不决定释放顺序。Session、Runner、Presentation 和下属 SaveSession/ResourceScope 使用同一机制。

Session 停止、runner 释放、设置 flush、save session 和资源释放全部尝试；先摘除已拥有的资源引用，避免失败后再释放。重复 Session.DisposeAsync 返回同一完成任务，也保留同一错误结果。Runner 中绑定/初始展示清理失败不阻止 Engine、重放、页面重置和后端释放，页面/NVL 重置仍早于 presentation 释放。Presentation 保持 restorer/view → video/audio → effects/presenters/layers 的顺序与 resolver 所有权。

审查发现下属 SaveSession 的 Gallery 释放可阻止 progress/variables/save service，ResourceScope 的某个 texture handle 失败可阻止 media files/assets；一并修正。两处初始化失败路径继续清理已经获得的资源，清理失败时将原始初始化错误一起保留。SaveSession 构造器在内部可组合，便于通过实际对象注入 Gallery/store 故障，不增加公共测试 API。

验证覆盖：同步/异步/取消多项错误保留、真实 SaveSession Gallery/变量 store 失败后仍提交 progress 并释放存档服务、真实 Session 在设置保存路径不可写时仍清空 Gallery 所有权、重复关闭复用结果。原挂起来自测试在 UI 回调使用同步 Assert.ThrowsAsync 阻塞 UI pump，改为 await + try/catch；lesson 已记录。最初测试还错误假设 ListSlots/缺失文件的 Load 必须访问存档锁，以及目录目标只抛 IOException；修正为真实锁入口 DeleteAsync，Windows 文件错误允许 UnauthorizedAccessException。没有通过吞异常改变生产策略。

最终 Integration 104/104、Architecture 37/37 通过，包含 Sample 构建；证据为 Temp/F-20261013-02/phase4b2-integration-final.trx、phase4b2-architecture.trx。本批文件运行 IDE0005 与 whitespace 整理。整个 feature 的原生媒体人工交互并未由此故障测试替代。

生产 C# 含注释/空行净增 25 行（含 23 行内部清理器）；收益是清理正确性和错误完整保留，不宣称本批总体删行。
