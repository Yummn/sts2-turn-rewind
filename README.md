# 回合回溯 / TurnRewind

Slay the Spire 2 turn rewind bar mod.

这些是给《Slay the Spire 2 / 杀戮尖塔2》v103/v107 调试制作的 MOD 归档。可安装压缩包放在 GitHub Releases；如果有多个版本，Release 按旧版到新版保留。

## 最新

- [v0.1.13](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.13)：修复机器人球回溯仍会对不上的问题。v0.1.12 已能捕获正确球快照，但 Android 恢复阶段通过反射按 ModelId 创建 OrbModel 失败，会恢复成空球槽；v0.1.13 改为直接用 ModelDb 按 ModelId 创建可变 OrbModel，恢复 OrbQueue 后刷新 NOrbManager。已用 Android v103 真实对局验证：T1 1 个闪电球、T2 3 个闪电球，互相长按回溯后球数量和视觉位置对齐。

## 历史

- [v0.1.0](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.0)
- [v0.1.3](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.3)
- [v0.1.11](https://github.com/Yummn/sts2-turn-rewind/releases/tag/v0.1.11)：首次加入恢复 OrbQueue 后重建球槽/球模型 UI。
- v0.1.4-v0.1.10：本地迭代，主要修正回合开始快照、能量/手牌可打状态、药水槽与手机版 TypeLoadException。当前建议直接使用 v0.1.13。
- v0.1.12：本地诊断版，改为独立球快照；实测发现 ModelId 恢复失败，已由 v0.1.13 修正。

## 安装

下载对应 Release 里的 zip，解压后把其中的 `TurnRewind` 文件夹放入游戏 `mods` 目录。

## 备注

- 最新本地整合包来自 `C:\Users\yummn\Downloads\杀戮尖塔2MOD\可安装-已测试v103`。
- 旧版本仅作留档；通常建议使用最新版本。