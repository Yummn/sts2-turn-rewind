# v0.1.28 补充实战验证（PC v0.107.1）

本次没有修改发布的 DLL。使用 Steam 启动真实战斗、临时测试模组调用游戏原有的抽牌钩子，再捕获快照、改变状态并执行 `SnapshotManager.Restore`。测试完成后恢复原存档和模组目录。

## 已通过

- 「环绕轨道」已花费能量 6、触发次数 1，回溯后剩余进度显示 2。
- 「虚空形态」本回合已计数 2 张；回溯后不再把后续牌错误地全部视为免费。
- 自动化、野性、华丽收场的简单内部计数和动态数值。
- 王者之踢调用实际 `AfterCardDrawn` 后的战斗内减费，以及叠加一层仅本回合生效的本地减费：两层费用修正回溯后保持顺序与结果。
- 带刺手甲对能力牌的全局费用 +1：回溯后仍只加一次；卡牌本地费用和最终费用分别与快照前一致。
- 力量、敏捷能力数值。
- 星能费用的基础值及“本回合”“打出前”两层临时修正：回溯后基础值、修正数量和最终费用一致。此项使用测试卡注入星能费用，验证的是通用快照机制，不是某张原生星能卡的完整出牌流程。

最终实战日志含：

```text
[CodexTurnRewindMonarchTest] PASS v0128 expanded: Orbit, Void Form, Automation, Feral, Panache, strength/dexterity, Spiked Gauntlets global cost, mixed local energy and ordered star costs.
```

## 类似状态审计与边界

扫描 PC v0.107.1 的能力内部 `Data` 类型：整数、布尔值、小数等简单字段符合 v0.1.28 的通用快照范围，包括 Dark Embrace、Hardened Shell、Juggling、Outbreak、Skittish 等；这些未逐个实战测试。

以下复杂字段**不在**通用快照范围内，尚不能认定其回溯结果正确：Nightmare 的所选卡牌、Curl Up 的卡牌引用，及 Afterimage、Calamity、Gravity、Rupture、Storm、Subroutine 等能力中的卡牌字典／集合。它们需要按卡牌身份和生物身份重新绑定，不能直接复制旧引用。此处是潜在的同类缺口，不等于已证实每项都会触发玩家可见错误。

Android v0.110.1 没有连接设备，本次仍未手机实战验证。
