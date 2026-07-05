# GalNet 文档

GalNet 是一套 Galgame 制作与运行工具，分为 **编辑器**（开发者制作用）和 **启动器**（玩家用）。

## 快速概览

```
┌──────────────────────────────────────────┐
│  Editor  /  Launcher                     │  壳层
├──────────────────────────────────────────┤
│  Editor.Control  /  Control              │  UI 控件层
├──────────────────────────────────────────┤
│  Runtime  /  Assets                      │  引擎层
├──────────────────────────────────────────┤
│  Core                                    │  数据模型 + 接口
└──────────────────────────────────────────┘
```

### 核心概念

| 概念 | 说明 |
|---|---|
| **Graph** | 一张游戏一张图，节点 + 边构成流程 |
| **Group** | 线性内容序列，包含条目列表 |
| **Branch** | 选项分支 / 条件分支 |
| **Entry** | 条目，分 ComplexEntry（开发期）→ SimpleEntry（运行时）两层 |
| **Layer** | 背景和立绘的统一抽象（id/asset/x/y/z/visible） |
| **IGameView** | Runtime 与 UI 的唯一契约接口 |
| **GameRuntime** | 运行时状态容器（位置/变量/场景/调用栈） |
| **EntryHandler** | 条目执行逻辑的扩展点 |

### 运行时流程

```
Graph → 编译 Group 条目 → 状态机循环 → 组执行完 → 沿边转移
                                      ↓
                               Handler.Start(ctx)
                               → IsBlocking? → 等待/打断 → Complete
```

### 文件格式管道

```
开发期: graph.json + *.galgroup
  → 编译: ComplexEntry → SimpleEntry
  → 导出: .galnet (游戏逻辑单文件)
  → 打包: .galpak (.galnet + 资源文件)
```

## 详细手册

| 手册 | 内容 |
|---|---|
| [架构参考](spec/architecture.md) | 技术栈、项目分层、领域模型、扩展点设计 |
| [运行时参考](spec/runtime.md) | GameRuntime、GameEngine、GameSnapshot、VariableStore、ExpressionEvaluator |
| [条目类型参考](spec/entry-types.md) | 全部条目类型定义、参数表、速查表 |
| [i18n 国际化](spec/i18n.md) | 三层键结构、键生成规则、JSON 示例 |
| [资源管理](spec/assets.md) | 包格式、架构分层、Scene 系统、存档结构 |
| [文件格式](spec/file-formats.md) | graph.json、.galgroup、.galnet、.galpak、编译管道 |

## 开发计划

详见 [dev/phase-plan.md](../dev/phase-plan.md)。
