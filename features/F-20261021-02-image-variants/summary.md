# 图片差分图集实现总结

日期：2026-10-23
状态：P1–P5 技术阶段 verified；整体自查 pass-with-follow-up。已知 Editor 基线/释放抖动和未执行的人工扩展矩阵继续保留，不宣称全部人工验收完成。

一份 `.variants` 描述指向一个图片目录，以文件主名下划线分段表达有序维度。显示实例独立保留完整请求，按精确 → 从后向前累计删除未保护维度 → 显式默认图解析。换图保留 Layer 身份、变换和连续属性动画，回退不覆盖未显示的请求。

## 实现与决定

- Core 提供不可变定义、构建诊断和纯解析；目录扫描与 JSON codec 在资源/作者层。
- 本地 provider 和导出链使用同一编译定义；PAK payload 仅含维度、默认图和 sprite ID。运行不扫描作者目录。
- 会话异步预载并验证 catalog 后注入 layers 模块。`variants.show/select` 本身同步提交状态；CatchUp 使用同样的逻辑。这个放置方式保持 Engine 读档换根前的同步校验协议。
- `Layer.Variants` 保存来源、完整请求、保护集合、实际键和原因；`builtin.layers` section v2，外层 GameSnapshot v4。内容不一致和旧 section 明确拒绝，无兼容迁移。
- Editor 接入目录创建、配置草稿、全轴选项/回退图片预览、show 初始选择和 select 对象/维度/值联动，以及静态场景事实、撤销/保存/资源代次保护。
- Sample 默认 intro 使用真实 `xy` 立绘与 classroom 背景；源素材字节与已有资源 ID 保留。新命名的适配只改变夹具，不改变解析规则。

## 验收映射

| 需求验收 | 证据 |
| --- | --- |
| 不同段数合法，不需所有组合 | P1 原始五名称矩阵；P4b 导入/预览；P5 xy/solo 与三轴 classroom |
| 单维保留其他意图，回到原选择 | P1/P3 smile_skirt→weap→smile；P5 xy_shy→solo→xy_shy，含 snapshot |
| 回退稳定、可诊断、无换选项猜测 | P1 乱序/位置反例；P4b 候选链；P5 Reduced/Default 实际图片 |
| 实例独立且保留变换/动画 | P3 Runtime 测试；P5 PAK 第二实例、move、loop、hide/re-show |
| 发布不依赖原目录、ID 稳定、坏引用拒绝 | P2 真实 producer/事务反例；P5 导出定义/图片逐字节比对、原生启动前删除临时作者项目 |
| 存档/CatchUp 保留请求与实际结果 | P3 内容冲突拒绝矩阵；P5 三模式回退快照继续选择、真实 FileSaveService 保存加载 |

各阶段完整证据：[P1](phase-1.md)、[P2](phase-2.md)、[P3](phase-3.md)、[P4a](phase-4a.md)、[P4b](phase-4b.md)、[P5](phase-5.md)。最终构建/回归数字、曾失败的验证和环境边界以 P5 表格为准。

## 当前文档

- [资源格式与编译](../../docs/spec/assets.md)
- [指令契约](../../docs/spec/entry-types.md)
- [状态与恢复](../../docs/spec/runtime.md)
- [编辑文件会话](../../docs/spec/editor-files.md)
- [场景对象分析](../../docs/spec/editor-scene-targets.md)
- [创建/配置/切换手册](../../docs/manual/image-variants.md)
- [可运行示例及检查点](../../GameTestCase/README.md#image-variants-walkthrough)

## 限制与后续

没有分层合成、专属差分交叉淡化、维度连续动画、隐式剧情迁移或旧存档兼容。图片改名、维度身份/顺序变化需修正引用，坏配置明确拒绝。资源定义按会话冻结，播放中不热更新。

原生渲染证据为独立进程的 SceneHost PNG 及正常退出；Editor 作者流程由真实挂载测试覆盖。本轮没有人工点击完整 Editor/存档菜单，也没有完成全屏、高 DPI、其他平台和长时间压力矩阵；不将自动化/截图验证扩大为全部人工验收。性能数据只覆盖已记录的纯索引样本与当前真实素材宿主，不承诺无限规模或忽略默认预加载的内存成本。

最终自查结论和已知基线问题见 [review](review.md)。
