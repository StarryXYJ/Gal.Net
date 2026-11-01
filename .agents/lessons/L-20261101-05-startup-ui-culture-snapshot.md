# 在本地化初始化前保存系统 UI 语言

- 状态：confirmed
- 来源：Sample 打包版开场与首次语言选择修复（独立修复）
- 范围：App、StartupLocalization、AvaloniaPlayerStartup、开场声明

现象：脚本运行通常已有中文 profile 且准备较快，打包版却显示英文声明或默认选中英文。仅检查最终首页语言会漏掉问题。

根因：DynamicLocalization CultureService.SetCulture 会改变线程的 CurrentUICulture。创建默认英文服务后再查询系统语言已太晚；首次 onboarding 又传入游戏默认语言，声明还曾保存创建时的一次性字符串。回归 FirstPickerUsesSystemLanguageAndLocalizedTextBeforeConfirmation 曾明确失败：预期 zh-CN、实际 en-US。

正确做法：App 在创建本地化服务前捕获系统 UI 语言；内容准备前只读已有 profile 语言，有效保存值优先，否则匹配系统语言、再用项目回退。onboarding 使用已经初始化的 UI 文化，不重新读取被改写的线程值；确认前不写语言或启动次数。声明使用 LocalizedString 绑定，不能靠后台初始化恰好在第二页前完成。

验证：53 项启动、开场、本地化、宿主及清理测试通过，覆盖系统中文、同语言区域匹配、无支持语言、保存偏好延迟恢复、失败与取消不提交。规范见 docs/manual/extensions/customize-sample.md。

禁止做法：用开发档案的最终首页中文证明首次启动或打包版开场正确；在构造默认英文服务后用 CurrentUICulture 代替原始系统语言。
