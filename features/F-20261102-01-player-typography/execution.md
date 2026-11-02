# 验证记录

2026-11-02，Windows / 本机默认 Debug 构建目录，无隔离 SDK、缓存或 worktree。

- `dotnet test test/GalNet.Core.Tests/GalNet.Core.Tests.csproj --no-restore`：156 passed。
- `dotnet test test/GalNet.Presentation.Tests/GalNet.Presentation.Tests.csproj --no-restore`：230 passed，TRX total 236 / executed 230。6 项 Explicit 未执行：共享 LibVLC 配置 1、加载期间隐藏/替换 2、真实视频 2、默认视觉预览 1。
- `dotnet test test/GalNet.IntegrationTests/GalNet.IntegrationTests.csproj --no-restore --filter <相关类型>`：最终 93 passed。完整类型列表见 summary。先前 51 项回归和 4 项初始字体集成也通过，不重复计入最终汇总。
- 初轮 Core 字号组合测试为 19 passed / 1 failed：测试把 `\skipC` 当成 skip，实际按既有边界规则保留字面文本；改用 `\skip C` 后通过。
- 初轮 Presentation 为 4 passed / 1 failed：字体测量缺 IFontManagerImpl，移至项目现有 Headless 集成会话后通过；纯单元回归保留字号/run/task 状态验证。
- 初轮集成代码有 3 个编译错误，分别为带可选第二参数的方法组不能直接转换 Action、IPlayerApplication 命名空间及 DialogueHistoryLine 必填 WasRead，修正后执行通过。
- 构建仍报告既有 AVLN3001（无公开默认构造的 GameShell/调试及项目 GamePage），不影响编译和相关实际页面挂载。
- 没有 Android/iOS 真机运行、原生桌面手工操作或全解决方案 CI；未做像素级前后对照。
- `python scripts/docs/sync_wiki.py check`：72 个文档页、导航、本地链接、标题锚点及 303 个仓库引用通过。`git diff --check` 通过。
