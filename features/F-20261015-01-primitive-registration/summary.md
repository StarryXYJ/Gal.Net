# Primitive 注册简化总结

## 实现

删除 PrimitiveImplementation。IPrimitiveModule 从暴露 Instructions 描述对象集合改为 Register(PrimitiveRegistry)；PrimitiveModule 接受注册回调。每个 view 组合期间使用一个注册表，最终只保留完整 ID → 实例工厂委托，不为每条指令创建 implementation 包装对象。

Register<T> 在组合期检查 DTO 契约，在调用期解码独立 Options。Immediate / Async 保留同步、单次异步短入口。组合成功或失败后注册表均冻结；内置契约筛选支持嵌套交集，并恢复外层筛选，已选指令仍校验最终 catalog。

对白、NVL、动画、动画计划、视频、效果和粒子实例直接接收 context/Options。模块借用 presenter，共享音频状态与释放顺序保留。request 构造入口继续供场景重放和底层消费者使用；同步命令继续用回调。Sample 截图和自定义指令测试已迁移。

## 实际成本

生产源码 src/ 相对本次开始时 HEAD：新增 264 行、删除 301 行，**净减 37 行**。包含新增 PrimitiveRegistry 全部 88 行；排除测试、正式文档和 feature 记录。按物理行统计，包含空行和注释，不通过挪入测试、生成代码或压缩排版来隐藏生产成本。

低于事前 40–100 行估算。原因是解码、schema/DTO 校验、Immediate/Async 和注册冻结仍需要公共代码，复杂实例新增 Options 入口后，也仍需保留重放消费的 request 入口。主要收益是消除逐指令包装对象和公共类型、缩短复杂指令注册的参数适配，而非大幅减少执行逻辑。

## 验证

- Core 104、Runtime 80、Presentation 185、Builtins 110、Integration 104、Architecture 37，共 620 项通过。
- 新增覆盖：成功/失败后的注册冻结、重复 ID、嵌套 catalog 子集筛选、筛选后恢复严格校验、已选指令 DTO 不匹配、工厂返回 null。既有测试继续覆盖默认值、converter、独立 Options、BatchId、播放模式、错误、释放与重放。
- 完成改动文件的 whitespace 和 IDE0005 门禁；git diff --check 无错误。
- 最终 `dotnet build GalNet.slnx --no-restore -m:1 -v:quiet -p:UseSharedCompilation=false` 成功，0 错误、38 警告。最后补充嵌套 catalog 交集筛选后，Presentation 185、Builtins 110、Integration 104 再次全部通过；上述 620 是不重复用例数，不把重复运行累加。

日志与 TRX 位于 Temp/primitive-solution-build.log、Temp/primitive-format-final.log、Temp/primitive-test-results/ 和 Temp/primitive-final-results/；这些是本地验证产物，不进入正式源码。

测试宿主和 dotnet format 的构建宿主在沙箱内无法通过管道通信；允许进程通信后正常完成。NuGet 漏洞源不可达产生 NU1900 警告；未改依赖或锁文件。没有运行 Explicit 原生媒体用例或完整 GUI 人工验收。

## 文档和边界

已更新 docs/development.md、docs/spec/runtime.md、docs/spec/entry-types.md 和 agent 上下文。ADR-0002 的契约/执行分离仍然成立，未改写已接受的历史记录。公共 API 有破坏性调整；外部自定义 module 需要迁移到注册回调。内容、存档格式、Core/Runtime 执行协议不变。
