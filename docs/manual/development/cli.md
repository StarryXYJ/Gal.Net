# 使用项目工具 CLI

CLI 复用 Editor 的项目服务，提供创建、只读查询、校验、编译、导出及游戏合包入口。从仓库根目录查看当前构建支持的命令：

```powershell
dotnet run --project src/Editor/GalNet.Editor.Headless -- help
dotnet run --project src/Editor/GalNet.Editor.Headless -- help --json
dotnet run --project src/Editor/GalNet.Editor.Headless -- help --search group --json
```

发行 Editor 内包含自包含 CLI 时可直接运行其入口，无需 dotnet SDK。`help` 不需要作者项目或契约；路由注册表生成帮助与机器 schema，参数以当前构建输出为准。

普通 help 输出文字；其他操作与错误使用协议版本 1 的 JSON，包含 operationId、success、data 和 diagnostics；失败返回非零退出码。结构查询可以在没有契约 DLL 时读取原始内容，schema/语义校验与编译则需要项目契约。

静态引用查询的 complete 为 false 时，不能把空结果解释为没有引用；自定义 C#、表达式和不透明结构可能超出分析能力。

常用任务分别见[外部工具创作](../editor/external-tools.md)、[内容导出](../editor/content-packaging.md)、[游戏合包](../publishing/game-distribution.md)。更详细的组件入口和边界见 [CLI README](../../../src/Editor/GalNet.Editor.Headless/README.md)。
