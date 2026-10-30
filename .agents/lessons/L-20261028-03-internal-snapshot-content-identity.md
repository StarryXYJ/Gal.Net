# 内部重建快照也要保留内容身份

状态：confirmed，2026-10-28。

现象：打包原生 Sample 可正常启动，但调试 Seek 在 RestoreFromCoreAsync 报“不同内容构建或缺少剧情包”。开发目录没有复现，因为开发 ContentBuildId 为空。

根因：给存档增加 ContentBuildId/StoryPackages 后，普通稳定快照已加身份，调试 replay 仍手写 new GameSnapshot，只复制节点、状态和变量。Sample 的精简调试存档也有同样遗漏。

正确做法：区分外部恢复输入和当前引擎生成的内部快照。内部 replay 基线由当前引擎 StampContent，精简外部副本保留源身份；不要放宽外部存档兼容校验。新增快照字段时搜索所有手写复制、缓存和替代工厂，并使用非空构建身份测试。

验证：Debug 配置下显式 Packaged_variants_render_in_native_sample_and_close_cleanly 通过；Runtime DebugSessionTests 增加当前/缓存/隔离 Seek 与未标记外档拒绝回归，SampleDebugSnapshotFactoryTests 增加身份复制断言。

适用：调试、回放、回滚、存档精简及跨模块快照工厂。

来源：features/F-20261028-01-content-packaging/phase-6.md；docs/spec/runtime/execution.md。
