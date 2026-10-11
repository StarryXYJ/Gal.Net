# DynamicLocalization Avalonia package must match the host Avalonia major version

状态：confirmed

来源 Feature：F-20261010-02-runtime-i18n

## 现象

`DynamicLocalization.Avalonia` 2.1.0 的 NuGet manifest 为 net6.0 至 net9.0 target groups 声明了 `Avalonia (>= 11.0.0)`，其 assembly 在载入时要求 `Avalonia.Markup.Xaml, Version=11.0.0.0`。GalNet 的 Avalonia Sample 已使用 Avalonia 12.0.5，因此不能直接采用该版本的 XAML markup extension。2.1.2 虽将依赖范围更新为 `Avalonia [11.0.0, 13.0.0)`，但运行时仍调用 Avalonia 12 已移除的 `Binding(string, BindingMode)` 构造函数。

## 根因

该包的 2.1.0 release 尚未针对 Avalonia 12 编译；Core package 是平台无关的，不具有此 Avalonia assembly dependency。2.1.2 的包元数据虽然允许 Avalonia 12，但实际 `LocalizeExtension` 仍为旧 `Binding` API 编译，因此 restore 和 build 都不能证明可启动。

## 正确做法

保留 Avalonia 12，不为使用 markup extension 降级全仓 Avalonia。Sample 只保留 `DynamicLocalization.Core` 的 `CultureService`，自有 extension 按官方 demo 绑定 Core 的 `LocalizedString.Value`，将平台 binding 换成 Avalonia 12 `ReflectionBinding`。`LocalizedString` 监听 culture/provider change，不能只在自己的 ApplyCulture wrapper 中发通知，否则外部直接改 culture 时不会更新。任何未来重新引入第三方 extension 的方案都必须做真实启动验证。

## 适用范围

任何将 `DynamicLocalization.Avalonia` 引入当前 Avalonia 12 GalNet 应用的 feature。

## 不要做什么

不要仅因 package 可 restore 就假定其 XAML extension 可与当前 Avalonia major 运行，也不要为单个本地化功能降级共享 Avalonia 依赖。

## 证据

- 用户于 2026-10-10 报告的 Sample 启动堆栈：`LocalizeExtension.ProvideValue` 调用缺失的 `Avalonia.Data.Binding..ctor(string, BindingMode)`。
- `GalNet.Sample.Avalonia` 在移除 `DynamicLocalization.Avalonia`、保留 Core 并改用自有适配层后成功构建；输出目录不再有 `DynamicLocalization.Avalonia.dll`。
