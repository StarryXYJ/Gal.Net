# 停止后端前先撤销播放身份

状态：confirmed。来源：F-20261015-03-modular-game-state；AudioPersistenceTests 的同步 Stop 终态事件回归。

现象：恢复时停止旧 voice，某些 host 会在 Stop 调用内同步报告 Ended。若映射仍有效，终态回调会把该事件作为自然结束，消费旧队列并启动声音，污染恢复状态。

根因：后端请求不是单向无回调边界；即使通常异步，也允许同步重入。

正确做法：先保存需停止的 binding，再清空 identity/ticket 映射，最后提交 Stop。结束回调必须核对 voice ID 与 generation；新运行代还需匹配 GameState 根。自然结束先提交队列变化再完成 ticket，避免完成通知观察半次更新。

适用：音频停止、重绑、恢复和销毁。禁止在后端 Stop 返回后才撤销全部旧身份。单项停止先从活动集合删除，避免同步终态事件推进队列。

发现方式：代码检查后加入 fake host 在 Stop 内立即回调 Ended 的测试；Runtime 回归 84 项通过。相关契约见 docs/spec/runtime.md。
