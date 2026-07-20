# Dock.Avalonia 集成设计文档

> 版本: 2.0  
> 对应代码: `GalNet.Editor\Dock\`  
> NuGet: `Dock.Avalonia` · `Dock.Model` · `Dock.Model.Mvvm` · `Dock.Avalonia.Themes.Fluent`

---

## 1. 概述

Dock.Avalonia 是一个面向 Avalonia UI 框架的停靠布局系统，支持：

- 可拖拽、拆分、组合的文档/工具面板
- 浮动窗口（原生窗口模式）
- 自定义布局序列化（`DockLayoutSerializer`）
- 扩展面板注册机制（`IEditorExtensionRegistry`）

在 GalNet Editor 中，Dock.Avalonia 承载编辑器的核心面板：**节点图编辑器**、**游戏预览**、**资源面板**、**日志面板**、**属性检查器**、**UI 定制**等，并通过 `EditorDockFactory` + `BuiltInDockContributions` 提供可扩展的面板注册机制。

---

## 2. 架构概览

### 2.1 分层模型

```
┌──────────────────────────────────────────────────────────────────┐
│  AXAML（View 层）                                                 │
│  EditorPageView.axaml — DockControl Layout="{Binding Layout}"   │
├──────────────────────────────────────────────────────────────────┤
│  ViewModel 层                                                     │
│  EditorPageViewModel — 管理 IRootDock Layout                     │
│  各面板 ViewModel（EditorWorkspaceViewModel, GamePreviewPanel...）│
├──────────────────────────────────────────────────────────────────┤
│  Dock 工厂层                                                      │
│  EditorDockFactory — 创建/恢复布局树、管理面板生命周期             │
│  BuiltInDockContributions — 注册内置面板到 IEditorExtensionRegistry│
│  DockLayoutSerializer — 自定义 JSON 布局序列化                   │
│  DockInspectorCoordinator — 协调 Inspector 面板与当前激活面板    │
├──────────────────────────────────────────────────────────────────┤
│  Dock 模型层（Dock.Model.Core / Dock.Model.Mvvm）                 │
│  Factory → RootDock → ProportionalDock → DocumentDock → Document │
├──────────────────────────────────────────────────────────────────┤
│  Dock 控制层（Dock.Avalonia）                                      │
│  DockControl — 渲染 Dock 布局、处理拖拽/停靠                      │
└──────────────────────────────────────────────────────────────────┘
```

### 2.2 核心类型

| 类型 | 命名空间 | 用途 |
|---|---|---|
| `Factory` | `Dock.Model.Mvvm` | 抽象基类，提供 `CreateLayout()` / `InitLayout()` |
| `IRootDock` | `Dock.Model.Core` | 布局根节点接口 |
| `RootDock` | `Dock.Model.Mvvm.Controls` | 根容器 |
| `ProportionalDock` | `Dock.Model.Mvvm.Controls` | 按比例分割的容器（支持水平/垂直） |
| `DocumentDock` | `Dock.Model.Mvvm.Controls` | 文档面板容器（标签页形式） |
| `Document` | `Dock.Model.Mvvm.Controls` | 单个文档面板 |
| `IDockable` | `Dock.Model.Core` | Dock 项统一接口 |
| `DockControl` | `Dock.Avalonia` | AXAML 控件，接收 `IRootDock` 并渲染 |

---

## 3. 布局树结构

当前编辑器布局采用 `ProportionalDock` 实现三层分割：

```
RootDock ("Root")
└── ProportionalDock ("Main", Horizontal)
    ├── ProportionalDock ("Center", Vertical, Proportion=0.68)
    │   ├── DocumentDock ("Documents")           ← 中央主区域
    │   │   ├── Document ("NodeGraph")           ← 节点图编辑器
    │   │   ├── Document ("GamePreview")          ← 游戏预览
    │   │   └── ... (其他默认/用户打开的面板)
    │   ├── ProportionalDockSplitter
    │   └── DocumentDock ("LogDocuments")         ← 底部区域
    │       ├── Document ("Log")                  ← 日志面板
    │       ├── Document ("Assets")               ← 资源面板
    │       ├── Document ("UiCustomization")      ← UI 定制面板
    │       └── ... (其他底部面板)
    ├── ProportionalDockSplitter
    └── DocumentDock ("InspectorDocuments", Proportion=0.32)  ← 右侧检查器
        └── Document ("Inspector")               ← 属性检查器
```

- `ProportionalDock` 按比例分割，用户可拖拽分割条调整
- 各 `DocumentDock` 内以标签页形式切换文档
- 面板可拖拽为浮动窗口（使用 `UrsaDockHostWindow` 原生窗口）
- 每个 `Document` 的 `Context` 属性持有该面板的 ViewModel 实例

---

## 4. 面板扩展机制

### 4.1 面板注册

面板通过 `IEditorExtensionRegistry.RegisterDockPanel()` 注册，由 `BuiltInDockContributions` 在启动时调用：

```csharp
registry.RegisterDockPanel(new DelegateDockPanelContribution(
    panelId: "NodeGraph",
    titleKey: "Dock.Panel.NodeGraph",
    placement: DockPanelPlacement.MainDocument,
    isGlobal: true,
    canClose: true,
    canFloat: true,
    isDefaultPanel: true,
    showInViewMenu: true,
    createViewModel: (sp, _) => sp.GetRequiredService<EditorWorkspaceViewModel>(),
    viewType: typeof(NodeGraphPanelView),
    inspector: new DelegateInspectorContribution(...)
));
```

### 4.2 面板属性

| 属性 | 说明 |
|---|---|
| `PanelId` | 唯一面板标识 |
| `TitleKey` | 本地化标题键 |
| `Placement` | 默认停靠位置：`MainDocument` / `BottomDocument` / `InspectorDocument` |
| `IsGlobal` | 全局唯一（不可重复打开） |
| `CanClose` | 允许关闭 |
| `CanFloat` | 允许拖出为浮动窗口 |
| `IsDefaultPanel` | 启动时默认显示 |
| `ShowInViewMenu` | 在 View 菜单中显示 |
| `Inspector` | 关联的 Inspector 面板（可选） |

### 4.3 内置面板列表

| PanelId | 位置 | 全局 | 默认 | 说明 |
|---|---|---|---|---|
| `NodeGraph` | MainDocument | 是 | 是 | 节点图编辑器 |
| `GamePreview` | MainDocument | 是 | 是 | 游戏预览 |
| `Assets` | BottomDocument | 是 | 是 | 资源管理 |
| `Log` | BottomDocument | 是 | 是 | 日志面板 |
| `GroupEditor` | MainDocument | 否 | 否 | 组编辑器（按需打开） |
| `Inspector` | InspectorDocument | 否 | 是 | 属性检查器 |
| `UiCustomization` | BottomDocument | 是 | 是 | UI 定制面板 |

---

## 5. 内容解析机制

Dock.Avalonia 不直接渲染 Document 的内容。内容通过 `DockViewLocator`（自定义 `IDataTemplate`）解析：

```
Document.Context = ViewModel 实例
    ↓
DockControl 检测到 Context 不为 null
    ↓
DockViewLocator 根据 ViewModel 类型查找对应的 View
    ↓
通过 BuiltInDockContributions 中注册的 viewType 创建 View
    ↓
设置 DataContext = ViewModel，渲染到 Document 内容区域
```

`DockViewLocator` 实现了 `IDataTemplate`，在 `App.axaml` 中注册为全局 DataTemplate。它通过 `IEditorViewFactory` 创建 View 实例，支持 DI 注入。

---

## 6. 工厂模式：EditorDockFactory

`EditorDockFactory` 继承自 `Dock.Model.Mvvm.Factory`，负责创建和管理 Dock 布局树。

### 6.1 职责

- **DI 整合**：通过 `IServiceProvider` 管理面板 ViewModel 生命周期
- **布局创建**：`CreateLayout()` 构建完整的 ProportionalDock 布局树
- **布局恢复**：`PrepareRestoredLayout()` 从序列化数据恢复布局，重建面板 ViewModel
- **面板操作**：`OpenPanel()` / `ToggleGlobalPanel()` / `OpenGroupEditor()` / `CloseGroupEditor()`
- **Inspector 协调**：通过 `DockInspectorCoordinator` 管理 Inspector 面板与当前激活面板的联动
- **本地化**：监听 `IEditorLocalizationService` 变更，自动刷新面板标题

### 6.2 生命周期

```
EditorPageViewModel 构造
    ↓
尝试从 EditorSettings.LastDockLayout 恢复布局
    ↓ (恢复失败或首次启动)
EditorDockFactory.CreateLayout()     ← 创建默认布局树
EditorDockFactory.InitLayout(Layout)  ← 初始化 Dock 命令、事件
    ↓
Layout 属性绑定到 AXAML 的 DockControl
    ↓
用户操作 → DockableAdded/Removed/Moved → LayoutChanged 事件
    ↓
保存时 → DockLayoutSerializer.Serialize() → 持久化到 EditorSettings
```

### 6.3 事件

| 事件 | 说明 |
|---|---|
| `LayoutChanged` | 面板创建、关闭、移动或激活时触发 |
| `ActiveDockableChanged` | 激活面板变更时更新 Inspector |
| `DockableAdded/Removed/Moved` | 面板增删移时触发 |

---

## 7. 布局序列化

`DockLayoutSerializer` 提供自定义 JSON 布局序列化，不依赖 `Dock.Serializer` 包。

- 只持久化布局结构数据（ID、位置、比例、方向等），不保存运行时引用（Context、Owner、Factory）
- 支持浮动窗口布局的保存和恢复
- 使用 `JsonNumberHandling.AllowNamedFloatingPointLiterals` 处理 NaN/Infinity
- 格式版本号为 1，反序列化时校验版本兼容性

```csharp
var serializer = new DockLayoutSerializer();
string json = serializer.Serialize(layout);       // 保存
IRootDock? restored = serializer.Deserialize(json); // 恢复
```

---

## 8. AXAML 绑定

```xml
xmlns:dock="using:Dock.Avalonia"

<dock:DockControl Layout="{Binding Layout}" />
```

- `Layout` 属性绑定到 `EditorPageViewModel.Layout`（`IRootDock?`）
- DockControl 自动渲染由 Factory 创建的布局树

---

## 9. 添加新面板

### 步骤 1：创建 ViewModel

```csharp
public class MyNewPanelViewModel : ViewModelBase
{
    // 面板的业务逻辑和数据
}
```

### 步骤 2：创建 View

```xml
<UserControl ... x:Class="GalNet.Editor.Views.MyNewPanelView"
             x:DataType="vm:MyNewPanelViewModel">
    <!-- 面板 UI -->
</UserControl>
```

### 步骤 3：注册面板

在 `BuiltInDockContributions.Register()` 中添加：

```csharp
registry.RegisterDockPanel(new DelegateDockPanelContribution(
    "MyNewPanel", "Dock.Panel.MyNewPanel",
    DockPanelPlacement.MainDocument, // 或其他位置
    isGlobal: true, canClose: true, canFloat: true,
    isDefaultPanel: false, showInViewMenu: true,
    (sp, _) => sp.GetRequiredService<MyNewPanelViewModel>(),
    typeof(MyNewPanelView),
    inspector: null // 或关联 Inspector
));
```

### 步骤 4：注册 DI

```csharp
services.AddTransient<MyNewPanelViewModel>();
```

---

## 10. 常见问题

### Q: 面板内容不显示

**答**：检查以下三点：
1. `DockViewLocator` 已在 `Application.DataTemplates` 中注册
2. `BuiltInDockContributions` 中面板的 `viewType` 正确
3. ViewModel 已注册到 DI 容器

### Q: 如何自定义面板停靠位置？

**答**：通过 `DockPanelPlacement` 枚举指定：
- `MainDocument` — 中央主区域
- `BottomDocument` — 底部日志/资源区域
- `InspectorDocument` — 右侧检查器区域

### Q: 浮动窗口使用什么实现？

**答**：使用 `UrsaDockHostWindow`（基于 Ursa 的原生窗口），通过 `RootDock.FloatingWindowHostMode = DockFloatingWindowHostMode.Native` 配置。

---

## 11. 参考链接

- [Dock.Avalonia GitHub](https://github.com/wieslawsoltes/Dock)
- [Document and Tool Content Guide](https://wieslawsoltes.github.io/Dock/articles/dock-content-guide.html)
- [Dock MVVM Guide](https://wieslawsoltes.github.io/Dock/articles/dock-mvvm.html)