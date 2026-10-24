# 图片差分图集收尾自查

日期：2026-10-23
范围：P5 实际 diff、P1–P4 验收记录及当前正式规范。实施者自查，未冒称独立评审。

结论：pass-with-follow-up。本阶段功能验收有证据，未发现阻止图集示例交付的问题；Editor 基线失败、释放抖动和未执行的扩展矩阵保留如下。

## 核对结果

- 样例直接接入默认入口，两个目录只包含其各自图集图片；旧图片像素、稳定 ID 与 Gallery item 保留，既有分支通过中英文完整运行验证。
- 测试使用真实导出/安装/PAK 与 compiled primitives，不用手写 catalog 代替发布链。原生夹具在启动前移除其作者项目，不能从原目录读取图片。
- 两种 fallback 均保留请求并在恢复后继续切换；正常、快速、CatchUp 均有相同实际结果断言。移动/循环/第二实例/hide/re-show 有独立断言。
- native 测试等待外部异步任务发布完成，五个渲染检查点不同，人工逐张查看图片；正常关闭并等待 exit 0。截图与范围明确，不把帧大小断言当成完整视觉证明。
- 测试夹具因 Sample 默认指令变化而同步适配；没有修改业务匹配算法、状态版本或 presentation 行为。没有暂存/提交并行调试面板等其他 feature 改动。
- 现有 spec 的过时 UI 状态已修正，手册链接到 Sample 可复现检查点；不把性能观测写成保证。

## 保留事项

1. Editor 全量 257 passed / 3 failed：两项 P4 已记录的 Group tail/row width 布局断言；另一次 `EntryFieldsUseProjectOrderAcrossMountedPagesAndSurvivePageCloseAndReopen` 在 Headless DisposeAsync 抛 NRE，单独复跑通过。未确认释放抖动根因，不能称其已修复；本轮五个图集 UI 用例均通过。其余八项目 1258 passed，原生显式 1 passed，完整解决方案构建通过。
2. 未进行本轮人工 Editor/存档菜单点击与高 DPI/全屏/多平台/长时矩阵。已完成的是 P4b 真实挂载作者流程、运行状态/落盘恢复和本轮原生渲染截图，不能宣称所有人工场景完成。
3. 规模测试为 4096 个纯定义变体及 10000 次选择，未包含 4096 张真实图片或数千 GUI 选项的渲染。原生宿主观测包含预加载和 shader 等资源；当前数据不能证明长时无泄漏或无限规模支持。

上述扩展验证作为明确 follow-up，不隐去基线或把未测范围标记为 verified。阶段数字及复现命令见 [P5](phase-5.md)。
