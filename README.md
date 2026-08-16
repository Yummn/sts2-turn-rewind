# 回合回溯 / TurnRewind

《杀戮尖塔 2》战斗回合快照与回溯模组。战斗界面显示最多十个回合节点，长按节点可恢复该回合开始时的战斗状态。

## 最新版本

- [v0.1.16](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.16)：修复召唤物死亡后回溯的怪物阶段与归属错误，并精确恢复击晕状态。已在女王/火炬头实战中验证：火炬头死亡后回溯不会再与女王阶段并存，击晕回溯后仍保持眩晕。
- [v0.1.15](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.15)：新增 Android v0.110.1 兼容。药水栏可用状态通过统一兼容访问器恢复，运行时会优先使用 v110 的 `CanUseOrRemovePotions`，并在 v103 使用 `CanRemovePotions`，避免直接引用另一版本不存在的接口。
- [v0.1.14](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.14)：修复回溯时怪物数量、身份、顺序和 Buff/Debuff 恢复错误。
- [v0.1.13](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.13)：修复机器人充能球快照恢复与视觉模型错位。

## 兼容版本

- Android v0.103.2：使用文件名含“手机-v103”的压缩包。
- Android v0.110.1：使用文件名含“手机-v110.1”的压缩包。
- PC v0.107.1：使用文件名含“电脑-v107.1”的压缩包。

## 安装

下载对应 Release 中的 ZIP。手机启动器可直接导入完整 ZIP；手动安装时，将 ZIP 内的 `TurnRewind` 文件夹完整复制到游戏 `mods` 目录。

不同游戏版本必须使用对应安装包，不能混用 DLL。

## 旧版本

GitHub Releases 保留历史版本；本地模组库只维护每个平台的最新版。
