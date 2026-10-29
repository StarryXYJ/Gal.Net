# 空本地化引用需要在作者索引、加载和解析边界保持一致

状态：confirmed
来源：2026-10-25 角色引用分析与空对话启动问题，用户明确要求空内容按空字符串处理；未创建独立 feature。

现象：作者索引已经忽略精确空字符串，但 GameLocalizationLoader.Require 把空 content 当成缺失 key，启动报 missing required localization key ''。同时 layer.show 的默认 transform={} 因字段声明为 JsonElement 而产生 field.opaque，阻止角色改 ID。

根因：作者引用覆盖分析与运行内容校验各自实现空值规则；没有引用的空结构也进入了无法遍历 schema 的兜底诊断。

正确做法：缺省、null、空串及纯空白引用不要求翻译；GameTextResolver 直接返回空字符串，不查询翻译来源。空 speaker 不查角色 catalog。作者索引跳过空对象/数组，未知字段和无 schema 类型只有存在非空值时才报告覆盖不完整；非空未知结构仍保留诊断。显式空译文是合法定义，不能当作缺译。

验证：LocalizationReferenceIndexTests 覆盖 layer.show 空 transform 与未知空字段；GameLocalizationLoadingTests 覆盖 dialogue.text、nvl.append 和 Choice 的空引用加载；GameTextResolverTests 验证空引用不调用来源；GameTextLocalizationTests 验证对话与 NVL 展示空文本。

适用范围：游戏本地化引用和引用分析中的空字段；不放宽角色声明的唯一身份、JSON 语法、必填字段或非空错误引用。禁止只在 UI 隐藏诊断、只修改索引却遗漏启动加载器，或把所有非空不透明字段都视为可安全重写。

关联文档：docs/spec/editor-game-localization.md、docs/spec/i18n.md。
