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

PC v0.107.1 专项实战测试已通过：强制启用 BetterDefect 改造回响，制造一个在动作执行器空闲后仍停留 0.6 秒的中央卡牌节点，立即请求回溯；模组正确等待视觉节点释放后才恢复，随后真实打出故障机器人打击，动作完成且卡牌离开屏幕。测试源码保存在 `tests/v0122-echo-action-live`。

```text
[TurnRewind] action executor is idle but card animation pipeline is still active (... playNodes=1); waiting before rewind.
[CodexTurnRewindEchoActionTest] PASS: post-Echo rewind card action completed and left the screen.
```
