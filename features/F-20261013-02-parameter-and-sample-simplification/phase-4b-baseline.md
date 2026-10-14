# Phase 4b 验证基线恢复

2026-10-15，状态：verified。

两个设置持久化用例通过程序集缓存的 HeadlessUnitTestSession.Dispatch<bool> 运行完整异步断言，提供 UI pump，关闭各自业务资源但不释放框架拥有的缓存 worker。原有全屏/窗口尺寸、保存后恢复、不覆盖其他变量及不重复写文件的断言不变。相关 fixture 标记 NonParallelizable；设置实现的其他任务改动未纳入提交。

未使用 filter 的完整 Integration 101/101 通过，无排除用例；TRX：Temp/F-20261013-02/phase4b-baseline.trx。4b1 中记录的两项挂起是历史限制，现在已补齐验证，不改写当时的原始证据。
