# Dock.Avalonia 集成设计文档

> 版本: 1.0  
> 对应代码: `GalNet.Editor\Dock\`、`GalNet.Editor\ViewModels\EditorPageViewModel.cs`、`GalNet.Editor\Views\EditorPageView.axaml`  
> NuGet: `Dock.Avalonia 12.0.0.2` · `Dock.Model 12.0.0.2` · `Dock.Model.Mvvm 12.0.0.2` · `Dock.Model.Avalonia 12.0.0.2` · `Dock.Avalonia.Themes.Fluent 12.0.0.2`

---

## 1. 概述

Dock.Avalonia 是一个面向 Avalonia UI 框架的停靠布局系统，支持：

- 可拖拽、拆分、组合的文档/工具面板
- 浮动窗口
- 布局序列化（JSON/XML/YAML/Protobuf）
- 多个 MVVM 框架（标准 MVVM、ReactiveUI、Prism）

在 GalNet Editor 中，Dock.Avalonia 承载编辑器的三个核心面板：**项目设置**、**编辑器设置**（作为工具面板）和**游戏预览**（作为文档面板），并提供了一个可扩展的 MVVM 框架供后续面板使用。

---

## 2. 架构概览

### 2.1 四层模型

```
┌─────────────────────────────────────────────────────────────┐
│  AXAML（View 层）                                            │
│  EditorPageView.axaml — DockControl Layout="{Binding}"     │
├─────────────────────────────────────────────────────────────┤
│  ViewModel 层                                                │
│  EditorPageViewModel — IRootDock Layout                      │
│  ProjectSettingsPanelViewModel                               │
│  EditorSettingsPanelViewModel                                │
│  GamePreviewPanelViewModel                                   │
├─────────────────────────────────────────────────────────────┤
│  Dock 模型层（Dock.Model.Core / Dock.Model.Mvvm）             │
│  Factory → RootDock → ToolDock/DocumentDock → Tool/Document │
├─────────────────────────────────────────────────────────────┤
│  Dock 控制层（Dock.Avalonia）                                  │
│  DockControl — 渲染 Dock 布局、处理拖拽/停靠                   │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 核心类型（Dock v12 API）

| 类型 | 命名空间 | 用途 |
|---|---|---|
| `Factory` | `Dock.Model.Mvvm` | 抽象基类，提供 `CreateLayout()` / `InitLayout()` |
| `IRootDock` | `Dock.Model.Core` | 布局根节点接口 |
| `RootDock` | `Dock.Model.Mvvm.Controls` | 根容器，包含 `ToolDock` 和 `DocumentDock` |
| `ToolDock` | `Dock.Model.Mvvm.Controls` | 工具面板容器（停靠在侧边） |
| `DocumentDock` | `Dock.Model.Mvvm.Controls` | 文档面板容器（占据中央区域） |
| `Tool` | `Dock.Model.Mvvm.Controls` | 单个工具面板（如项目设置） |
| `Document` | `Dock.Model.Mvvm.Controls` | 单个文档面板（如游戏预览） |
| `IDockable` | `Dock.Model.Core` | Dock 项统一接口，所有容器/面板均实现 |
| `DockControl` | `Dock.Avalonia` | AXAML 控件，接收 `IRootDock` 并渲染 |

---

## 3. 布局树结构

```
RootDock ("Root")
├── ToolDock ("Tools")           ← 侧边停靠，可折叠/拖拽
│   ├── Tool ("ProjectSettings") — Context = ProjectSettingsPanelViewModel
│   └── Tool ("EditorSettings")  — Context = EditorSettingsPanelViewModel
└── DocumentDock ("Documents")   ← 中央区域，可拖拽为标签页
    └── Document ("GamePreview") — Context = GamePreviewPanelViewModel
```

- `ToolDock` 默认停靠在左侧/右侧，用户可拖拽为浮动窗口
- `DocumentDock` 占据中央区域，多个文档以标签页形式切换
- 每个 `Tool`/`Document` 的 `Context` 属性持有该面板的 ViewModel 实例

---

## 4. 内容解析机制

Dock.Avalonia 不直接渲染 Tool/Document 的内容。内容通过 **DataTemplate** 解析：

### 4.1 注册 DataTemplate

在 `App.axaml` 的 `Application.DataTemplates` 中注册隐式 DataTemplate：

```xml
<Application.DataTemplates>
    <DataTemplate DataType="{x:Type vm:ProjectSettingsPanelViewModel}">
        <views:ProjectSettingsPanelView />
    </DataTemplate>
    <DataTemplate DataType="{x:Type vm:EditorSettingsPanelViewModel}">
        <views:EditorSettingsPanelView />
    </DataTemplate>
    <DataTemplate DataType="{x:Type vm:GamePreviewPanelViewModel}">
        <views:GamePreviewPanelView />
    </DataTemplate>
</Application.DataTemplates>
```

### 4.2 解析流程

```
Tool.Context = ProjectSettingsPanelViewModel
    ↓
DockControl 检测到 Context 不为 null
    ↓
查找 Application.DataTemplates 中匹配的 DataTemplate
    ↓
DataTemplate 匹配 ViewModel 类型
    ↓
创建 ProjectSettingsPanelView，设置 DataContext = ViewModel
    ↓
渲染到 Tool 的内容区域
```

> **关键点**：`Tool` 和 `Document` 在 v12 中没有 `Content` 属性。内容通过 `Context` 属性存储 ViewModel，再通过 Avalonia 的 DataTemplate 系统渲染。

### 4.3 主题

Dock 需要使用 `DockFluentTheme` 应用 Dock 控件样式：

```xml
<Application.Styles>
    <FluentTheme />
    <semi:SemiTheme Locale="zh-CN" />
    <dockFluent:DockFluentTheme />
</Application.Styles>
```

---

## 5. 工厂模式：EditorDockFactory

`EditorDockFactory` 负责创建 Dock 布局树。它继承自 `Dock.Model.Mvvm.Factory`，重写 `CreateLayout()`。

### 5.1 职责

- **DI 整合**：通过 `IServiceProvider` 注入 ViewModel 实例
- **布局定义**：构建 RootDock → ToolDock/DocumentDock → Tool/Document 的层级结构
- **内容绑定**：将 ViewModel 实例赋值给 Tool/Document 的 `Context` 属性

### 5.2 注册

```csharp
// App.axaml.cs
services.AddSingleton<EditorDockFactory>();
```

由于 `EditorDockFactory` 每次调用 `CreateLayout()` 时会从 DI 重新获取 ViewModel（单例），因此注册为 Singleton 是安全的。

### 5.3 生命周期

```
EditorPageViewModel 构造函数
    ↓
InitializeDock()
    ↓
_dockFactory.CreateLayout()     ← 创建完整的 IRootDock 布局树
_dockFactory.InitLayout(Layout)  ← 初始化 Dock 命令、事件等
    ↓
Layout 属性绑定到 AXAML 的 DockControl
```

---

## 6. ViewModel 集成架构

### 6.1 数据流

```
┌──────────────┐  Layout (IRootDock)  ┌───────────────┐
│ ViewModel    │ ──────────────────→  │ AXAML         │
│              │                      │               │
│ IRootDock    │                      │ DockControl   │
│ Layout       │                      │ Layout="{Binding Layout}"
└──────────────┘                      └───────────────┘
```

### 6.2 EditorPageViewModel 关键属性

| 属性 | 类型 | 说明 |
|---|---|---|
| `Layout` | `IRootDock?` | Dock 布局根节点，绑定到 AXAML 的 `DockControl.Layout` |
| `UndoCommand` | `ICommand` | 撤销命令（占位） |
| `RedoCommand` | `ICommand` | 重做命令（占位） |
| `SaveLayoutCommand` | `ICommand` | 保存窗口布局（占位） |
| `LoadLayoutCommand` | `ICommand` | 加载窗口布局（占位） |
| `ResetLayoutCommand` | `ICommand` | 重置为默认布局（占位） |

### 6.3 布局命令系

Dock v12 的 `Factory` 基类提供了以下内置命令（通过 `InitLayout` 自动激活）：

| 命令 | 类型 | 说明 |
|---|---|---|
| `Factory.AddDockable` | `ICommand` | 向容器中添加面板 |
| `Factory.RemoveDockable` | `ICommand` | 移除面板 |
| `Factory.SetActiveDockable` | `ICommand` | 激活指定面板 |
| `Factory.CloseDockable` | `ICommand` | 关闭面板 |
| `Factory.PinDockable` | `ICommand` | 固定/取消固定面板 |

这些命令在布局初始化后即可通过 `IDockable.Factory` 访问。

---

## 7. AXAML 绑定

### 7.1 DockControl

```xml
xmlns:dock="using:Dock.Avalonia"

<dock:DockControl Layout="{Binding Layout}" Background="#1E1E2E" />
```

- `Layout` 属性绑定到 `EditorPageViewModel.Layout`（`IRootDock?`）
- DockControl 自动渲染由 Factory 创建的布局树
- DockControl 内部使用 `DockContentPresenter` 查找 DataTemplate 进行内容渲染

### 7.2 状态栏

DockControl 下方有一个状态栏，绑定 `StatusText` 和 `ProjectName`：

```xml
<Border Grid.Row="1" Background="#181825" Padding="12,6">
    <TextBlock Text="{Binding StatusText}" />
    <TextBlock Text="{Binding ProjectName}" />
</Border>
```

---

## 8. NuGet 包依赖

```xml
<PackageReference Include="Dock.Avalonia" Version="12.0.0.2" />
<PackageReference Include="Dock.Avalonia.Themes.Fluent" Version="12.0.0.2" />
<PackageReference Include="Dock.Model" Version="12.0.0.2" />
<PackageReference Include="Dock.Model.Avalonia" Version="12.0.0.2" />
<PackageReference Include="Dock.Model.Mvvm" Version="12.0.0.2" />
```

各包的作用：

| 包 | 作用 |
|---|---|
| `Dock.Avalonia` | 核心控件 `DockControl` 和交互逻辑 |
| `Dock.Avalonia.Themes.Fluent` | Fluent 风格主题 |
| `Dock.Model` | 核心模型接口（`IDockable`, `IRootDock` 等） |
| `Dock.Model.Avalonia` | Avalonia 模型层（支持 `ItemsSource` 绑定） |
| `Dock.Model.Mvvm` | MVVM 基类（`Factory`、`Tool`、`Document` 等） |

---

## 9. 如何添加新面板

### 步骤 1：创建 ViewModel

```csharp
public class MyNewPanelViewModel : ObservableObject
{
    // 面板的业务逻辑和数据
}
```

在 DI 中注册：

```csharp
services.AddTransient<MyNewPanelViewModel>();
```

### 步骤 2：创建 View

```xml
<UserControl ... x:Class="GalNet.Editor.Views.MyNewPanelView"
             x:DataType="vm:MyNewPanelViewModel">
    <!-- 面板 UI -->
</UserControl>
```

### 步骤 3：注册 DataTemplate

在 `App.axaml` 的 `Application.DataTemplates` 中添加：

```xml
<DataTemplate DataType="{x:Type vm:MyNewPanelViewModel}">
    <views:MyNewPanelView />
</DataTemplate>
```

### 步骤 4：在 Factory 中添加面板

在 `EditorDockFactory.CreateLayout()` 中创建 Tool/Document 并设置 Context：

```csharp
var myVm = _serviceProvider.GetRequiredService<MyNewPanelViewModel>();
var myTool = new Tool
{
    Id = "MyNewPanel",
    Title = "我的新面板",
    Context = myVm
};

// 添加到现有 ToolDock
toolDock.VisibleDockables.Add(myTool);
```

---

## 10. 常见问题

### Q: Tool/Document 的 `Content` 属性不存在

**答**：Dock v12 移除了 `Content` 属性。使用 `Context` 属性存储数据对象（ViewModel），通过 DataTemplate 渲染。

### Q: 面板内容不显示

**答**：检查以下三点：
1. DataTemplate 已在 `Application.DataTemplates` 中注册
2. DataTemplate 的 `DataType` 与 `Tool.Context` 的运行时类型完全匹配
3. `DockFluentTheme` 已添加到 `Application.Styles`

### Q: 布局序列化如何实现？

**答**：Dock 支持多种序列化格式。安装 `Dock.Serializer.Newtonsoft` 或 `Dock.Serializer.SystemTextJson` 后，通过 Factory 的序列化方法保存/加载布局。

### Q: ToolDock 默认停靠在哪一侧？

**答**：`ToolDock` 的停靠位置由 `RootDock.VisibleDockables` 中与 `DocumentDock` 的相对位置决定。在当前的布局树中，`ToolDock` 在前、`DocumentDock` 在后，ToolDock 默认停靠在左侧。

---

## 11. API 速查

```csharp
// ── 创建布局 ──
public override IRootDock CreateLayout()
{
    var tool = new Tool { Id = "id", Title = "标题", Context = viewModel };
    var document = new Document { Id = "id", Title = "标题", Context = viewModel };
    
    var toolDock = new ToolDock
    {
        VisibleDockables = CreateList<IDockable>([tool]),
        ActiveDockable = tool
    };
    
    var docDock = new DocumentDock
    {
        VisibleDockables = CreateList<IDockable>([document]),
        ActiveDockable = document
    };
    
    return new RootDock
    {
        VisibleDockables = CreateList<IDockable>([toolDock, docDock]),
        ActiveDockable = docDock
    };
}

// ── 初始化 ──
var layout = factory.CreateLayout();
factory.InitLayout(layout);

// ── 绑定控件（AXAML） ──
// <DockControl Layout="{Binding Layout}" />
```

---

## 12. 参考链接

- [Dock.Avalonia GitHub](https://github.com/wieslawsoltes/Dock)
- [Document and Tool Content Guide](https://wieslawsoltes.github.io/Dock/articles/dock-content-guide.html)
- [Dock MVVM Guide](https://wieslawsoltes.github.io/Dock/articles/dock-mvvm.html)
- [Styling and Theming](https://wieslawsoltes.github.io/Dock/articles/dock-styling.html)
