# TurnRewind v0.1.23

## 修复

- 安全回溯边界新增怪物回合异步任务检测：怪物仍在执行招式、敌方回合已启动或玩家回合正处于两个结束阶段时，不再直接重写战斗图，而是等待稳定的玩家操作阶段。
- 修复尖刺蟾蜍与蝌蚪在荆棘增减动画/异步任务尚未结束时回溯，旧时间线继续修改荆棘层数的问题。
- 重建怪物节点后，根据 `SpinyToad.IsSpiny` 和 `ThornsPower` 同步带刺形态动画，并直接落到对应待机状态，保证荆棘层数、模型形态、下一意图和下一次褪刺结算一致。

## 构建与实战验证

以下三个目标均为零警告、零错误：

- PC v0.107.1
- Android v0.103.2
- Android v0.110.1

PC v0.107.1 专项实战测试强制进入 `SPINY_TOAD_NORMAL`，执行原版“长出尖刺 → 尖刺爆炸”循环，在带有5层荆棘且下一意图为爆炸时捕获快照。先执行爆炸清空荆棘，再回溯并检查：5层荆棘、`IsSpiny`、`SPIKE_EXPLOSION_MOVE` 状态机游标和带刺待机动画全部恢复；随后再次执行爆炸，荆棘正常且仅移除一次。测试还人为保持 `IsPerformingMove=true`，确认回溯会等待怪物任务释放后再恢复。

```text
[TurnRewind] monster/turn pipeline is still active (... moving=[SPINY_TOAD]); waiting before rewind.
[CodexTurnRewindThornsTest] PASS: thorn power, SpinyToad body/intent and post-rewind unspike lifecycle are exact; active monster moves are deferred.
```

测试源码保存在 `tests/v0123-spiny-toad-thorns-live`。
