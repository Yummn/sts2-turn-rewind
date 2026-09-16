# 回合回溯 / TurnRewind

> 项目分类：个人项目 / Slay the Spire 2 Mod / 可直接使用

《杀戮尖塔 2》战斗回合快照与回溯模组。战斗界面显示最多十个回合节点，长按节点可恢复该回合开始时的战斗状态。

## 下载与安装

请从 [GitHub Releases](https://github.com/Yummn/sts2-turn-rewind/releases) 下载与你的游戏平台和游戏版本对应的 ZIP。

- 手机启动器：可以直接导入完整 ZIP。
- 手动安装：将 ZIP 内的 `TurnRewind` 文件夹完整复制到游戏 `mods` 目录。
- 不同游戏版本必须使用对应安装包，不能混用 DLL。

## 兼容版本

- Android v0.103.2：使用文件名含“手机-v103”的压缩包。
- Android v0.110.1：使用文件名含“手机-v110.1”的压缩包。
- PC v0.107.1：使用文件名含“电脑-v107.1”的压缩包。

## 最新版本

- [v0.1.23](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.23)：回溯会等待怪物异步招式与玩家回合切换阶段彻底结束，避免尖刺蟾蜍/蝌蚪旧时间线继续错误增减荆棘；重建怪物节点后同步恢复带刺模型、荆棘层数和下一意图。PC v0.107.1 已用尖刺蟾蜍完整“长刺→回溯→爆刺”循环及怪物动作中延迟回溯专项测试验证。

- [v0.1.22](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.22)：回溯现在会等待动作执行器、出牌队列、手牌拖拽队列和中央出牌节点连续四帧全部空闲后再恢复，专门修复回响形态重复出牌动画未完全释放时回溯，下一张牌卡在屏幕中央且不结算的问题。已为 PC v0.107.1、Android v0.103.2 和 Android v0.110.1 完成对应程序集编译；PC 专项实战已验证模组会等待残留中央卡牌节点释放，并可在回溯后正常打出下一张牌。

- [v0.1.21](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.21)：回溯会先等待当前出牌、怪物塞状态牌等动作到达安全边界，再清理残留卡牌节点并恢复快照，修复卡牌悬停在屏幕中央且后续无法出牌；最终回合快照改在 `TurnStarted` 后覆盖，保留倒数计时施加的灾厄等回合开始效果；已消耗的“每场战斗第一次打出”附魔不会因回溯重新启用。PC v0.107.1 专项实战测试已通过动作中回溯、灾厄最终快照和 Glam 一次性附魔三项回归。

- [v0.1.20](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.20)：在玩家和手牌恢复后重新广播战斗历史变化、重算卡牌数值并重建 BetterDefect 的闪电球、冰球和能力牌战斗计数。Android v0.110.1 实战验证：向当前回合注入4条已完成出牌记录后回溯，计数从4精确恢复为0，实际打出改造版超越光速会正确抽1张牌。

- [v0.1.19](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.19)：按状态 ID 恢复怪物状态机游标、下一意图与首回合起手字段，修复双蛞蝓第一回合回溯后意图变化；同时保存能力子类运行时字段并同步 `Surrounded` 朝向贴图，修复螃蟹战回溯后伤害方向正确但角色不转身。Android v0.110.1 已实战验证三只尸体蛞蝓的三个随机起手意图可逐一精确恢复，并验证螃蟹战回溯后朝向与贴图恢复且仍可再次正常转身。

- [v0.1.18](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.18)：新增独立遗物实例快照，回溯时恢复所有 `[SavedProperty]` 计数、遗物状态及可见计数，同时保留原遗物实例以维持遗物栏信号连接。已在 Android v0.110.1 实战中验证开心小花、双节棍和钢笔尖的计数与状态均能恢复。
- [v0.1.17](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.17)：快照现在会同步保存并恢复完整战斗历史，修复回溯到回合开始后回响形态仍被误判为“本回合已触发”的问题。已在 Android v0.110.1 实战中验证原版回响（第一张牌重复）和 BetterDefect 改造回响（第二张牌重复），能力层数及触发次数均能正确恢复。
- [v0.1.16](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.16)：修复召唤物死亡后回溯的怪物阶段与归属错误，并精确恢复击晕状态。
- [v0.1.15](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.15)：新增 Android v0.110.1 兼容。药水栏可用状态通过统一兼容访问器恢复，运行时会优先使用 v110 的 `CanUseOrRemovePotions`，并在 v103 使用 `CanRemovePotions`，避免直接引用另一版本不存在的接口。
- [v0.1.14](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.14)：修复回溯时怪物数量、身份、顺序和 Buff/Debuff 恢复错误。
- [v0.1.13](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.13)：修复机器人充能球快照恢复与视觉模型错位。

## 旧版本

GitHub Releases 保留历史版本；本地模组库只维护每个平台的最新版。
