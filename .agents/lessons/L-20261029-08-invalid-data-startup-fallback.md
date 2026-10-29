# 安装内容损坏异常与启动 profile 回退

状态：confirmed
日期：2026-10-29
来源：features/F-20261029-02-packed-game-documents

- 现象：新增 game.pak 身份读取后，profile 定位先于日志和启动窗口执行，损坏包可能提前终止程序。
- 根因：`System.IO.InvalidDataException` 的基类是 `SystemException`，不是 `IOException`；原来针对文件与 JSON 的 catch 不会包含 GPAK 验证失败。
- 发现：复核 Sample 启动顺序，并在实际 PowerShell 宿主查询异常 BaseType 确认。
- 正确做法：只在允许缺失身份的 profile 探测边界显式处理 InvalidDataException，暂用路径摘要；正式内容加载继续拒绝损坏包，由启动窗口报告错误。
- 禁止：把内容加载校验本身改成静默回退，或认为所有 System.IO 命名空间异常都会被 IOException 捕获。
- 回归：PackedDocumentTests.CorruptedPackedDocumentsAreRejectedWithoutLegacyFallback 同时验证 profile 探测不抛出与内容加载仍拒绝；isolated-integration.trx 中 game.pak/localization.pak 两用例通过。
