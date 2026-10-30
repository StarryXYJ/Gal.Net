# 等待交付也持有共享加载资源的生命周期

状态：confirmed
来源：features/F-20261030-01-project-quality/phase-plan.md，P1。

## 现象与根因

两个 Acquire 共享完成的加载，但 continuation 的执行顺序不同。首个调用者取得并释放句柄时，RefCount 可归零，第二个调用者仍在等待领取。仅按 RefCount 释放会提前销毁缓存对象，第二个调用者得到 null。现有“多个已取得句柄”及“一个等待者取消”测试未覆盖这个间隙。

## 发现与验证

使用独立可控 SynchronizationContext，先泵出第一个请求、释放其句柄，再泵第二个请求，无需 sleep 或依赖线程池碰撞。旧实现三个新用例失败；修复后 Assets 107 项通过。覆盖 ID/路径取得、最后等待者取消、manager 销毁及恰好一次释放。

## 正确做法

共享加载的缓存条目在已发放引用与尚未领取的等待者均为零时才可正常回收。复用现有 owner 的 PendingWaiters，集中判定并从缓存移除，在锁外 Dispose；最后等待者取消也必须走最终清理。manager 整体销毁仍可终止所有待交付请求。

## 适用范围与禁止做法

适用于 single-flight 加载后向多个消费者分发引用/租约的缓存。不要把“加载任务完成”等同于“所有消费者已取得引用”，也不要用随机压力重跑代替确定性交错测试。

关联：src/Shared/GalNet.Assets/AssetManager.cs、test/GalNet.Assets.Tests/AssetPipelineTests.cs、docs/spec/assets.md。
