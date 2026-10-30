# 用通用原语调用项目逻辑

`custom.invoke` 用两个字符串调用宿主注册的处理器。每个 action 不需要单独的参数 DTO 或项目专属 schema；项目 catalog 仍须包含内置 `BuiltinSchemas.CreateCustomActionsModule()`。`CreateAllModules()` 和 `AddBuiltinSchemas()` 已包含它。CLI、Editor 和 App 必须使用同一套契约，已有项目需重新构建并部署契约 DLL。

作者 `.rawgalgroup` 的条目示例：

```json
{
  "id": "set-score",
  "type": "custom.invoke",
  "parameters": {
    "action": "project.score",
    "argument": "12"
  }
}
```

`action` 必须是非空白字符串，按 `StringComparer.Ordinal` 精确匹配，不裁剪空白、不反射查找 CLR 方法。`argument` 必须显式提供字符串，可以为空；框架不解析、求值、本地化或替换其内容。项目可以自行使用 JSON、简单文本或其他语法。编辑器使用普通文本和多行文本字段，编译器只检查共享字段结构，不检查处理器是否注册。

## 注册同步处理器

```csharp
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;

Action<GalNet.Core.Primitives.PrimitiveCreateContext, string> setScore =
    (context, argument) => context.Runtime.SetVariable("score",
        int.Parse(argument, System.Globalization.CultureInfo.InvariantCulture));

var actions = new BuiltinCustomActionModule()
    .RegisterImmediate("project.score", setScore, catchUp: setScore)
    .RegisterImmediate("project.notify", argument => ShowNotification(argument));

using var registration = new GameRegistration();
var view = registration.AddBuiltins(customActions: actions).BuildView();
```

上例 `ShowNotification` 是项目自己的方法。只需要参数时使用 `Action<string>` 重载；需要变量、状态或当前条目时，使用 `Action<PrimitiveCreateContext, string>` 重载。资源管理器、presenter 等宿主服务可以由委托捕获，所有权与释放仍由原宿主管理。

每次调用只选择一个委托：

| 初始执行模式 | 调用行为 |
| --- | --- |
| Normal | `execute`，注册时必填 |
| Quick | `quick`；省略时调用 `execute` |
| CatchUp | `catchUp`；省略时不执行并立即完成 |

`RegisterImmediate` 同步执行，非阻塞、不可跳过。CatchUp 默认空操作是此模块便利入口的策略，与通用 `PrimitiveRegistry.Immediate` 默认重做 dispatch 不同。需要追帧重建逻辑状态时必须提供 `catchUp`；一次性副作用通常保持空操作。

## 异步与完整实例

一次异步操作使用 `RegisterAsync`，委托类型为 `Func<string, CancellationToken, Task>` 或带 `PrimitiveCreateContext` 的重载：

```csharp
var actions = new BuiltinCustomActionModule()
    .RegisterAsync("project.prepare", (argument, token) => PrepareResourceAsync(argument, token),
        blocking: true,
        quick: (argument, token) => PrepareResourceAsync(argument, token));
```

`PrepareResourceAsync` 是宿主提供的方法。异步便利入口沿用上述模式选择，`blocking` 默认 false；委托的 token 联合游戏运行 scope 与模块生命周期，Task 必须包含资源清理的完成过程。Engine 等待阻塞实例完成后继续，未处理异常进入 Engine 的失败路径。模块通过 `PrimitiveTaskScope` 跟踪便利入口的异步操作；宿主停止 Engine 后应 `await view.DisposeAsync()`，取消并等待全部操作清理，未处理失败在异步释放时聚合传播。不要使用 `async void` 或把后台任务丢弃。

运行期间切换 Quick/CatchUp 不会重新调用另一套委托，也不会自动结束正在等待的操作。处理器应响应取消；需要在模式切换时加速、结束操作，或支持玩家跳过时，使用 `RegisterFactory(id, (context, argument) => new MyPrimitiveInstance(...))`。每次返回新实例并保留 `context.BatchId`，副作用放在实例 dispatch 中。完整实例自行管理 Normal/Quick/CatchUp、模式切换、跳过与完成语义，异步任务所有权和资源清理也由其项目模块/宿主管理；便利入口的默认模式策略与任务跟踪不覆盖它。

## 注册与错误

创建 view 时冻结 action 注册；重复 ID、空白 ID 和冻结后注册会报错。模块属于一个游戏 view，不能复用到另一个 view。`AddBuiltins(customActions: actions)` 或 `AddBuiltinImplementations(catalog, customActions: actions)` 会接收该模块所有权；部分组合可用 `AddBuiltinCustomActions(actions)`，不要再重复注册同一条执行指令。

未注册 action 在所有模式下记 warning，包含 action、Group 和条目身份，不包含 argument，然后作为已完成的非阻塞空操作继续。`new BuiltinCustomActionModule(strictMissingActions: true)` 可将这种情况升级为异常。已注册处理器或工厂抛出的异常始终正常传播。

注册表、委托、Task、服务和资源租约不进入存档。需要持久化的数据仍通过游戏变量或其他模块的 `PrimitiveRegistry.State/Restore/ValidateState` 声明与恢复，数据修改遵守 `GameState.Update` 边界。

Sample 的调试定位不会自动允许 `custom.invoke`：单个 wire ID 背后可以有任意行为。项目确认所有相关 action 的追帧语义后，才能在自己的调试策略中为该指令声明 Scene 或 Ignore；当前调试策略没有按 action 字符串单独授权的能力。
