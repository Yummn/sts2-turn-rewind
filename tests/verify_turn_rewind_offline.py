#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--binary", action="append", type=Path, default=[])
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()

    root = args.root.resolve()
    source_root = root / "src" if (root / "src").is_dir() else root
    source = (source_root / "TurnRewindCode" / "SnapshotManager.cs").read_text(encoding="utf-8")
    main_file = (source_root / "TurnRewindCode" / "MainFile.cs").read_text(encoding="utf-8")
    manifest = (source_root / "TurnRewind.json").read_text(encoding="utf-8")

    checks = {
        "manifest version is v0.1.30": '"version": "v0.1.30"' in manifest,
        "load log version is v0.1.30": "loaded v0.1.30" in main_file,
        "snapshot stores complete combat history": "public required List<CombatHistoryEntry> CombatHistoryEntries" in source,
        "capture copies combat history entries": "CombatHistory.Instance" not in source and "CombatManager.Instance.History.Entries.ToList()" in source,
        "restore replaces combat history before player state": (
            "RestoreCombatHistory(snapshot.CombatHistoryEntries);" in source
            and source.index("RestoreCombatHistory(snapshot.CombatHistoryEntries);") < source.index("RestorePlayers(state, snapshot);")
        ),
        "restore clears and repopulates the live history list": (
            'AccessTools.Field(typeof(CombatHistory), "_entries")' in source
            and "liveEntries.Clear();" in source
            and "liveEntries.Add(entry);" in source
        ),
        "history changed event is raised after restore": (
            'AccessTools.Field(typeof(CombatHistory), "Changed")' in source
            and "changed.DynamicInvoke();" in source
        ),
        "snapshot stores exact creature references": "public required Creature Creature" in source,
        "snapshot stores creature combat ids": "public required uint? CombatId" in source,
        "snapshot stores next creature id": "public required uint NextCreatureId" in source,
        "snapshot stores escaped creature list": "public required List<Creature> EscapedCreatures" in source,
        "snapshot stores dedicated power state": "public sealed class PowerExtraSnapshot" in source,
        "snapshot stores amount-on-turn-start": "public required int AmountOnTurnStart" in source,
        "snapshot stores duration skip flag": "public required bool SkipNextDurationTick" in source,
        "restore reconciles creature roster": "RestoreCreatureRoster(state, snapshot)" in source,
        "post-snapshot creatures are removed": "state.RemoveCreature(creature, unattach: true)" in source,
        "missing snapshot creatures are re-added": "state.AddCreature(creature)" in source,
        "ally and enemy order are replaced exactly": (
            'ReplaceCreatureSideList(state, "_allies"' in source
            and 'ReplaceCreatureSideList(state, "_enemies"' in source
        ),
        "next creature id is restored": '"_nextCreatureId")?.SetValue(state, snapshot.NextCreatureId)' in source,
        "non-player visuals are rebuilt after every rewind": "RebuildNonPlayerCreatureNodes(state, snapshot.CreatureExtras)" in source,
        "monster subclass runtime fields are snapshotted": (
            "MonsterRuntimeFieldSnapshot" in source
            and "CaptureMonsterRuntimeFields" in source
            and "RestoreMonsterRuntimeFields" in source
        ),
        "stun cursor restores without transition side effects": (
            '"_currentState"' in source
            and '"<NextMove>k__BackingField"' in source
            and "ForceCurrentState(restoredState)" not in source
        ),
        "power is attached without removal callbacks": (
            "ModelDb.GetById<PowerModel>(saved.Id).ToMutable();" in source
            and "powerList.Clear();" in source
            and "creature.ApplyPowerInternal(power);" in source
        ),
        "power owner and turn metadata are restored": (
            'AccessTools.Field(typeof(PowerModel), "_owner")?.SetValue(power, creature)' in source
            and 'AccessTools.Field(typeof(PowerModel), "_amountOnTurnStart")?.SetValue(power, saved.AmountOnTurnStart)' in source
            and 'AccessTools.Field(typeof(PowerModel), "_skipNextDurationTick")?.SetValue(power, saved.SkipNextDurationTick)' in source
        ),
        "power applier and target references are restored": (
            'SetPropertyOrField(power, "_applier", saved.Applier)' in source
            and 'SetPropertyOrField(power, "_target", saved.Target)' in source
        ),
        "monster intent state ids are snapshotted": (
            "public string? NextMoveId" in source
            and "public string? CurrentStateId" in source
            and "public List<string>? StateLogIds" in source
        ),
        "monster states resolve against the live machine": (
            "ResolveMonsterState" in source
            and "machine.States.TryGetValue(stateId, out var liveState)" in source
        ),
        "power subclass runtime fields are snapshotted": (
            "PowerRuntimeFieldSnapshot" in source
            and "CapturePowerRuntimeFields" in source
            and "RestorePowerRuntimeFields" in source
        ),
        "card-play counters refresh after player restore": (
            "RefreshCardPlayCountersAfterRestore(state);" in source
            and source.index("RestorePlayers(state, snapshot);") < source.index("RefreshCardPlayCountersAfterRestore(state);")
            and "RecalculateCardValues" in source
        ),
        "BetterDefect combat counters rebuild from restored history": (
            "SynchronizeBetterDefectCombatCounters" in source
            and '"LightningChanneled"' in source
            and '"FrostChanneled"' in source
            and '"PowerCardsPlayed"' in source
        ),
        "surrounded facing visuals are synchronized": (
            'power.GetType().Name == "SurroundedPower"' in source
            and 'AccessTools.Method(surrounded.GetType(), "FlipScale")' in source
            and "SyncCreaturePowerVisuals(creature);" in source
        ),
        "v111 turn-state collections are cleared": (
            'AccessTools.Field(typeof(CombatManager), "_turnState")' in source
            and 'ClearMemberCollection(turnState, "PlayersReadyToEndTurn")' in source
            and 'ClearMemberCollection(turnState, "PlayersReadyToBeginEnemyTurn")' in source
            and 'ClearMemberCollection(turnState, "PlayersTakingExtraTurn")' in source
        ),
        "v111 end-turn flags are reset": (
            'SetPropertyOrField(turnState, "EndingPlayerTurnPhaseOne", false)' in source
            and 'SetPropertyOrField(turnState, "EndingPlayerTurnPhaseTwo", false)' in source
            and 'SetPropertyOrField(turnState, "PendingLoss", null)' in source
            and "ActionSynchronizerCombatState.PlayPhase" in source
        ),
        "sandpit restore does not launch asynchronous position update": (
            "RestoreSandpitLayout(creature)" not in source
            and "UpdateCreaturePositions" not in source
        ),
        "rewind UI uses long press": (
            "HoldSeconds = 0.85" in (source_root / "TurnRewindCode" / "RewindBar.cs").read_text(encoding="utf-8")
            and "SnapshotManager.Restore(snapshot);" in (source_root / "TurnRewindCode" / "RewindBar.cs").read_text(encoding="utf-8")
        ),
        "rewind UI drag moves panel instead of selecting a segment": (
            "MovePanelBy" in (source_root / "TurnRewindCode" / "RewindBar.cs").read_text(encoding="utf-8")
            and "SelectAt" not in (source_root / "TurnRewindCode" / "RewindBar.cs").read_text(encoding="utf-8")
        ),
    }

    for binary in args.binary:
        path = binary if binary.is_absolute() else root / binary
        checks[f"compiled binary exists: {path}"] = path.is_file() and path.stat().st_size > 0

    passed = [name for name, ok in checks.items() if ok]
    failed = [name for name, ok in checks.items() if not ok]
    report = [
        "TurnRewind v0.1.30 offline audit",
        f"Timestamp: {dt.datetime.now().astimezone().isoformat(timespec='seconds')}",
        f"Passed: {len(passed)}",
        f"Failed: {len(failed)}",
        "",
        *[f"PASS: {name}" for name in passed],
        *[f"FAIL: {name}" for name in failed],
        "",
    ]
    text = "\n".join(report)
    print(text)
    if args.report:
        args.report.write_text(text, encoding="utf-8")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
