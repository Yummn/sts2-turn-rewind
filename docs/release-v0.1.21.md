# TurnRewind v0.1.21

## 修复

- 回溯请求不再直接修改仍被 `ActionExecutor` 使用的战斗对象；会等待当前出牌、生成/塞入状态牌及其预览动作结束，再进入恢复阶段。
- 恢复时终止出牌队列残留 Tween，并清理 `NCombatUi` 下所有旧 `NCard` 节点后重建手牌，修复卡牌悬停在屏幕中央、手牌锁死和后续无法出牌。
- `TurnStarted` 发出后用完整回合开始状态覆盖 `SetupPlayerTurn` 的早期快照，使倒数计时施加的灾厄、球被动及其他回合开始效果都进入快照。
- 为战斗卡牌记录跨快照身份、牌组原型和附魔运行时状态；已经消耗的每场战斗一次附魔保持单调消耗，不能通过回溯重新启用。

## 实战验证

PC v0.107.1 自动化实战测试通过：

1. 在自定义卡牌预览动作仍执行时请求回溯，确认恢复等待动作结束，出牌队列为零、画面卡牌节点数与手牌一致、手牌未锁死。
2. 在早期快照后施加 7 层灾厄并触发最终 `TurnStarted` 捕获，移除后回溯，灾厄精确恢复为 7 层。
3. 在快照后消耗 Glam 一次性附魔，回溯到使用前快照，确认附魔仍为 Disabled 且 `UsedThisCombat=true`。

测试日志最终结果：

```text
[CodexTurnRewindV0121RaceTest] TURN-START PASS
[CodexTurnRewindV0121RaceTest] ACTION PASS
[CodexTurnRewindV0121RaceTest] ENCHANT PASS
[CodexTurnRewindV0121RaceTest] PASS
```
