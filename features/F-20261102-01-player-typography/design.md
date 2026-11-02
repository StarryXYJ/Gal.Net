# 设计

设置服务只保存 FontSizePreset 枚举（Small/Smaller/Larger/Large），沿用玩家变量和 SettingsCoordinator；Sample 提供四套语义字号数值和平台默认档，更新 surface 与 shell 的局部资源。避免全局 Application 样式影响 Editor，避免复制设置队列或播放状态。桌面较小档采用现有实际默认值；已有细微字号差异通过独立资源键保留，按各自语义角色变化。

角色：页面标题、分组标题、正文、强调（Speaker 等）、操作、辅助。对白正文单独保留现有资源键，允许与普通正文独立设值；品牌 Logo 不接入档位。平台首次默认由 Sample 配置，已保存选择优先，设备旋转不改变档位。

富文本扩展 `<size=1.25em>`、`<size=125%>`、`<size=+4>`、`<size=-2>`、`<size=24>` 及 `</size>`。em/% 与加减均相对于父级字号，裸正数为绝对设计字号。支持带引号、大小写及嵌套恢复。相对偏移使用控件所在坐标系的字号单位，不是物理像素。

Core token 新增带默认值的 init 属性，保留原位置构造函数和 Deconstruct；用 scale/offset 表示相对于控件基础字号的仿射变换，嵌套时合成，不引用 Avalonia。非法/非有限值标签保留字面文本。最终渲染字号限制到 1–512，避免负字号或异常资源消耗。现有标签语义不变，不扩展 span 属性语法。

TypewriterTextBlock 为当前 runs 保留变换。基础 FontSize 改变时仅更新 Run.FontSize，不重解析内容、不重启任务、不触发 Completed。现有 inherited 字号由默认 runs 继续继承。ADV/NVL/历史共用同一控件，自然得到相同语义；截图使用实际当前排版，存档正文不改写。

风险：大字号的长文本可能改变换行和内容高度；本项不扩展成页面重排，不以 Headless 取代真机验收。测试覆盖资源更新、布局测量与状态，不承诺任意长度文本永不溢出。
