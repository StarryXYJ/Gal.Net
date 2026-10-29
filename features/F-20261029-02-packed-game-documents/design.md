# 设计

## 文档容器

复用 GPAK v3、现有压缩/逐项 SHA-256 和元数据编码，不新增 BSON 或第二种二进制格式。文档包使用独立固定的 document 类型目录和保留逻辑路径，不贡献普通资源 provider，因此资源覆盖包不能替换配置、人物或内置文本。game.pak/localization.pak 的控制身份必须和包内 content 描述一致。

game.pak 保存 content.json（新导出 version=3）、settings.json、gallery.json、characters.json、packaging-layout.json 与 installation.json（运输清单 version=3）。content 描述仍声明基础/剧情分片及其哈希，新增 localization.pak 的完整文件描述。localization.pak 内每文化一个 texts/<culture>.json 平铺文本字典。作者多文件维持原路径 ordinal 覆盖次序；JSON 紧凑编码后由 GPAK 压缩。空语言集也生成有效空语言包。

galpak v3 ZIP 仍统一 pak/ 外层，唯一固定入口为 pak/game.pak；运输清单位于其内部，列出其余文件（避免自哈希循环）。game.pak 自身通过 GPAK 表项校验，安装复用还比较完整入口文件。新输出运输清单使用固定时间，避免同内容重复导出的入口包非确定性；发行时间由发行清单承担。旧 v1/v2 的顶层或 pak/<name>.galnet 路径保持读取兼容。旧 content v2 继续按散文件加载，发现 game.pak 时不回退。

## 本地化与扩展目录

新安装的内置人物和文本仅从包读取；外置 I18n/<culture>/**/*.json 全部是可选重载，不使用 External 层。开发目录和旧安装仍保持 I18n 内置 + I18n/External 的旧规则。默认语言完整性只检查内置源；外置来源损坏记录诊断并跳过，文化回退/来源顺序沿用当前 resolver。

打包与安装始终生成 I18n/、ResourcePatches/、StoryPatches/<当前 buildId>/，ZIP 显式保存空目录。启动不创建安装目录中的文件。相同安装复用保留外置翻译和补丁，不覆盖不同内容。

## 接入

文档编解码归 Assets；文件定位和安装归 Storage.FileSystem；GameHost/Runtime 继续消费既有中立内容对象。Sample 使用同一个入口读取器取得 projectId，保持 profile 摘要规则。给分发 CLI 增加只读 game inspect，以便 PowerShell 5.1/7 脚本读取身份而不重复实现 GPAK 或加载玩家 DLL。

当前仍保留整片读取语义，不宣称按语言分条目已实现按需 I/O。新打包布局标记进入 buildId，因此旧存档不会被误认为同一个内容构建；存档版本和资源 PAK 版本不变。
