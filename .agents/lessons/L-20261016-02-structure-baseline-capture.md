# 结构撤销材料在获得发布门之后捕获

状态：confirmed
来源：F-20261015-02-editor-authoring-workspace / P2

风险：普通保存发布期间允许后续编辑。如果结构编辑先捕获 Saved，再等待发布门，先前保存完成后基线已经推进；结构操作本身可成功，但它保留的撤销材料来自旧基线。

正确做法：ProjectContentSession 的结构准备回调在历史捕获租约和发布门内运行，并提供当时的 Working/Saved。记录只在成功后接收这些材料；重做删除再次捕获当时已保存内容，不固定最初基线。保持 P1b 的锁顺序，不为了取得新基线先持有发布门等待历史。

验证：StructurePreparationSeesTheBaselineOfAnEarlierSaveThatWasStillPublishing 暂停普通保存，再发起结构准备；回调应等待发布结束并读取新基线。最终 Shared 188 项回归已通过，该用例已验证。

适用范围：Group 删除/恢复和后续文件事务。禁止在进入可能异步等待的发布流程前预读 Saved 用作撤销备份。
