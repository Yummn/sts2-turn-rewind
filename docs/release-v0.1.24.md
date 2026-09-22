# TurnRewind v0.1.24

## 修复

- 快照记录每个非玩家生物节点的精确全局坐标与缩放；重建节点并完成通用站位后，再覆盖为快照坐标，修复回溯后怪物模型逐次上移。
- 记录卡牌子类中的战斗运行时字段与全部动态变量，包括储君“君王之剑”的当前伤害、爪击/暴走/重殴等牌的本场战斗累计伤害。
- 同步保存卡牌对应牌组原型的运行时状态，避免战斗牌与牌组版本数值脱节。
- 卡牌恢复为新实例后，按稳定卡牌身份重新绑定战斗历史中的 `CardPlay.Card` 等引用，修复依赖“这张牌此前打出次数”的效果无法识别旧回合记录。

## 构建与验证

- PC v0.107.1：零警告、零错误。
- Android v0.103.2：零警告、零错误。
- Android v0.110.1 / v111（启动器 1.9 兼容构建）：零警告、零错误。

PC 专项实战测试执行了真实的君王之剑出牌：先累计 15 点铸剑，使伤害从 10 变为 25 并捕获快照；随后继续增加到 45，同时人为将怪物模型上移 180 像素。回溯后验证：君王之剑动态伤害和私有累计字段均恢复为 25，战斗历史中的出牌记录指向恢复后的卡牌实例，怪物全局坐标与快照完全一致。

```text
[TurnRewind] remapped combat-history card references: 12.
[CodexTurnRewindCardRuntimePositionTest] PASS: Sovereign Blade retained 15 prior forge damage, history points at the restored card, and monster global position is exact.
```

测试源码保存在 `tests/v0124-card-runtime-position-live`。
