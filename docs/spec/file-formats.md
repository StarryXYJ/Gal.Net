# 文件格式与编译管道

---

## 格式总览

```
graph.json          ← 节点/边元数据（位置、参数等），每个 Group 对应一个 .galgroup 文件
*.galgroup          ← 组条目文件（开发期含复杂条目，编译后只含简单条目）
.galnet             ← 编译产物：graph.json + 编译后 galgroup → 单文件（可二进制化/加密/压缩），纯逻辑不含资源
.galpak             ← 最终分发包：.galnet + 资源文件 → 压缩打包
```

---

## graph.json

每个项目一张主图，存储所有节点和边的元数据（节点类型、位置、显示名称、配置参数等）。Group 节点通过一个 `file` 字段引用对应的 `.galgroup` 文件路径。

```json
{
  "version": 1,
  "name": "MyGame",
  "rootNodeId": "group_intro",
  "nodes": [
    {
      "id": "group_intro",
      "type": "Group",
      "name": "开场",
      "x": 100, "y": 220,
      "file": "groups/intro.galgroup"
    },
    {
      "id": "branch_01",
      "type": "Branch",
      "name": "选择路线",
      "x": 350, "y": 220,
      "branchType": "Choice",
      "options": [
        { "text": "选项A", "condition": "" },
        { "text": "选项B", "condition": "flag == true" }
      ]
    }
  ],
  "edges": [
    { "fromNodeId": "group_intro", "fromOutlet": 0, "toNodeId": "branch_01" }
  ]
}
```

---

## .galgroup 格式

组条目文件，一种纯文本格式。开发期编辑器写入时包含**复杂条目**，运行时加载后由 `Compile()` 编译为**仅含简单条目的内部表示**。导出时编译后的数据随 graph 一同序列化进 `.galnet`。

### 转义规则

值内可通过 `\` 转义分隔符：
- `\:` 表示字面 `:`
- `\;` 表示字面 `;`
- `\\` 表示字面 `\`

### 每行格式

```
条目类型 : 参数1:值1; 参数2:值2; ...
```

### 示例（开发期，含复杂条目）

```
单行文本 : 名称:Alice; 内容:你好\:世界; 是否可打断:true; 对话框id:default_dialogue
播放音频 : 文件引用:bgm_01; 轨道:bgm; 播放形式:repeat
显示图像 : id:bg; 文件引用:bg_classroom; z:0; 转场:fade
表达式求值 : 目标变量:score; 表达式:[score] + 10 * [multiplier]
```

### 解析算法

1. 按行分割
2. 按首个未转义 `:` 取条目类型
3. 剩余部分按未转义 `; ` 分割为参数段
4. 每个参数段按首个未转义 `:` 分割为键值对
5. 反转义键与值

---

## .galnet 格式

**编译后的游戏逻辑单文件**，内容来源：
- `graph.json`（节点/边结构）
- 所有编译后的 `.galgroup`（仅含 SimpleEntry）

格式为二进制 blob（可加密、压缩），供 Runtime 直接加载。不含资源文件（资源由 Assets 模块独立管理）。

### 结构

```
[Header]        ← 版本号 + 图数量 + 是否加密 + 是否压缩
[GraphData]     ← 序列化的 Graph 对象（含编译后的 SimpleEntry 列表）
[Hash]          ← SHA256 完整性校验
```

---

## .galpak 格式

**最终分发的游戏包**，启动器可导入。内容：
- `.galnet`（游戏逻辑，可加密/压缩）
- 资源文件列表（每个资源可独立设置压缩/加密选项）
- `Manifest` 清单（资源索引、版本、入口图 ID、全局 Hash）

### 结构

```
[Manifest]      ← JSON，资源索引 + 元数据
[.galnet blob]  ← 二进制游戏逻辑块
[Asset_1]       ← 资源数据（可选压缩/加密）
[Asset_2]
...
[Asset_N]
[GlobalHash]    ← 整体 SHA256
```

---

## 编译规则（ComplexEntry → SimpleEntry）

### 条目 ID 方案

1. 每个复杂条目在组内有唯一的顺序 ID（其在组中的行号）
2. 编译时 `ComplexEntry.Compile()` 返回若干 SimpleEntry：
   - 若返回 1 个：子条目 ID = `"{Id}"`（如 `"5"`）
   - 若返回多个：子条目 ID = `"{Id}_1"`, `"{Id}_2"`, ...（如 `"5_1"`, `"5_2"`）
   - 所有子条目的 `SourceId` = 原复杂条目的 `Id`
3. 组加载时一次性编译全部复杂条目 → 得扁平 SimpleEntry 列表 → Runtime 执行
4. 热更新时：重新编译单个复杂条目 → 替换对应 ID 前缀的简单条目

### 编译时机

- 编辑器保存组时触发；Runtime 加载组时触发（惰性编译 + 缓存）

### 编译示例

```
复杂条目 (id=5):  show_character | id=alice, asset=alice_smile, x=0.3, y=0.5, z=10, sfx=sfx_appear, transition=fade
    ↓ Compile()
简单条目:
  5_1: show_layer  | id=alice, asset=alice_smile, x=0.3, y=0.5, z=10
  5_2: play_sfx    | channel=sfx1, asset=sfx_appear
  5_3: transition  | type=fade, duration=0.5
```

---

## 数据流

### 开发期

```
图编辑 ──→ graph.json（节点 + 边 + 元数据）
组编辑 ──→ *.galgroup（一行一条 ComplexEntry）
条目编辑 ──→ 组文件的某一行，参数由分隔符分隔
```

### 编译期（保存时 / 加载时触发）

```
ComplexEntry（开发期 .galgroup 行）
  → Compile() → SimpleEntry 列表
  → ID 方案：id=5 展开为 5_1, 5_2,...；SourceId=5 可追溯到原始行
  → 结果缓存在内存，开发期 .galgroup 文件本身不改变
```

### 运行时（编辑器预览 / 启动器运行）

```
Runtime 加载（由 GameEngine 驱动）：
  1. 读取 graph.json → 解析节点/边结构
  2. 按 Group 节点引用的 path 加载对应 .galgroup → 解析 ComplexEntry 行
  3. Compile() 所有 ComplexEntry → 内存中得 SimpleEntry 扁列表
  4. GameEngine 通过 IGameRuntime 接口统一管理运行时状态
     （CurrentNodeId/EntryIndex/VariableStore/SceneState/调用栈/View/I18n）
  5. 定位入口节点，开始状态机循环
  （开发期目录形式或 .galnet 单文件形式，加载路径不同但内部逻辑一致）
```

### 导出（编辑器 → .galpak）

```
                                                                  ┌── 可选加密
                                                                  │
graph.json + *.galgroup ──→ 编译全部 ComplexEntry ──→ 序列化 ──→ .galnet ──┐
                                                                  │         │
资源文件 ──→ 构建索引（Manifest）─────────────────────────────────→ ────────┤
                                                                  │         │
                                          .galnet + 资源 ──→ 打包 ──→ .galpak
                                                                  │
                                                      ┌── 可选压缩/加密
                                                      └── SHA256 校验
```

### 导入（启动器）

```
.galpak ──→ 校验 Hash ──→ 解压/解密 ──→ 提取 .galnet + 资源
                                            │
                                            └── Runtime 加载 .galnet ──→ 运行
```
