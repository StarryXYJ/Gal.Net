# 虚拟化行模板允许空内容

状态：confirmed
日期：2026-10-21
来源：F-20261019-01-editor-game-localization / P5

真实 ListBox 滚动测试在窗口关闭时于 GameLocalizationPanelView.CreateRow 发生空引用。FuncDataTemplate 的声明类型不能保证传入内容非 null：虚拟化容器解绑/回收时会使用空内容刷新 ContentPresenter，直接 row! 后访问属性会使关闭或滚动抛错。

模板工厂对 null 返回空 Border，其余内容按正常行创建。181 行和 58 个分类的真实挂载测试验证只实现部分容器、滚动至最后文本并关闭窗口；修正后该用例通过。不要用全部预生成控件绕过虚拟化回收，也不要仅根据 rows 集合元素非空推断模板输入必非空。

关联：features/F-20261019-01-editor-game-localization/phase-5.md。
