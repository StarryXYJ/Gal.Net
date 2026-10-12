# 阶段

## 页面框架与交互验证 — verified

统一七个页面和视觉类，渲染设置/有缩略图的 Gallery/大图详情；以实际鼠标返回验证导航，检查大小窗口及所有主题组合。退出条件为相关展示层回归、Sample 构建和格式检查通过，并记录预览与验证边界。

证据：Presentation 98 通过，新增六个大图返回检查，九种组合的面板与返回几何一致；Sample 构建通过，变更测试文件格式/import 和 diff 检查通过。渲染目录 artifacts/player-page-layout。

## 用户修订：主题独立页面及字号 — verified

补齐三套主题的全部普通页面 View；移除页面外框，调整对白名字/正文层级；验证各主题内导航一致且大图不会遮挡返回。退出条件：最终展示与集成回归、主题项目格式和 Sample 构建通过；记录渲染对比。渲染目录 artifacts/theme-owned-pages。

证据：最终 Presentation 98、Integration 62 通过（并构建 Sample）；Themes whitespace/IDE0005 和变更测试文件 whitespace 检查通过。九种组合验证 View 属于各自主题命名空间、页面无外框和名字字号，大图返回覆盖两种窗口尺寸。修正 Paper 宽度资源的重复定义后全套回归通过。
