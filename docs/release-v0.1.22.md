# TurnRewind v0.1.22

## 修复

- 安全回溯边界不再只判断 `ActionExecutor`。现在同时检查 `NCardPlayQueue`、`NPlayerHand._holdersAwaitingQueue`、手牌选牌状态和 `PlayContainer` 中央出牌节点。
- 只有上述状态连续四个渲染帧全部空闲才开始恢复，避免回响形态的第二次出牌已结束逻辑动作、但最后一段卡牌 Tween/回调仍持有旧节点时重建手牌。
- 增加等待原因日志；若中央卡牌动画尚未释放，会记录 `queue / awaiting / selecting / playNodes` 状态，便于继续定位极端模组冲突。

## 构建验证

以下三个目标均为零警告、零错误：

- PC v0.107.1
- Android v0.103.2
- Android v0.110.1

此前的真实出牌基线已验证：原版回响与 BetterDefect 改造回响在回溯后，下一张 `PlayCardAction` 都能完成并离开屏幕。本版额外覆盖该动作结束与视觉释放之间的竞态窗口。当前机器 Steam 客户端未建立用户会话，未在本次发布前重新启动游戏执行最终 live test；测试模组源码保存在 `tests/v0122-echo-action-live`。
