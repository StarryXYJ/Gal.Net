# Editor 的 Avalonia 程序集别名

状态：confirmed。来源：F-20261011-03-theme-owned-views。

现象：Editor 直接全局引用 GalNet.Avalonia.Themes 后，既有 Avalonia.Controls/Input 等限定名称被解析为 GalNet.Avalonia 下的成员，出现 CS0234 和接口返回类型错误。

根因与证据：Editor 位于 GalNet.Editor 命名空间；新增 GalNet.Avalonia.Themes 暴露同级 GalNet.Avalonia，使 Avalonia 的相对名称解析受到影响。既有 GameView 引用已采用 GameViewAssembly 别名。Themes 引用改为 Aliases="ThemesAssembly"，仅 EditorPreviewHost 用 extern alias 后，原来 13 个编译错误消失，独立输出目录构建成功。

做法：Editor 增加 GalNet.Avalonia.* 引用时沿用程序集别名，局部通过别名访问 API；不要为此批量重写无关 Editor 源码中的 Avalonia 名称。改动后构建 Editor，不能只验证新增程序集。
