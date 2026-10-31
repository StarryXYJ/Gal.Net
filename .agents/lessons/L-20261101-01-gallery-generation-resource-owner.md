# Gallery 异步请求必须保持本次资源归属

- 状态：confirmed
- 来源：F-20261031-02-sample-ui-template / P4
- 范围：Gallery 缩略图列表替换、解码取消、场景重播 pending startup 与释放
- 证据：`.artifacts/sample-ui-template/p4/presentation.trx` 中 GalleryMechanismTests 四项通过。

## 现象与根因

旧缩略图实现跨 await 后读取可替换的 `_thumbnailCancellation` 字段：下一次激活已换源时，旧解码结果会通过新取消源检查并回写已释放的行；Dispose 将字段清空后还可能空引用。取消解码不保证后端立刻停止或一定抛取消。

场景重播启动在本次 token 取消后会自行释放已返回的 run，但旧代码仍保留 `_starting` 指向同一 run 的完成任务；后续 Close/Dispose 再次等待该任务并重复释放。资源释放的归属不能同时留在启动分支和 pending slot。

## 正确做法与验证

缩略图请求捕获本次取消源及行数组；跨 await 后检查本次取消，过期结果立即释放，不能再读取下一代的取消字段。场景启动在确认 generation 仍属于自己后，先清空 `_starting`，再在取消路径释放结果；generation 已更换时由正在 drain 的释放路径拥有该结果。

GalleryMechanismTests 以门控任务模拟忽略取消、晚返回的解码/启动：分别覆盖列表替换与 Dispose，覆盖仅取消启动和启动期间 Dispose；旧行不接收预览，scope 释放等待启动结束，每个 run 的释放计数为一。

禁止用正常速度的解码/运行通过代替晚返回验证；禁止以重复 Dispose 通常无害作为双重所有权的理由。相关正式事实见 docs/spec/presentation/game-view.md。
