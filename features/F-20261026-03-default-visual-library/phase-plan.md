# 阶段计划

## P1 效果资源与默认项目部署 — verified

补齐 Bloom / Blur / Vignette / ColorGrade / DepthBlur / SpeedLines / Reveal 等资源、项目用途绑定和默认模板部署。模板可内嵌于编辑器，部署后作为项目资产使用，不要求全项目固定 GUID。验证 metadata、SkSL 编译、项目 staging 与 dry-run，以及已有内容补入时保留修改。退出：资源能加载且默认项目获得可编辑源文件和指向自身资产的默认绑定。

结果：8 个 shader（含 MaskReveal）嵌入 Editor.Shared 初始化模板；默认项目创建将其复制为普通项目资产并生成自身 GUID。新增 `galnet.project.defaultVisualPrograms`；纯自定义契约和无契约 scaffold 不注入资源。准备计划克隆配置、保留已有绑定及未知 section，重复准备不替换已绑定资产。已有项目的用户入口留到 P4 集成，不将 Prepare API 宣称为已上线的补入命令。

验证：ProjectCreationTests + DefaultVisualTemplateTests 共 9 项通过；DefaultVisualShaderTests 共 8 项通过（metadata 及 SKRuntimeEffect 实际编译）。Editor.Shared 串行构建 0 警告/0 错误（RunAnalyzers=false）。普通多进程构建曾无诊断退出，串行构建通过；受限环境 testhost 连接超时，沙箱外相同测试通过，未将该超时归因为业务代码失败。尚不宣称 DepthBlur 的 layerZ 注入或 MaskReveal 的纹理绑定完成，二者属于 P2。

## P2 渲染能力 — verified

注入图层 Z；补齐 texture 参数租约和绑定。验证景深在 Z 变化后更新、遮罩真实采样、alpha 与资源释放。退出：CPU 像素证据与已有渲染回归通过；GPU 路径共享绑定代码并构建通过。

结果：CPU/GPU 层效果链每次绘制传入当前图层 Z，通用 shader 自动绑定保留的 `layerZ` uniform；外部 texture 参数按画布缩放，缺省可选纹理绑定透明值。默认宿主向效果运行器注入 AssetManager，各效果独立拥有纹理租约；替换/停止/Dispose/部分准备失败释放。异步准备使用实例代次防止停止后复活。27 项 shader/像素/生命周期及既有效果测试通过；GameHost 构建通过（既有 BuiltinScene nullable 警告 1 项）。像素测试验证移动 Z 后失焦及恢复、径向/定向集中线随 time 变化、遮罩实际采样及缩放。GPU 共享绑定路径构建通过，未宣称本阶段有真实 GPU 截图验收。

## P3 转场与演出组合 — verified

实现 catalog 第一批组合，带 schema、边界校验、临时资源清理和作者分析；不为每种 shader 新增效果 Entry。编辑器从项目用途绑定填充显式资源参数，编译器只传递 Entry 上的引用。验证缺失绑定、手动替换、项目绑定变更不追改旧 Entry、CLI 显式参数、shader 契约匹配与生成事件的资源依赖。验证展开、末帧、短时长、非法参数、不同播放 handle、快进/停止行为。退出：推荐第一批可调用且编译结果通过现有时间线验证，导出包含所需 shader 与纹理。

结果：新增 transition.reveal（Wipe/Radial/Noise/Blinds/Mask）、transition.blur、preset.motion（9 种动作）、preset.shake（最多 32 个同步图层）、preset.speedLines、preset.impact，共 6 个 Composite；未新增 shader 专用 Primitive。资源检查在项目 ValidateGroup / build / export 入口验证资源存在、layer stage、参数种类/可动画性/范围。编辑器插入与类型切换填充用途绑定；CLI 编译仍要求显式资源。场景分析编译复合时间线，引用/复制支持 targets[].handleId。新增四种编辑器语言标签。

验证：Builtins 完整 306 项通过；Editor.Shared 完整 447 项通过；后续范围检查调整后 4 项资源/作者用例再次通过；独立 artifacts 输出的桌面作者交互 9 项通过（实际预设创建/类型切换/Undo/Redo/保存重开、6 种原转场选择器、引用审查、批量插入）。两帧短时长、不同模式、锚点推近、显式引用传递、末帧清理、Normal/Quick/CatchUp 稳定状态与快照恢复均有测试。普通 Editor 输出被已打开的编辑器 PID 锁定，改用独立输出；未关闭用户进程。animation.stop 的现有 Cancel/Replaced 语义不承诺执行末帧清理；完整收尾使用 CompleteImmediately，P4 手册说明。

## P4 展示、回归与文档 — verified

提供可运行展示与手册，运行相关构建/测试，审查 diff；summary 逐项报告实现与限制。退出：第一批验收证据齐全，远期项明确保留。

实现：新增 project visuals install 显式补入命令，复用 ProjectApplyService 的候选验证、原哈希检查和作者文件事务；保留已有绑定，拒绝占用路径，支持 dry-run/重复执行。GameTestCase ending 接入首批效果、切焦、动作、两种集中线及转场，两个语言与两条路线共用；样例 shader 作为普通项目资产保存。手册和 spec 同步。

视觉复核修正：CPU contact sheet 暴露了稀疏景深网格重影与块状集中线；景深改为中心加 48 点圆盘采样，集中线改为连续细线和像素覆盖抗锯齿。128×128 定向线回归曾复现全透明，修正后通过。证据为 artifacts/visual-library-preview.png 及显式 WriteDefaultVisualPreview。真实 GPU 截图与性能专项未执行，不能把 CPU 视觉结果表述成 GPU 验收。

最终验证：Shared 完整 448 项通过；Presentation 完整 257 项通过；GameTestCase 集成 7 项通过，包含双语两路线及从实际 PAK 读取八个 shader 与遮罩图片；安装/CLI 帮助相关 13 项通过（包含于 Shared 完整集）。Builtins 306 项、桌面作者交互 9 项在 P3 通过。CLI 完整项目校验无 error；32 条现有开放 JSON 的 localization opaque/incomplete 警告保留，不宣称静态分析完全。原示例忽略的 Contracts DLL ABI 过期，重建并本地部署后验证；二进制不纳入提交。上次自动审批服务额度限制导致一次导出测试未执行，恢复后重跑通过。

格式：dotnet format whitespace GalNet.slnx --no-restore --include（本 feature 已跟踪与新建 C# 文件）--verify-no-changes 通过；git diff --check 通过。收尾仅修正本 feature 的空白格式，未改动无关代码。交付自审为 pass-with-follow-up；GPU 截图和性能专项保留为后续。
