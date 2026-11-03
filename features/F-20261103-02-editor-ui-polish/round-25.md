# 第二十五轮：默认视觉资源直接选择

2026-11-03。天气纹理映射的值复用图片资源选择器，默认视觉程序映射的值复用效果程序选择器；用途键及作者数据格式不变。打开时保留已有 ID，只在用户选择后更新项目草稿，仍可撤销；无效/暂不可解析的 ID 不因打开界面自动重写。

挂载测试验证天气图片和视觉程序选择器类型、已有 ID 保留、修改后提交与撤销；连同音频设置测试 2/2 通过。第二次测试遇默认 obj 中 PDB 被另一 .NET 构建占用，用户已允许现有 out 目录，因此使用 `--artifacts-path out/editor-ui-verification -p:RestoreLockedMode=true` 和本机缓存进行验证；首次尝试 `--locked-mode` 不被 dotnet test 接受，改用 MSBuild 属性后通过。未复制 SDK 或 NuGet 缓存。
