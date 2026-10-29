# 属性逻辑路径不能替代实际 JSON Pointer

状态：confirmed
日期：2026-10-27
来源：F-20261027-03-recursive-properties

现象：给 JsonElement 字段补上嵌套结构后，检查器能够识别 JSON 字符串内部的多个引用，但旧的角色比较/替换和文本复制只使用 JsonPointer。多个内部引用共享外层字符串位置，会发生位置键冲突或将整个字符串替换为一个引用值。

根因：JSON Pointer 无法穿过字符串编码边界。结构内逻辑路径和实际 wire 位置是两种信息；只迁移只读分析，遗漏写入及复制消费者，会把原来的覆盖不足变成错误写入。

正确做法：类型化路径保留 Member、Index、MapKey 与 JsonString 边界；对外 Pointer 在编码边界终止，内部位置使用完整逻辑路径。引用比较/去重使用逻辑位置，回写使用类型化路径的纯 Rewrite，并保留原编码、未知成员和精确数值。宿主仍检查作者身份与事务。

验证：RecursivePropertyTests 的编码对象替换；LocalizationReferenceIndexTests.MultipleReferencesInsideAnEncodedArrayHaveDistinctLocationsAndRewriteTheirOwnLeaves 验证同一外层 Pointer 的两条引用独立回写且保留大整数；Editor 全回归覆盖角色和嵌套文本复制。

额外实现陷阱：System.Text.Json.Nodes.JsonArray 不能将已经属于该数组的节点再次赋回同一位置。递归修改子节点后，如果返回的仍是原实例，应保留它而不是重新挂载。初次引用回写测试实际暴露 NodeAlreadyHasParent；比较引用后才执行替换可避免该问题。

禁止仅为消除 opaque 警告就宣称全部引用消费者已支持新结构；必须同时检查定位、身份比较、修改、复制与归档路径。
