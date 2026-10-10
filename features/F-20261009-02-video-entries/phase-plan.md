# 实施阶段

## Phase 1：契约与运行时（verified）

前置：用户确认推进规则并授权实现。定义视频 presenter、entry 参数、Layer 视频源状态与实例生命周期。验证阻塞分段跳过、停止/替换和稳定快照。退出：Builtin/Runtime 测试通过。

证据：Builtins 51、Runtime 35 项通过；Integration 的视频测试确认连续推进仍阻塞并保存此前快照，自然结束自动继续。

## Phase 2：解码与场景合成（verified）

依赖 Phase 1。接入 LibVLC 像素回调、帧租约和现有图层生命周期，组合到 Sample。验证视频像素进入同一排序/变换/效果管线与资源清理。退出：Presentation 测试与 Sample 构建通过，真实 niko.mp4 解码有证据。

证据：Sample 编译成功；Presentation 77 项通过；4 项 opt-in native smoke 通过，实际解码 1280×738 帧并输出场景 PNG，覆盖连续 seek、自然结束、加载期 hide/replace。

## Phase 3：样例与文档（verified）

依赖 Phase 2。添加视频 meta 和 Raw 样例，编译内容、运行 Headless，同步 spec 和 feature 总结。退出：编译内容与无界面 smoke 通过；记录可用验证、剩余限制，不把未执行的人工观看宣称通过。

证据：Editor CLI 编译 4 个 Raw group 成功；提供控制台输入后 Headless 运行至结尾且 exit 0；本次修改文件的 whitespace 和 IDE0005 检查通过。Core 57、Integration 34 项通过。

## 整体收尾状态

视频功能实现和以上阶段已验证，但架构门禁存在既有失败（24 通过、1 失败）：Builtins 项目在 HEAD 已引用 Runtime，当前允许依赖白名单尚未列入 Runtime。未修改白名单或既有音频结构；feature 保持 `validation-blocked`，不宣称所有门禁通过。详情见 [summary.md](summary.md)。
