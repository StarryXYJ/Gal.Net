# GalNet 架构

GalNet 把“游戏逻辑”与“如何显示、从哪里读数据、如何保存玩家数据”分开。`Core` 只包含领域模型和协议；`Runtime` 解释图和条目；`Control` 是 Avalonia 的一组默认实现；编辑器、启动器和未来的独立宿主只负责选择并组合服务。

## 分层与组合根

```
Editor / Launcher / future standalone host
        │ supplies content, profile and UI project services
        ▼
Control (Avalonia templates, router, media adapters)
        ▼
Runtime (GameEngine, handlers, snapshots)
        ▼
Core (graph, variables, UI definitions and contracts)
```

`IGameContentProvider` 是游戏内容入口。它返回图、资源根和 `UiProject`，因而编辑器预览可以从内存或临时预览目录构建内容，启动器可以从解压包读取，独立宿主也可以读取 exe 同目录目录包。运行流程不再自行猜测数据目录或写入样例游戏文件。

玩家数据由宿主提供：`ISettingsService`、`ISaveService`、`IVariableService` 和 `IGameProgressService`。默认文件实现属于宿主组合根；游戏流程只消费接口。

## UI 项目

每个游戏项目有下列可随游戏导出的目录：

```
UI/
  ui.json                  # 版本、颜色、类别默认视图
  WidgetInstance/*.json    # 控件实例
  ScreenInstance/*.json    # 视图实例
```

`ui.json` 的 `DefaultViews` 将类别键映射到视图实例。内置键为 `title`、`game`、`settings`、`save-load` 和 `gallery`。缺少 `UI/ui.json` 的旧项目会在首次打开时物化一组默认实例。

控件和视图都是三层模型：类别描述最小行为；代码模板定义可配置项、默认值、校验和构建行为；项目实例通过 `TemplateId` 引用模板。视图对控件的引用由模板定义的配置项表达，不拥有控件，所以同一控件实例可以在多个视图中使用。项目不加载任意 XAML 或任意 Avalonia 类型。

## 颜色与导航

颜色是 `ui.json` 中的键值表。模板声明默认颜色键，实例可覆盖为另一个颜色键或 `#AARRGGBB` 字面量。`IColorPalette` 提供解析和变更通知，渲染实现应直接绑定调色板而非复制颜色值。编辑器的颜色面板删除前必须检查控件和视图配置中的引用。

游戏页面不使用编辑器的 `INavigationService`。`IGameScreenRouter.NavigateAsync(categoryKey)` 按类别键解析当前 UI 项目的默认视图，并由宿主提供对应的视图/VM 工厂。新增类别时，扩展注册模板和路由工厂；替换内置菜单只需把同类别的默认视图改为另一个模板实例。

## 开发与迁移

实现宿主时，先注册内容提供者和玩家数据服务，再注册默认或自定义控件/视图模板及游戏路由。编辑器预览使用项目 `.galnet/player` 作为测试档案，但内容与 UI 可以来自未保存的工作区状态。

本轮迁移移除了游戏流程中 `IGameDataProvider` 的目录依赖、样例数据回退和临时创建存档服务的行为。使用旧流程的宿主需要改为提供 `IGameContentProvider`，并在 `GameFlowOptions` 中显式传入存档/进度服务（或从其组合根 DI 提供）。

## 当前未完成项

以下内容是已预留协议或目录结构、但尚未作为完整产品能力交付的部分：

- **控件与视图实例编辑器**：目前可持久化、校验、加载并按类别路由 UI 实例；编辑器只提供颜色面板，尚未提供控件配置项、视图实例、控件引用和布局的可视化编辑器。
- **模板实际替换渲染**：内置 Screen 模板已由注册表负责分派，后续继续把各 Screen/Widget 的实例配置和组合完全迁入模板构建器。
- **颜色引用分析范围**：颜色面板会保护控件实例显式 `ColorOverrides` 中的引用；模板默认值和未来视图配置中的颜色引用还需要纳入统一的引用分析器。
- **独立宿主与启动器**：接口支持目录包、编辑器预览和未来归档包，但本轮没有新增独立 EXE、用户/游戏管理 UI，也没有实现 `.pak` 直接启动。
- **包导出与迁移工具**：旧项目会在打开时生成默认 `UI/`；尚无命令行批量迁移、UI 格式升级器或把 UI 目录加入导出包的完整流水线。
- **自动化覆盖**：现有 Runtime、资产和编辑器测试已通过；UI 项目持久化测试已添加，但旧的独立 `GalNet.Control.Tests` 工程仍有既有 `WidgetTemplate`/`WidgetConfig` 编译缺口，需先修复后纳入 CI。
