# 第一轮：文件面板局部调整

2026-11-03。P2 进行中，本轮仅覆盖文件面板；不是完整 P2 验收。

- 缩略图非空时隐藏网格类型图标，无缩略图仍显示图标；保留加载、位图释放及列表视图机制。
- 文件右键菜单按剪贴板、新建/导入、重命名/删除和外部操作分组；右键已经选中的项目时不主动重置多选。
- `dotnet test test/GalNet.Editor.Tests/GalNet.Editor.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~ProjectFileBrowserMountedControlsNavigateSelectRenameCopyAndConfirmDelete|FullyQualifiedName~BrowserAddressBlocksDropAndGlobalSearchExitFollowSelection' --verbosity quiet`：2/2 通过；此前构建 Editor.Tests 成功。首次按文件名筛选未匹配测试，完整共享测试类运行被主动中止，不能计入通过。
- 未完成：透明图片实际像素/异步加载、真实右键多选、各菜单操作对象、其他工作区右键菜单和真实桌面视觉检查；这些仍是 P2 退出条件。
