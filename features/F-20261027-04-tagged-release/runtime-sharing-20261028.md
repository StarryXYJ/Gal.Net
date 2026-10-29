# Editor/CLI 运行时共享与精简发行

用户授权：Editor 与 CLI 避免重复运行时；额外分发要求用户安装 .NET 的 Editor/Sample。VLC 仅考虑容易裁减的部分，不引入复杂构建维护。

## 实现

- 两个应用先独立 publish，再合并到同一应用目录。重名发布文件必须 SHA256 一致，否则失败；不覆盖依赖，不依靠删除 CLI 运行时后搜索父目录的未定义行为。CLI 入口调整为根目录 `GalNet.Editor.Headless.exe`，Skill 为根目录 `Skills/galnet-project-authoring`。
- `Publish.ps1 -Deployment SelfContained/FrameworkDependent` 控制部署方式，默认保持完整版。两种方式均保留 R2R、win-x64 原生依赖与契约 IL 身份规则。
- `Release.ps1` 默认生成六个应用 ZIP 和一个两种方式共享的 Contracts ZIP；精简应用名追加 `-framework-dependent`，README 写明 .NET 10 x64 Runtime 要求和下载入口。
- 验收涵盖两种方式的 manifest/hash、运行时份数、runtimeconfig、所有入口的 R2R、契约闭包、解压 CLI 的 help/创建项目/schema 查询。可选 DesktopSmoke 增加 Editor 初始化及实际加载的 runtime 路径检查，保留 Sample 会话初始化。
- VLC 暂不追加裁剪。Gallery 缩略图截图、视频/音频播放及 Editor 预览有不同依赖，现阶段保留已有 x64 插件集合。

## 验证

本地候选已封存：`out/runtime-sharing/0.1.0-dev.30`，七个 ZIP、SHA256SUMS、release-index.json。发布保护七项、脚本语法及改动文件的 whitespace 检查通过。

| ZIP | 完整版 MiB | Framework-dependent MiB |
| --- | --- | --- |
| Editor（含 CLI） | 111.95 | 76.80 |
| Sample | 107.36 | 72.20 |
| Sample-GameTestCase | 137.71 | 102.55 |

Contracts ZIP 为 0.35 MiB，两种部署共用。与上次 Editor 149.66 MiB 的候选相比，共目录发布后约减少 37.71 MiB；跨版本源码可能有其他小变化，不能将差值全部当作精确运行时大小。

`out/runtime-sharing-smoke/app-validation.json` 为 passed=true、desktopStartup=true，包含两种部署。全部入口均有 R2R 标记、跨模式玩家契约闭包字节一致、完整版每包只有根目录一份 coreclr，精简版不含 coreclr/hostfxr/hostpolicy/System.Private.CoreLib。两种分发 CLI 均通过 help、实际创建项目和 schema 查询；两种 Sample 的独立 profile 日志均含 Session initialized。

实际进程模块：完整版 Editor 从解压目录根加载 coreclr.dll；精简版从 `C:/Program Files/dotnet/shared/Microsoft.NETCore.App/10.0.12/coreclr.dll` 加载。Editor 只验证初始化和 runtime 来源，Sample 只验证会话初始化，不代表所有 UI 或完整剧情验收。

首次 Release 在精简版 CLI 创建临时目录时出现一次 Access denied，未封存最终目录。相同 CLI、相同路径独立重试成功；未修改产品代码或加入自动重试。随后在独立短目录对原七个 ZIP 重新进行完整分发与 DesktopSmoke 验证并通过。使用 `out/runtime-sharing-resume.ps1` 调用 Release.ps1 的原始封存尾段生成最终索引，未重新编译或修改 ZIP；原始失败日志保留在 `.work/.../app-validation/logs/`。访问拒绝的具体原因未证实。

工作区另有并行 content-packaging 源码改动，未覆盖这些改动；这是 dirty 本地 Apps 候选，未重复构建或验收 NuGet，不宣称正式 tag 发布。构建仍有既有分析器与 Avalonia 警告。

这次只生成本地候选，不上传、不创建 tag，不代表跨平台发行已验证。
