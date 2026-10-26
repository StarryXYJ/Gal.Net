# 阶段计划

## P1 效果资源与默认项目部署 — verified

补齐 Bloom / Blur / Vignette / ColorGrade / DepthBlur / SpeedLines / Reveal 等资源、项目用途绑定和默认模板部署。模板可内嵌于编辑器，部署后作为项目资产使用，不要求全项目固定 GUID。验证 metadata、SkSL 编译、项目 staging 与 dry-run，以及已有内容补入时保留修改。退出：资源能加载且默认项目获得可编辑源文件和指向自身资产的默认绑定。

结果：8 个 shader（含 MaskReveal）嵌入 Editor.Shared 初始化模板；默认项目创建将其复制为普通项目资产并生成自身 GUID。新增 `galnet.project.defaultVisualPrograms`；纯自定义契约和无契约 scaffold 不注入资源。准备计划克隆配置、保留已有绑定及未知 section，重复准备不替换已绑定资产。已有项目的用户入口留到 P4 集成，不将 Prepare API 宣称为已上线的补入命令。

验证：ProjectCreationTests + DefaultVisualTemplateTests 共 9 项通过；DefaultVisualShaderTests 共 8 项通过（metadata 及 SKRuntimeEffect 实际编译）。Editor.Shared 串行构建 0 警告/0 错误（RunAnalyzers=false）。普通多进程构建曾无诊断退出，串行构建通过；受限环境 testhost 连接超时，沙箱外相同测试通过，未将该超时归因为业务代码失败。尚不宣称 DepthBlur 的 layerZ 注入或 MaskReveal 的纹理绑定完成，二者属于 P2。

## P2 渲染能力 — planned

注入图层 Z；补齐 texture 参数租约和绑定。验证景深在 Z 变化后更新、遮罩真实采样、alpha 与资源释放。退出：CPU 像素证据与已有渲染回归通过；GPU 路径共享绑定代码并构建通过。

## P3 转场与演出组合 — planned

实现 catalog 第一批组合，带 schema、边界校验、临时资源清理和作者分析；不为每种 shader 新增效果 Entry。编辑器从项目用途绑定填充显式资源参数，编译器只传递 Entry 上的引用。验证缺失绑定、手动替换、项目绑定变更不追改旧 Entry、CLI 显式参数、shader 契约匹配与生成事件的资源依赖。验证展开、末帧、短时长、非法参数、不同播放 handle、快进/停止行为。退出：推荐第一批可调用且编译结果通过现有时间线验证，导出包含所需 shader 与纹理。

## P4 展示、回归与文档 — planned

提供可运行展示与手册，运行相关构建/测试，审查 diff；summary 逐项报告实现与限制。退出：第一批验收证据齐全，远期项明确保留。
