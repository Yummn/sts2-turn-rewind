using HarmonyLib;
using Godot;
using System.Collections;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace TurnRewind;

public sealed class TurnSnapshot
{
    public required int Sequence { get; init; }
    public required int RoundNumber { get; init; }
    public required CombatSide CurrentSide { get; init; }
    public required int PlayerTurnNumber { get; init; }
    public required NetFullCombatState FullState { get; init; }
    public required List<CreatureExtraSnapshot> CreatureExtras { get; init; }
    public required uint NextCreatureId { get; init; }
    public required List<Creature> EscapedCreatures { get; init; }
    public required List<PotionBarSnapshot> PotionBars { get; init; }
    public required List<OrbQueueSnapshot> OrbQueues { get; init; }
    public required string Label { get; init; }
    public required string Key { get; init; }
}

public sealed class CreatureExtraSnapshot
{
    public required int Index { get; init; }
    public required Creature Creature { get; init; }
    public required uint? CombatId { get; init; }
    public required int CurrentHp { get; init; }
    public required int MaxHp { get; init; }
    public required int Block { get; init; }
    public required List<PowerExtraSnapshot> Powers { get; init; }
    public MoveState? NextMove { get; init; }
    public MonsterState? CurrentState { get; init; }
    public bool? PerformedFirstMove { get; init; }
    public List<MonsterState>? StateLog { get; init; }
    public bool? SpawnedThisTurn { get; init; }
    public bool? IsPerformingMove { get; init; }
    public required bool IsStunned { get; init; }
    public required List<MonsterRuntimeFieldSnapshot> MonsterRuntimeFields { get; init; }
}

public sealed class MonsterRuntimeFieldSnapshot
{
    public required string DeclaringType { get; init; }
    public required string FieldName { get; init; }
    public object? Value { get; init; }
}

public sealed class PowerExtraSnapshot
{
    public required ModelId Id { get; init; }
    public required int Amount { get; init; }
    public required int AmountOnTurnStart { get; init; }
    public required bool SkipNextDurationTick { get; init; }
    public Creature? Applier { get; init; }
    public Creature? Target { get; init; }
}

public sealed class PotionBarSnapshot
{
    public required object? PlayerId { get; init; }
    public required int MaxPotionCount { get; init; }
    public required bool CanRemovePotions { get; init; }
    public required List<object?> SlotPotionIds { get; init; }
}

public sealed class OrbQueueSnapshot
{
    public required object? PlayerId { get; init; }
    public required int Capacity { get; init; }
    public required List<OrbSnapshot> Orbs { get; init; }
}

public sealed class OrbSnapshot
{
    public required object? Id { get; init; }
    public required int Passive { get; init; }
    public required int Evoke { get; init; }
    public required string DebugId { get; init; }
    public required Dictionary<string, decimal> DecimalFields { get; init; }
}

internal static class SnapshotManager
{
    private const int MaxSnapshots = 10;
    private static readonly List<TurnSnapshot> _snapshots = [];
    private static int _sequence;
    private static bool _initialized;
    private static bool _restoring;
    private static string? _lastCaptureKey;
    private static readonly System.Reflection.PropertyInfo? CanUseOrRemovePotionsProperty =
        AccessTools.Property(typeof(Player), "CanUseOrRemovePotions") ??
        AccessTools.Property(typeof(Player), "CanRemovePotions");

    public static IReadOnlyList<TurnSnapshot> Snapshots => _snapshots;

    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        AddCombatManagerHandler("CombatSetUp", (Action<CombatState>)OnCombatSetUp);
        AddCombatManagerHandler("TurnStarted", (Action<CombatState>)OnTurnStarted);
        AddCombatManagerHandler("CombatEnded", (Action<CombatRoom>)OnCombatFinished);
        AddCombatManagerHandler("CombatWon", (Action<CombatRoom>)OnCombatFinished);
    }

    private static void AddCombatManagerHandler(string name, Delegate handler)
    {
        var manager = CombatManager.Instance;
        try
        {
            var evt = typeof(CombatManager).GetEvent(name);
            if (evt is not null)
            {
                evt.AddEventHandler(manager, handler);
                return;
            }
        }
        catch { }

        try
        {
            var field = AccessTools.Field(typeof(CombatManager), name);
            if (field is not null)
                field.SetValue(manager, Delegate.Combine(field.GetValue(manager) as Delegate, handler));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] failed to subscribe CombatManager.{name}: {ex.Message}");
        }
    }

    private static void OnCombatFinished(CombatRoom _) => Clear();

    private static void OnCombatSetUp(CombatState state)
    {
        Clear();
        MainFile.Logger.Info("[TurnRewind] combat setup detected; snapshot queue cleared.");
    }

    private static void OnTurnStarted(CombatState state)
    {
        if (_restoring)
            return;

        try
        {
            if (!CombatManager.Instance.IsInProgress || state.CurrentSide != CombatSide.Player)
                return;

            // CombatManager.TurnStarted fires before SetupPlayerTurn has fully reset
            // energy/drawn hand/potion usability on v103.  The real snapshot is
            // captured by CombatManagerSetupPlayerTurnPatch after that task
            // completes, so this handler is kept only as a lightweight fallback
            // subscription point and deliberately does not serialize state.
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[TurnRewind] failed to capture turn snapshot: {ex}");
        }
    }

    public static async void CaptureAfterPlayerTurnSetup(Task setupTask, Player player)
    {
        if (_restoring)
            return;

        try
        {
            await setupTask.ConfigureAwait(true);

            if (_restoring || !CombatManager.Instance.IsInProgress)
                return;

            var state = CombatManager.Instance.DebugOnlyGetState();
            if (state is null || state.CurrentSide != CombatSide.Player)
                return;

            if (!state.Players.Contains(player))
                player = state.Players.FirstOrDefault() ?? player;

            CapturePlayerTurnSnapshot(state, player, "after SetupPlayerTurn");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] delayed player-turn snapshot skipped: {ex.Message}");
        }
    }

    private static void CapturePlayerTurnSnapshot(CombatState state, Player? player, string reason)
    {
        var turn = player?.PlayerCombatState is { } pcs ? GetTurnNumber(pcs, state.RoundNumber) : state.RoundNumber;
        var energy = player?.PlayerCombatState?.Energy ?? -1;
        var handCount = player?.PlayerCombatState?.Hand.Cards.Count ?? -1;
        var potionCount = CountPlayerPotions(player);
        var orbQueues = CaptureOrbQueues(state);
        var key = $"{state.GetHashCode()}:{state.RoundNumber}:{turn}:{state.CurrentSide}:{CombatManager.Instance.History.GetHashCode()}";
        if (_lastCaptureKey == key)
            return;
        _lastCaptureKey = key;

        var snapshot = new TurnSnapshot
        {
            Sequence = ++_sequence,
            RoundNumber = state.RoundNumber,
            CurrentSide = state.CurrentSide,
            PlayerTurnNumber = turn,
            FullState = NetFullCombatState.FromRun(state.RunState, null),
            CreatureExtras = CaptureCreatureExtras(state),
            NextCreatureId = GetNextCreatureId(state),
            EscapedCreatures = state.EscapedCreatures.ToList(),
            PotionBars = CapturePotionBars(state),
            OrbQueues = orbQueues,
            Label = $"T{turn}",
            Key = key
        };

        _snapshots.Add(snapshot);
        while (_snapshots.Count > MaxSnapshots)
            _snapshots.RemoveAt(0);

        MainFile.Logger.Info($"[TurnRewind] captured player turn snapshot ({reason}): seq={snapshot.Sequence}, turn={turn}, energy={energy}, hand={handCount}, potions={potionCount}, orbs={DescribeOrbQueues(snapshot.OrbQueues)}, queue={_snapshots.Count}.");
        RewindBar.RefreshAllBars();
    }

    public static void Clear()
    {
        _snapshots.Clear();
        _lastCaptureKey = null;
        RewindBar.RefreshAllBars();
    }

    public static void Restore(TurnSnapshot snapshot)
    {
        if (_restoring)
            return;

        var state = CombatManager.Instance.DebugOnlyGetState();
        if (state is null || !CombatManager.Instance.IsInProgress)
            return;

        try
        {
            _restoring = true;
            MainFile.Logger.Info($"[TurnRewind] restoring snapshot seq={snapshot.Sequence}, turn={snapshot.PlayerTurnNumber}.");

            // Stop the active executor/turn coroutine at the next pause point while we mutate the combat graph.
            CombatManager.Instance.Pause();
            RunManager.Instance.ActionExecutor.Pause();

            ApplyManagerFlagsForPlayerTurn(snapshot);
            ApplyState(state, snapshot);

            RunManager.Instance.ActionExecutor.Unpause();
            CombatManager.Instance.Unpause();
            MainFile.Logger.Info($"[TurnRewind] restore complete: seq={snapshot.Sequence}, turn={snapshot.PlayerTurnNumber}.");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[TurnRewind] restore failed: {ex}");
            try
            {
                RunManager.Instance.ActionExecutor.Unpause();
                CombatManager.Instance.Unpause();
            }
            catch { }
        }
        finally
        {
            _restoring = false;
            RewindBar.RefreshAllBars();
        }
    }

    private static void ApplyManagerFlagsForPlayerTurn(TurnSnapshot snapshot)
    {
        var manager = CombatManager.Instance;
        AccessTools.Field(typeof(CombatManager), "_playersReadyToEndTurn")?.GetValue(manager)
            ?.GetType().GetMethod("Clear")?.Invoke(AccessTools.Field(typeof(CombatManager), "_playersReadyToEndTurn")?.GetValue(manager), null);
        AccessTools.Field(typeof(CombatManager), "_playersReadyToBeginEnemyTurn")?.GetValue(manager)
            ?.GetType().GetMethod("Clear")?.Invoke(AccessTools.Field(typeof(CombatManager), "_playersReadyToBeginEnemyTurn")?.GetValue(manager), null);
        AccessTools.Field(typeof(CombatManager), "_playersTakingExtraTurn")?.GetValue(manager)
            ?.GetType().GetMethod("Clear")?.Invoke(AccessTools.Field(typeof(CombatManager), "_playersTakingExtraTurn")?.GetValue(manager), null);
        AccessTools.Field(typeof(CombatManager), "_inPlayerTurnSetup")?.SetValue(manager, false);
        AccessTools.Field(typeof(CombatManager), "_deferredEndTurnTransition")?.SetValue(manager, null);
        AccessTools.PropertySetter(typeof(CombatManager), "IsEnemyTurnStarted")?.Invoke(manager, [false]);
        AccessTools.PropertySetter(typeof(CombatManager), "EndingPlayerTurnPhaseOne")?.Invoke(manager, [false]);
        AccessTools.PropertySetter(typeof(CombatManager), "EndingPlayerTurnPhaseTwo")?.Invoke(manager, [false]);
        AccessTools.PropertySetter(typeof(CombatManager), "PlayerActionsDisabled")?.Invoke(manager, [false]);

        try
        {
            (AccessTools.Field(typeof(CombatManager), "_cardOrPotionEffectDepth")?.GetValue(manager) as IDictionary)?.Clear();
        }
        catch { }

        CancelQueuedPotionActions();
    }

    private static void CancelQueuedPotionActions()
    {
        try
        {
            var canceled = 0;
            var synchronizer = RunManager.Instance.ActionQueueSynchronizer;
            if (synchronizer is not null)
            {
                var waiting = AccessTools.Field(synchronizer.GetType(), "_requestedActionsWaitingForPlayerTurn")?.GetValue(synchronizer) as IList;
                canceled += CancelPotionActionsInList(waiting, removeFromList: true, waitingOnly: false);
            }

            var queueSet = RunManager.Instance.ActionQueueSet;
            var queues = AccessTools.Field(queueSet.GetType(), "_actionQueues")?.GetValue(queueSet) as IEnumerable;
            if (queues is not null)
            {
                foreach (var queue in queues)
                {
                    var actions = AccessTools.Field(queue.GetType(), "actions")?.GetValue(queue) as IList;
                    canceled += CancelPotionActionsInList(actions, removeFromList: true, waitingOnly: true);
                }
            }

            if (canceled > 0)
                MainFile.Logger.Info($"[TurnRewind] canceled queued potion actions before restore: {canceled}.");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] queued potion action cleanup skipped: {ex.Message}");
        }
    }

    private static int CancelPotionActionsInList(IList? actions, bool removeFromList, bool waitingOnly)
    {
        if (actions is null)
            return 0;

        var canceled = 0;
        for (var i = actions.Count - 1; i >= 0; i--)
        {
            var action = actions[i];
            if (!IsPotionGameAction(action))
                continue;
            if (waitingOnly && !string.Equals(GetRawMember(action!, "State")?.ToString(), "WaitingForExecution", StringComparison.Ordinal))
                continue;

            try { InvokeByName(action!, "Cancel"); } catch { }
            if (removeFromList)
            {
                try { actions.RemoveAt(i); } catch { }
            }
            canceled++;
        }
        return canceled;
    }

    private static bool IsPotionGameAction(object? action)
    {
        if (action is null)
            return false;
        var name = action.GetType().Name;
        return string.Equals(name, "UsePotionAction", StringComparison.Ordinal)
            || string.Equals(name, "DiscardPotionGameAction", StringComparison.Ordinal)
            || name.Contains("Potion", StringComparison.Ordinal);
    }

    private static void ApplyState(CombatState state, TurnSnapshot snapshot)
    {
        state.RoundNumber = snapshot.RoundNumber;
        state.CurrentSide = CombatSide.Player;

        MainFile.Logger.Info("[TurnRewind] apply state: restoring run rng.");
        TryRestoreRunRng(state.RunState, snapshot.FullState);
        MainFile.Logger.Info("[TurnRewind] apply state: restoring creature roster.");
        var rosterChanged = RestoreCreatureRoster(state, snapshot);
        MainFile.Logger.Info("[TurnRewind] apply state: restoring creature vitals/powers.");
        RestoreCreatures(state, snapshot.CreatureExtras);
        MainFile.Logger.Info("[TurnRewind] apply state: restoring creature move extras.");
        RestoreCreatureExtras(state, snapshot.CreatureExtras);
        // Rebuild even when the roster contains the same creature objects.
        // Death, summon and stun animations live on NCreature nodes rather
        // than in CombatState and otherwise survive the model rollback.
        MainFile.Logger.Info($"[TurnRewind] apply state: rebuilding creature visuals (rosterChanged={rosterChanged}).");
        RebuildNonPlayerCreatureNodes(state);
        MainFile.Logger.Info("[TurnRewind] apply state: restoring players.");
        RestorePlayers(state, snapshot);
        MainFile.Logger.Info("[TurnRewind] apply state: refreshing combat UI.");
        RefreshCombatUiAfterRestore(state);
    }

    private static void TryRestoreRunRng(IRunState runState, NetFullCombatState fullState)
    {
        try
        {
            runState.Rng.GetType().GetMethod("LoadFromSerializable")?.Invoke(runState.Rng, [fullState.Rng]);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] run RNG restore skipped: {ex.Message}");
        }
    }

    private static List<CreatureExtraSnapshot> CaptureCreatureExtras(CombatState state)
    {
        var result = new List<CreatureExtraSnapshot>();
        var creatures = state.Creatures.ToList();
        for (var i = 0; i < creatures.Count; i++)
        {
            var creature = creatures[i];
            var monster = creature.Monster;
            var machine = monster?.MoveStateMachine;
            result.Add(new CreatureExtraSnapshot
            {
                Index = i,
                Creature = creature,
                CombatId = creature.CombatId,
                CurrentHp = creature.CurrentHp,
                MaxHp = creature.MaxHp,
                Block = creature.Block,
                Powers = creature.Powers.Select(CapturePower).ToList(),
                NextMove = monster?.NextMove,
                CurrentState = machine is null ? null : AccessTools.Field(typeof(MonsterMoveStateMachine), "_currentState")?.GetValue(machine) as MonsterState,
                PerformedFirstMove = machine is null ? null : AccessTools.Field(typeof(MonsterMoveStateMachine), "_performedFirstMove")?.GetValue(machine) as bool?,
                StateLog = machine?.StateLog?.ToList(),
                SpawnedThisTurn = monster?.SpawnedThisTurn,
                IsPerformingMove = monster?.IsPerformingMove,
                IsStunned = creature.IsStunned,
                MonsterRuntimeFields = monster is null ? [] : CaptureMonsterRuntimeFields(monster)
            });
        }
        return result;
    }

    private static List<MonsterRuntimeFieldSnapshot> CaptureMonsterRuntimeFields(MonsterModel monster)
    {
        var result = new List<MonsterRuntimeFieldSnapshot>();
        var flags = System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.DeclaredOnly;

        // Monster subclasses keep encounter phase, summon ownership and
        // special stun state in their own fields (for example Queen's
        // _hasAmalgamDied/_amalgam and CeremonialBeast's stun flag). The
        // generic network snapshot does not include those values.
        for (var type = monster.GetType(); type is not null && type != typeof(MonsterModel); type = type.BaseType)
        {
            foreach (var field in type.GetFields(flags))
            {
                if (field.IsStatic || field.IsLiteral || field.IsInitOnly || !CanSnapshotMonsterField(field.FieldType))
                    continue;
                try
                {
                    result.Add(new MonsterRuntimeFieldSnapshot
                    {
                        DeclaringType = field.DeclaringType?.AssemblyQualifiedName ?? type.AssemblyQualifiedName ?? type.FullName ?? type.Name,
                        FieldName = field.Name,
                        Value = field.GetValue(monster)
                    });
                }
                catch { }
            }
        }
        return result;
    }

    private static bool CanSnapshotMonsterField(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive || underlying.IsEnum || underlying == typeof(decimal) ||
               underlying == typeof(string) || underlying == typeof(ModelId) ||
               typeof(Creature).IsAssignableFrom(underlying) ||
               typeof(MonsterState).IsAssignableFrom(underlying);
    }

    private static PowerExtraSnapshot CapturePower(PowerModel power)
    {
        return new PowerExtraSnapshot
        {
            Id = power.Id,
            Amount = power.Amount,
            AmountOnTurnStart = power.AmountOnTurnStart,
            SkipNextDurationTick = power.SkipNextDurationTick,
            Applier = GetMember<Creature>(power, "Applier", "_applier"),
            Target = GetMember<Creature>(power, "Target", "_target")
        };
    }

    private static List<PotionBarSnapshot> CapturePotionBars(CombatState state)
    {
        var result = new List<PotionBarSnapshot>();
        foreach (var player in state.Players)
        {
            try
            {
                var slotIds = new List<object?>();
                foreach (var slot in GetPotionSlots(player).Cast<object?>())
                {
                    slotIds.Add(slot is null ? null : GetPotionId(slot));
                }

                while (slotIds.Count < player.MaxPotionCount)
                    slotIds.Add(null);

                result.Add(new PotionBarSnapshot
                {
                    PlayerId = player.NetId,
                    MaxPotionCount = Math.Max(player.MaxPotionCount, slotIds.Count),
                    CanRemovePotions = GetCanUseOrRemovePotions(player),
                    SlotPotionIds = slotIds
                });
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[TurnRewind] potion bar snapshot skipped for {player.NetId}: {ex.Message}");
            }
        }
        return result;
    }

    private static List<OrbQueueSnapshot> CaptureOrbQueues(CombatState state)
    {
        var result = new List<OrbQueueSnapshot>();
        foreach (var player in state.Players)
        {
            try
            {
                var queue = player.PlayerCombatState?.OrbQueue;
                if (queue is null)
                    continue;

                result.Add(new OrbQueueSnapshot
                {
                    PlayerId = player.NetId,
                    Capacity = queue.Capacity,
                    Orbs = queue.Orbs.Select(CaptureOrb).ToList()
                });
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[TurnRewind] orb queue snapshot skipped for {player.NetId}: {ex.Message}");
            }
        }

        return result;
    }

    private static OrbSnapshot CaptureOrb(OrbModel orb)
    {
        return new OrbSnapshot
        {
            Id = GetOrbId(orb),
            Passive = SafeInt(orb.PassiveVal),
            Evoke = SafeInt(orb.EvokeVal),
            DebugId = SafeId(orb),
            DecimalFields = CaptureOrbDecimalFields(orb)
        };
    }

    private static Dictionary<string, decimal> CaptureOrbDecimalFields(OrbModel orb)
    {
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        try
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (var field in orb.GetType().GetFields(flags))
            {
                if (field.FieldType != typeof(decimal))
                    continue;
                if (field.GetValue(orb) is decimal value)
                    values[field.Name] = value;
            }
        }
        catch { }
        return values;
    }

    private static uint GetNextCreatureId(CombatState state)
    {
        try
        {
            if (AccessTools.Field(typeof(CombatState), "_nextCreatureId")?.GetValue(state) is uint value)
                return value;
        }
        catch { }

        return state.Creatures
            .Select(creature => creature.CombatId ?? 0u)
            .DefaultIfEmpty(0u)
            .Max() + 1u;
    }

    private static bool RestoreCreatureRoster(CombatState state, TurnSnapshot snapshot)
    {
        var savedCreatures = snapshot.CreatureExtras
            .OrderBy(saved => saved.Index)
            .Select(saved => saved.Creature)
            .ToList();
        var before = state.Creatures.ToList();
        var changed = before.Count != savedCreatures.Count || !before.SequenceEqual(savedCreatures);

        foreach (var creature in before.Where(creature => !savedCreatures.Contains(creature)).ToList())
        {
            try
            {
                state.RemoveCreature(creature, unattach: true);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[TurnRewind] failed to remove post-snapshot creature {creature.ModelId}: {ex.Message}");
            }
        }

        // RemoveCreature can return early for an already-detached summon even
        // though a stale side-list entry still exists. The exact side-list
        // replacement below removes it from the roster; explicitly detach its
        // CombatState as well so queued hooks cannot re-use it after restore.
        foreach (var creature in before.Where(creature => !savedCreatures.Contains(creature)))
            SetPropertyOrField(creature, "CombatState", null);

        foreach (var saved in snapshot.CreatureExtras.OrderBy(saved => saved.Index))
        {
            var creature = saved.Creature;
            if (state.ContainsCreature(creature))
                continue;

            try
            {
                SetPropertyOrField(creature, "CombatState", state);
                SetPropertyOrField(creature, "CombatId", saved.CombatId);
                state.AddCreature(creature);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[TurnRewind] failed to re-add saved creature {creature.ModelId}: {ex.Message}");
            }
        }

        // AddCreature appends revived monsters. Restore the exact snapshot order
        // so move order and NetFullCombatState creature-to-state mapping remain
        // stable even when a middle monster died or a minion was spawned later.
        ReplaceCreatureSideList(state, "_allies", savedCreatures.Where(creature => creature.Side == CombatSide.Player));
        ReplaceCreatureSideList(state, "_enemies", savedCreatures.Where(creature => creature.Side == CombatSide.Enemy));

        if (AccessTools.Field(typeof(CombatState), "_escapedCreatures")?.GetValue(state) is IList escaped)
        {
            escaped.Clear();
            foreach (var creature in snapshot.EscapedCreatures)
                escaped.Add(creature);
        }

        AccessTools.Field(typeof(CombatState), "_nextCreatureId")?.SetValue(state, snapshot.NextCreatureId);

        MainFile.Logger.Info(
            $"[TurnRewind] creature roster restored: {before.Count}->{state.Creatures.Count}, " +
            $"changed={changed}, order=[{string.Join(",", state.Creatures.Select(creature => creature.ModelId.Entry))}].");
        return changed;
    }

    private static void ReplaceCreatureSideList(CombatState state, string fieldName, IEnumerable<Creature> creatures)
    {
        if (AccessTools.Field(typeof(CombatState), fieldName)?.GetValue(state) is not IList list)
            return;

        list.Clear();
        foreach (var creature in creatures)
            list.Add(creature);
    }

    private static void RestoreCreatures(CombatState state, IReadOnlyList<CreatureExtraSnapshot> snapshots)
    {
        foreach (var saved in snapshots)
        {
            var creature = saved.Creature;
            if (!state.ContainsCreature(creature))
            {
                MainFile.Logger.Warn($"[TurnRewind] saved creature missing after roster restore: index={saved.Index}, id={creature.ModelId}.");
                continue;
            }

            ApplyCreatureVitals(creature, saved.CurrentHp, saved.MaxHp, saved.Block);
            RestorePowers(creature, saved.Powers);
        }
    }

    private static void RestoreCreatureExtras(CombatState state, List<CreatureExtraSnapshot> extras)
    {
        var creatures = state.Creatures.ToList();
        foreach (var saved in extras)
        {
            if (!state.ContainsCreature(saved.Creature))
                continue;

            var monster = saved.Creature.Monster;
            if (monster?.MoveStateMachine is null)
                continue;

            try
            {
                RestoreMonsterRuntimeFields(monster, saved.MonsterRuntimeFields);

                if (saved.SpawnedThisTurn.HasValue)
                    AccessTools.Field(typeof(MonsterModel), "_spawnedThisTurn")?.SetValue(monster, saved.SpawnedThisTurn.Value);
                if (saved.IsPerformingMove.HasValue)
                    AccessTools.Field(typeof(MonsterModel), "_isPerformingMove")?.SetValue(monster, saved.IsPerformingMove.Value);

                var machine = monster.MoveStateMachine;
                // During an actual stun, CurrentState is STUNNED while NextMove
                // already contains the move the monster will use after recovering.
                // They are intentionally different and both must be restored.
                var restoredState = saved.CurrentState ?? saved.NextMove;
                if (restoredState is not null)
                {
                    // ForceCurrentState invokes transition logic.  For STUNNED
                    // that logic immediately selected the monster's next normal
                    // move, so a rewind visually revived the stun but the monster
                    // was already actionable.  A snapshot restore must put the
                    // cursor back without running Enter/Exit side effects.
                    AccessTools.Field(typeof(MonsterMoveStateMachine), "_currentState")
                        ?.SetValue(machine, restoredState);
                }

                // Write NextMove after the cursor.  Some state-machine transition
                // helpers replace this property as a side effect; restoring it
                // last keeps Creature.IsStunned and the intent UI in agreement.
                var nextMoveBackingField = AccessTools.Field(typeof(MonsterModel), "<NextMove>k__BackingField");
                if (nextMoveBackingField is not null)
                    nextMoveBackingField.SetValue(monster, saved.NextMove);
                else
                    SetPropertyOrField(monster, "NextMove", saved.NextMove);
                if (saved.PerformedFirstMove.HasValue)
                    AccessTools.Field(typeof(MonsterMoveStateMachine), "_performedFirstMove")?.SetValue(machine, saved.PerformedFirstMove.Value);
                if (saved.StateLog is not null)
                {
                    machine.StateLog.Clear();
                    machine.StateLog.AddRange(saved.StateLog);
                }
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[TurnRewind] failed to restore monster move state at index {saved.Index}: {ex.Message}");
            }
        }
    }

    private static void ApplyCreatureVitals(Creature creature, int hp, int maxHp, int block)
    {
        creature.SetMaxHpInternal(maxHp);
        creature.SetCurrentHpInternal(hp);
        if (creature.Block > block)
            creature.LoseBlockInternal(creature.Block - block);
        else if (creature.Block < block)
            creature.GainBlockInternal(block - creature.Block);
    }

    private static void RestorePowers(Creature creature, IReadOnlyList<PowerExtraSnapshot> powers)
    {
        foreach (var power in creature.RemoveAllPowersInternalExcept().ToList())
        {
            // RemoveAllPowersInternalExcept already detaches; enumerating forces completion.
        }

        foreach (var saved in powers)
        {
            try
            {
                // ToMutable(saved.Amount) changes the amount before Owner is set.
                // PowerModel.SetAmount then calls Owner.InvokePowerModified and
                // throws NullReferenceException for every non-zero buff/debuff.
                // Create at zero, attach it silently, then restore turn metadata.
                var power = ModelDb.GetById<PowerModel>(saved.Id).ToMutable();
                power.ApplyInternal(creature, saved.Amount, silent: true);
                power.AmountOnTurnStart = saved.AmountOnTurnStart;
                power.SkipNextDurationTick = saved.SkipNextDurationTick;
                SetPropertyOrField(power, "_applier", saved.Applier);
                SetPropertyOrField(power, "_target", saved.Target);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[TurnRewind] failed to restore power {saved.Id}: {ex.Message}");
            }
        }
    }

    private static void RestoreMonsterRuntimeFields(MonsterModel monster, IReadOnlyList<MonsterRuntimeFieldSnapshot> fields)
    {
        foreach (var saved in fields)
        {
            try
            {
                var declaringType = Type.GetType(saved.DeclaringType, throwOnError: false) ??
                    FindType(saved.DeclaringType.Split(',')[0]);
                var field = declaringType?.GetField(
                    saved.FieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.DeclaredOnly);
                if (field is not null && field.DeclaringType?.IsInstanceOfType(monster) == true)
                    field.SetValue(monster, saved.Value);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[TurnRewind] failed to restore monster field {saved.FieldName}: {ex.Message}");
            }
        }
    }

    private static void RebuildNonPlayerCreatureNodes(CombatState state)
    {
        var room = NCombatRoom.Instance;
        if (room is null)
            return;

        try
        {
            var activeNodes = AccessTools.Field(typeof(NCombatRoom), "_creatureNodes")?.GetValue(room) as IList;
            var removingNodes = AccessTools.Field(typeof(NCombatRoom), "_removingCreatureNodes")?.GetValue(room) as IList;

            RemoveNonPlayerCreatureNodes(activeNodes);
            RemoveNonPlayerCreatureNodes(removingNodes);

            foreach (var creature in state.Creatures.Where(creature => !creature.IsPlayer))
                room.AddCreature(creature);

            var allNodes = room.CreatureNodes.ToList();
            var allyNodes = allNodes.Where(node => node.Entity.Side == CombatSide.Player).ToList();
            if (allyNodes.Count > 0)
            {
                var scaling = state.Encounter?.GetCameraScaling() ?? 1f;
                NCombatRoom.PositionPlayersAndPets(
                    allyNodes,
                    scaling,
                    state.Encounter?.FullyCenterPlayers ?? false);
            }

            var enemyNodes = allNodes.Where(node => node.Entity.Side == CombatSide.Enemy).ToList();
            var encounterSlots = AccessTools.Field(typeof(NCombatRoom), "<EncounterSlots>k__BackingField")?.GetValue(room)
                ?? AccessTools.Field(typeof(NCombatRoom), "EncounterSlots")?.GetValue(room);
            if (enemyNodes.Count > 0 && encounterSlots is null)
            {
                var scaling = state.Encounter?.GetCameraScaling() ?? 1f;
                AccessTools.Method(typeof(NCombatRoom), "PositionEnemies")?.Invoke(room, [enemyNodes, scaling]);
            }

            AccessTools.Method(typeof(NCombatRoom), "UpdateCreatureNavigation")?.Invoke(room, null);
            MainFile.Logger.Info($"[TurnRewind] rebuilt non-player creature nodes: {state.Creatures.Count(creature => !creature.IsPlayer)}.");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] creature visual rebuild failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void RemoveNonPlayerCreatureNodes(IList? nodes)
    {
        if (nodes is null)
            return;

        foreach (var node in nodes.Cast<object?>().OfType<NCreature>().Where(node => !node.Entity.IsPlayer).ToList())
        {
            try { nodes.Remove(node); } catch { }
            try { node.DeathAnimCancelToken.Cancel(); } catch { }
            try { node.Visible = false; } catch { }
            try { node.GetParent()?.RemoveChild(node); } catch { }
            try { node.QueueFree(); } catch { }
        }
    }

    private static void RestorePlayers(CombatState state, TurnSnapshot snapshot)
    {
        var fullState = snapshot.FullState;
        var savedPlayers = GetEnumerableMember(fullState, "Players").Cast<object>().ToList();
        MainFile.Logger.Info($"[TurnRewind] restoring players reflective count={savedPlayers.Count}.");

        foreach (var saved in savedPlayers)
        {
            var savedPlayerId = GetMember<object>(saved, "playerId", "PlayerId");
            var player = state.Players.FirstOrDefault(p => ValuesEqual(p.NetId, savedPlayerId));
            if (player?.PlayerCombatState is null)
                continue;

            player.Gold = GetMember(saved, "gold", "Gold", player.Gold);
            TryRestorePlayerRng(player, saved);
            TryRestoreRelicGrabBag(player, saved);

            var pcs = player.PlayerCombatState;
            var savedTurn = GetSavedTurnNumber(saved, state.RoundNumber);
            SetTurnNumber(pcs, savedTurn <= 0 ? 1 : savedTurn);
            TrySetPlayerPhase(pcs, GetMember<object>(saved, "phase", "Phase"));
            pcs.Energy = GetMember(saved, "energy", "Energy", pcs.Energy);
            pcs.Stars = GetMember(saved, "stars", "Stars", pcs.Stars);
            try
            {
                if (!RestorePotionsFromSnapshot(player, snapshot))
                    RestorePotionsReflective(player, saved);
            }
            catch (Exception ex) { MainFile.Logger.Warn($"[TurnRewind] potion state restore failed before pile restore for {player.NetId}: {ex.GetType().Name}: {ex.Message}"); }
            RestorePilesReflective(state, player, GetEnumerableMember(saved, "piles", "Piles"));
            if (!RestoreOrbsFromSnapshot(player, snapshot))
                RestoreOrbsReflective(player, GetEnumerableMember(saved, "orbs", "Orbs"));
            RebuildOrbUiAfterRestore(player);
            pcs.RecalculateCardValues();
            MainFile.Logger.Info($"[TurnRewind] restored player {player.NetId}: turn={GetTurnNumber(pcs, state.RoundNumber)}, phase={GetPlayerPhase(pcs)}, energy={pcs.Energy}, hand={pcs.Hand.Cards.Count}, draw={pcs.DrawPile.Cards.Count}, discard={pcs.DiscardPile.Cards.Count}, exhaust={pcs.ExhaustPile.Cards.Count}, play={pcs.PlayPile.Cards.Count}, potions={CountPlayerPotions(player)}.");
        }
    }

    private static void SetTurnNumber(PlayerCombatState pcs, int value)
    {
        var setter = AccessTools.PropertySetter(typeof(PlayerCombatState), "TurnNumber");
        if (setter is not null)
        {
            setter.Invoke(pcs, [value]);
            return;
        }
        AccessTools.Field(typeof(PlayerCombatState), "<TurnNumber>k__BackingField")?.SetValue(pcs, value);
    }

    private static int GetTurnNumber(PlayerCombatState pcs, int fallback)
    {
        try
        {
            var prop = AccessTools.Property(typeof(PlayerCombatState), "TurnNumber");
            if (prop?.GetValue(pcs) is int value)
                return value;
        }
        catch { }

        try
        {
            var field = AccessTools.Field(typeof(PlayerCombatState), "<TurnNumber>k__BackingField");
            if (field?.GetValue(pcs) is int value)
                return value;
        }
        catch { }

        return fallback;
    }

    private static object? GetPlayerPhase(PlayerCombatState pcs)
    {
        try { return AccessTools.Property(typeof(PlayerCombatState), "Phase")?.GetValue(pcs); } catch { }
        try { return AccessTools.Field(typeof(PlayerCombatState), "<Phase>k__BackingField")?.GetValue(pcs); } catch { }
        return null;
    }

    private static int GetSavedTurnNumber(object saved, int fallback)
    {
        try
        {
            var type = saved.GetType();
            if (type.GetField("turnNumber")?.GetValue(saved) is int fieldValue)
                return fieldValue;
            if (type.GetProperty("turnNumber")?.GetValue(saved) is int propValue)
                return propValue;
            if (type.GetField("TurnNumber")?.GetValue(saved) is int fieldValue2)
                return fieldValue2;
            if (type.GetProperty("TurnNumber")?.GetValue(saved) is int propValue2)
                return propValue2;
        }
        catch { }
        return fallback;
    }

    private static void TrySetPlayerPhase(PlayerCombatState pcs, object? phaseValue)
    {
        try
        {
            var prop = AccessTools.Property(typeof(PlayerCombatState), "Phase");
            if (prop is null || !prop.CanWrite)
                return;

            var phaseType = prop.PropertyType;
            object? phase = null;
            var phaseName = phaseValue?.ToString();
            if (phaseName == "None")
                phaseName = "Play";

            if (phaseValue is not null && phaseType.IsInstanceOfType(phaseValue))
                phase = phaseName == "Play" ? Enum.Parse(phaseType, "Play") : phaseValue;
            else if (!string.IsNullOrEmpty(phaseName))
                phase = Enum.Parse(phaseType, phaseName, ignoreCase: true);
            else
                phase = Enum.Parse(phaseType, "Play");

            prop.SetValue(pcs, phase);
        }
        catch { }
    }

    private static void TryRestorePlayerRng(Player player, object saved)
    {
        try
        {
            var rngSet = GetMember<object>(saved, "rngSet", "RngSet");
            if (rngSet is not null)
                player.PlayerRng.GetType().GetMethod("LoadFromSerializable")?.Invoke(player.PlayerRng, [rngSet]);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] player RNG restore skipped for {player.NetId}: {ex.Message}");
        }
    }

    private static void TryRestoreRelicGrabBag(Player player, object saved)
    {
        try
        {
            var bag = GetMember<object>(saved, "relicGrabBag", "RelicGrabBag");
            if (bag is not null)
                player.RelicGrabBag.GetType().GetMethod("LoadFromSerializable")?.Invoke(player.RelicGrabBag, [bag]);
        }
        catch { }
    }

    private static bool RestorePotionsFromSnapshot(Player player, TurnSnapshot snapshot)
    {
        var bar = snapshot.PotionBars.FirstOrDefault(p => ValuesEqual(p.PlayerId, player.NetId));
        if (bar is null)
            return false;

        try
        {
            ReplacePotionSlots(player, bar.SlotPotionIds, bar.MaxPotionCount, bar.CanRemovePotions, preserveSlots: true);
            MainFile.Logger.Info($"[TurnRewind] restored potion bar from snapshot for {player.NetId}: slots={player.MaxPotionCount}, potions={CountPlayerPotions(player)}.");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] snapshot potion restore failed for {player.NetId}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void RestorePotionsReflective(Player player, object saved)
    {
        try
        {
            var maxPotionCount = GetMember(saved, "maxPotionCount", "MaxPotionCount", player.MaxPotionCount);
            var potionIds = GetEnumerableMember(saved, "potions", "Potions")
                .Cast<object>()
                .Select(savedPotion => GetMember<object>(savedPotion, "id", "Id"))
                .Where(id => id is not null)
                .Cast<object?>()
                .ToList();

            ReplacePotionSlots(player, potionIds, maxPotionCount, canRemovePotions: true, preserveSlots: false);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] potion state restore skipped for {player.NetId}: {ex.Message}");
        }
    }

    private static void ReplacePotionSlots(Player player, IReadOnlyList<object?> potionIds, int requestedMaxPotionCount, bool canRemovePotions, bool preserveSlots)
    {
        if (AccessTools.Field(typeof(Player), "_potionSlots")?.GetValue(player) is not IList slots)
        {
            MainFile.Logger.Warn("[TurnRewind] Player._potionSlots not found; potion model restore skipped.");
            return;
        }

        foreach (var oldPotion in slots.Cast<object?>().Where(p => p is not null).ToList())
        {
            try
            {
                SetPotionRuntimeFlags(oldPotion!, isQueued: false, removed: true);
            }
            catch { }
        }

        var maxPotionCount = Math.Max(0, Math.Max(requestedMaxPotionCount, potionIds.Count));
        slots.Clear();
        for (var i = 0; i < maxPotionCount; i++)
            slots.Add(null);

        var nextSequentialSlot = 0;
        for (var i = 0; i < potionIds.Count; i++)
        {
            var potionId = potionIds[i];
            if (potionId is null)
            {
                if (!preserveSlots)
                    nextSequentialSlot++;
                continue;
            }

            var slotIndex = preserveSlots ? i : nextSequentialSlot++;
            if (slotIndex < 0 || slotIndex >= slots.Count)
                continue;

            try
            {
                var potion = CreatePotionModel(potionId);
                if (potion is null)
                    continue;
                SetPropertyOrField(potion, "Owner", player);
                SetPotionRuntimeFlags(potion, isQueued: false, removed: false);
                slots[slotIndex] = potion;
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[TurnRewind] failed to restore potion {potionId}: {ex.Message}");
            }
        }

        SetCanUseOrRemovePotions(player, canRemovePotions);
    }

    private static bool GetCanUseOrRemovePotions(Player player)
    {
        try
        {
            return CanUseOrRemovePotionsProperty?.GetValue(player) as bool? ?? true;
        }
        catch
        {
            return true;
        }
    }

    private static void SetCanUseOrRemovePotions(Player player, bool value)
    {
        try
        {
            CanUseOrRemovePotionsProperty?.SetValue(player, value);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] potion availability flag restore skipped: {ex.Message}");
        }
    }

    private static void SetPotionRuntimeFlags(object potion, bool isQueued, bool removed)
    {
        var type = potion.GetType();
        try { AccessTools.PropertySetter(type, "IsQueued")?.Invoke(potion, [isQueued]); } catch { }
        try { AccessTools.PropertySetter(type, "HasBeenRemovedFromState")?.Invoke(potion, [removed]); } catch { }
        try { AccessTools.Field(type, "<IsQueued>k__BackingField")?.SetValue(potion, isQueued); } catch { }
        try { AccessTools.Field(type, "<HasBeenRemovedFromState>k__BackingField")?.SetValue(potion, removed); } catch { }
    }

    private static void RestorePiles(CombatState state, Player player, List<NetFullCombatState.CombatPileState> savedPiles)
    {
        var pcs = player.PlayerCombatState!;
        foreach (var pile in pcs.AllPiles)
        {
            foreach (var card in pile.Cards.ToList())
            {
                pile.RemoveInternal(card, silent: false);
                state.RemoveCard(card);
            }
            pile.Clear(silent: false);
        }

        foreach (var savedPile in savedPiles)
        {
            var pile = GetPile(pcs, savedPile.pileType);
            if (pile is null)
                continue;

            foreach (var savedCard in savedPile.cards)
            {
                try
                {
                    var card = CardModel.FromSerializable(savedCard.card);
                    state.AddCard(card, player);
                    RestoreCardLocalState(card, savedCard);
                    pile.AddInternal(card, silent: false);
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Warn($"[TurnRewind] failed to restore card in {savedPile.pileType}: {ex.Message}");
                }
            }
            pile.InvokeCardAddFinished();
            pile.InvokeContentsChanged();
        }
    }

    private static CardPile? GetPile(PlayerCombatState pcs, PileType pileType) => pileType switch
    {
        PileType.Hand => pcs.Hand,
        PileType.Draw => pcs.DrawPile,
        PileType.Discard => pcs.DiscardPile,
        PileType.Exhaust => pcs.ExhaustPile,
        PileType.Play => pcs.PlayPile,
        _ => null
    };

    private static void RestorePilesReflective(CombatState state, Player player, IEnumerable savedPiles)
    {
        var pcs = player.PlayerCombatState!;
        foreach (var pile in pcs.AllPiles)
        {
            foreach (var card in pile.Cards.ToList())
            {
                pile.RemoveInternal(card, silent: false);
                state.RemoveCard(card);
            }
            pile.Clear(silent: false);
        }

        foreach (var savedPile in savedPiles.Cast<object>())
        {
            var pileTypeValue = GetMember<object>(savedPile, "pileType", "PileType");
            if (!TryConvertPileType(pileTypeValue, out var pileType))
                continue;

            var pile = GetPile(pcs, pileType);
            if (pile is null)
                continue;

            foreach (var savedCard in GetEnumerableMember(savedPile, "cards", "Cards").Cast<object>())
            {
                try
                {
                    var serializableCard = GetMember<object>(savedCard, "card", "Card");
                    var card = CreateCardFromSerializable(serializableCard);
                    if (card is null)
                        continue;
                    state.AddCard(card, player);
                    RestoreCardLocalStateReflective(card, savedCard);
                    pile.AddInternal(card, silent: false);
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Warn($"[TurnRewind] failed to restore card in {pileType}: {ex.Message}");
                }
            }
            pile.InvokeCardAddFinished();
            pile.InvokeContentsChanged();
        }
    }

    private static bool TryConvertPileType(object? value, out PileType pileType)
    {
        if (value is PileType typed)
        {
            pileType = typed;
            return true;
        }

        if (value is not null && Enum.TryParse(value.ToString(), out pileType))
            return true;

        pileType = default;
        return false;
    }

    private static CardModel? CreateCardFromSerializable(object? serializableCard)
    {
        if (serializableCard is null)
            return null;

        foreach (var method in typeof(CardModel).GetMethods().Where(m => m.Name == "FromSerializable" && m.IsStatic))
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || !parameters[0].ParameterType.IsInstanceOfType(serializableCard))
                continue;
            try { return method.Invoke(null, [serializableCard]) as CardModel; } catch { }
        }
        return null;
    }

    private static void RestoreCardLocalStateReflective(CardModel card, object savedCard)
    {
        var afflictionId = GetMember<object>(savedCard, "affliction", "Affliction");
        if (afflictionId is not null)
        {
            try
            {
                var affliction = ModelDb.GetById<AfflictionModel>((ModelId)afflictionId).ToMutable();
                card.AfflictInternal(affliction, GetMember(savedCard, "afflictionCount", "AfflictionCount", 0));
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[TurnRewind] failed to restore affliction {afflictionId}: {ex.Message}");
            }
        }

        var energyCost = GetNullableIntMember(savedCard, "energyCost", "EnergyCost");
        if (energyCost.HasValue)
        {
            try
            {
                card.EnergyCost.SetThisTurn(energyCost.Value);
                card.InvokeEnergyCostChanged();
            }
            catch { }
        }

        foreach (var keyword in GetEnumerableMember(savedCard, "keywords", "Keywords").Cast<object>())
        {
            try
            {
                InvokeByName(card, "AddKeyword", keyword);
            }
            catch { }
        }
    }

    private static void RestoreCardLocalState(CardModel card, NetFullCombatState.CardState savedCard)
    {
        if (savedCard.affliction is not null)
        {
            try
            {
                var affliction = ModelDb.GetById<AfflictionModel>(savedCard.affliction).ToMutable();
                card.AfflictInternal(affliction, savedCard.afflictionCount);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[TurnRewind] failed to restore affliction {savedCard.affliction}: {ex.Message}");
            }
        }

        if (savedCard.energyCost.HasValue)
        {
            try
            {
                card.EnergyCost.SetThisTurn(savedCard.energyCost.Value);
                card.InvokeEnergyCostChanged();
            }
            catch { }
        }

        if (savedCard.keywords is not null)
        {
            foreach (var keyword in savedCard.keywords)
            {
                try { card.AddKeyword(keyword); } catch { }
            }
        }
    }

    private static void RestoreOrbs(Player player, List<NetFullCombatState.OrbState> savedOrbs)
    {
        var queue = player.PlayerCombatState!.OrbQueue;
        queue.Clear();
        queue.AddCapacity(Math.Max(player.BaseOrbSlotCount, savedOrbs.Count));
        foreach (var saved in savedOrbs)
        {
            try
            {
                var orb = ModelDb.GetById<OrbModel>(saved.id).ToMutable();
                queue.Insert(queue.Orbs.Count, orb);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[TurnRewind] failed to restore orb {saved.id}: {ex.Message}");
            }
        }
    }

    private static void RefreshCombatUiAfterRestore(CombatState state)
    {
        try
        {
            var ui = NCombatRoom.Instance?.Ui;
            if (ui is null)
                return;

            var player = state.Players.FirstOrDefault();
            if (player?.PlayerCombatState is not { } pcs)
                return;

            ResetPlayQueueAndPlayArea(ui);
            RebuildHandUi(ui.Hand, pcs.Hand.Cards);
            RestoreHandToPlayableState(ui.Hand, state);
            ForceRefreshEnergyAndStars(ui, pcs);
            ForceSetPileCount(ui.DrawPile, pcs.DrawPile.Cards.Count);
            ForceSetPileCount(ui.DiscardPile, pcs.DiscardPile.Cards.Count);
            ForceSetPileCount(ui.ExhaustPile, pcs.ExhaustPile.Cards.Count);
            RefreshPotionUi(state, player);
            ui.EndTurnButton.Initialize(state);
            AccessTools.Method(typeof(NEndTurnButton), "OnTurnStarted")?.Invoke(ui.EndTurnButton, [state]);

            var room = NCombatRoom.Instance;
            if (room is null)
                return;

            foreach (var creatureNode in room.CreatureNodes)
            {
                try
                {
                    if (creatureNode.Entity?.Monster is not null)
                        TaskHelper.RunSafely(creatureNode.RefreshIntents());
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] combat UI refresh skipped: {ex.Message}");
        }
    }

    private static void RebuildHandUi(NPlayerHand hand, IReadOnlyList<CardModel> handCards)
    {
        try { hand.CancelAllCardPlay(); } catch { }
        try { AccessTools.Method(typeof(NPlayerHand), "CancelHandSelectionIfNecessary")?.Invoke(hand, null); } catch { }
        ClearHolderContainer(hand.CardHolderContainer, hand);
        ClearHolderContainer(hand, hand);

        var selected = AccessTools.Field(typeof(NPlayerHand), "_selectedHandCardContainer")?.GetValue(hand) as Control;
        if (selected is not null)
            ClearHolderContainer(selected, hand);
        ClearAwaitingHandQueue(hand);

        foreach (var card in handCards)
        {
            var cardNode = NCard.Create(card, ModelVisibility.Visible);
            if (cardNode is not null)
                hand.Add(cardNode);
        }
        hand.ForceRefreshCardIndices();
    }

    private static void RestoreHandToPlayableState(NPlayerHand hand, CombatState state)
    {
        try { AccessTools.PropertySetter(typeof(NPlayerHand), "CurrentMode")?.Invoke(hand, [NPlayerHand.Mode.Play]); } catch { }
        try { AccessTools.Field(typeof(NPlayerHand), "_currentCardPlay")?.SetValue(hand, null); } catch { }
        try { AccessTools.Field(typeof(NPlayerHand), "_draggedHolderIndex")?.SetValue(hand, -1); } catch { }
        try { AccessTools.Field(typeof(NPlayerHand), "_isDisabled")?.SetValue(hand, false); } catch { }
        try { (AccessTools.Field(typeof(NPlayerHand), "_selectedCards")?.GetValue(hand) as IList)?.Clear(); } catch { }
        try { ClearAwaitingHandQueue(hand); } catch { }
        try { AccessTools.Method(typeof(NPlayerHand), "OnCombatStateChanged")?.Invoke(hand, [state]); } catch { }
        try { AccessTools.Method(typeof(NPlayerHand), "OnPlayerActionsDisabledChanged")?.Invoke(hand, [state]); } catch { }
        try { AccessTools.Method(typeof(NPlayerHand), "AnimEnable")?.Invoke(hand, null); } catch { }
        try { hand.FlashPlayableHolders(); } catch { }
    }

    private static void ResetPlayQueueAndPlayArea(NCombatUi ui)
    {
        try
        {
            if (AccessTools.Field(typeof(NCardPlayQueue), "_playQueue")?.GetValue(ui.PlayQueue) is IList queue)
                queue.Clear();
        }
        catch { }

        try
        {
            foreach (var card in ui.PlayContainer.GetChildren().OfType<NCard>().ToList())
            {
                try { ui.PlayContainer.RemoveChild(card); } catch { }
                try { card.QueueFree(); } catch { }
            }
        }
        catch { }
    }

    private static void ForceRefreshEnergyAndStars(NCombatUi ui, PlayerCombatState pcs)
    {
        try
        {
            var energyCounter = AccessTools.Field(typeof(NCombatUi), "_energyCounter")?.GetValue(ui);
            energyCounter?.GetType().GetMethod("OnEnergyChanged")?.Invoke(energyCounter, [pcs.Energy, pcs.Energy]);
            energyCounter?.GetType().GetMethod("Refresh")?.Invoke(energyCounter, null);
        }
        catch { }

        try
        {
            var starCounter = AccessTools.Field(typeof(NCombatUi), "_starCounter")?.GetValue(ui);
            starCounter?.GetType().GetMethod("OnStarsChanged")?.Invoke(starCounter, [pcs.Stars, pcs.Stars]);
            starCounter?.GetType().GetMethod("Refresh")?.Invoke(starCounter, null);
        }
        catch { }
    }

    private static void RefreshPotionUi(CombatState state, Player player)
    {
        try
        {
            var root = NCombatRoom.Instance?.GetTree()?.Root;
            if (root is null)
                return;

            var containerType = FindType("MegaCrit.Sts2.Core.Nodes.Potions.NPotionContainer");
            if (containerType is null)
            {
                MainFile.Logger.Warn("[TurnRewind] potion UI refresh skipped: NPotionContainer type not found.");
                return;
            }

            var refreshed = 0;
            foreach (var container in FindNodesByType(root, containerType))
            {
                try
                {
                    RebuildPotionContainer(container, state, player);
                    refreshed++;
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Warn($"[TurnRewind] failed to refresh potion UI container: {ex.Message}");
                }
            }

            if (refreshed > 0)
                MainFile.Logger.Info($"[TurnRewind] refreshed potion UI containers={refreshed}, potions={player.Potions.Count()}.");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] potion UI refresh skipped: {ex.Message}");
        }
    }

    private static void RebuildPotionContainer(Node container, CombatState state, Player player)
    {
        var containerType = container.GetType();
        try { InvokeByName(container, "DisconnectPlayerEvents"); } catch { }
        try { SetPropertyOrField(container, "_player", player); } catch { }
        try { InvokeByName(container, "ConnectPlayerEvents"); } catch { }
        AccessTools.Method(containerType, "GrowPotionHolders")?.Invoke(container, [player.MaxPotionCount]);
        var holders = (AccessTools.Field(containerType, "_holders")?.GetValue(container) as IEnumerable)?.Cast<object>().ToList();
        if (holders is null || holders.Count == 0)
            return;

        foreach (var holder in holders)
            ClearPotionHolderImmediate(holder);

        var potions = GetPotionSlots(player).Cast<object>().ToList();
        for (var i = 0; i < potions.Count && i < holders.Count; i++)
        {
            var potion = potions[i];
            if (potion is null)
                continue;
            SetPropertyOrField(potion, "Owner", player);
            SetPotionRuntimeFlags(potion, isQueued: false, removed: false);
            var node = CreatePotionNodeReflective(potion);
            if (node is null)
                continue;
            if (node is Node2D node2D)
                node2D.Position = new Vector2(-30f, -30f);
            else if (node is Control control)
                control.Position = new Vector2(-30f, -30f);
            InvokeByName(holders[i], "AddPotion", node);
            InvokeByName(holders[i], "CancelPotionUseOrDiscard");
        }

        AccessTools.Method(containerType, "UpdateNavigation")?.Invoke(container, null);
    }

    private static void ClearPotionHolderImmediate(object holder)
    {
        try { InvokeByName(holder, "CancelPotionUseOrDiscard"); } catch { }
        var holderType = holder.GetType();

        try
        {
            if (AccessTools.Field(holderType, "_popup")?.GetValue(holder) is Node popup)
            {
                try { InvokeByName(popup, "Remove"); } catch { }
                try { popup.QueueFree(); } catch { }
                AccessTools.Field(holderType, "_popup")?.SetValue(holder, null);
            }
        }
        catch { }

        try
        {
            var potionNode = AccessTools.Property(holderType, "Potion")?.GetValue(holder) as Node;
            if (potionNode is not null)
            {
                try { ((Node)holder).RemoveChild(potionNode); } catch { }
                try { potionNode.QueueFree(); } catch { }
            }

            AccessTools.PropertySetter(holderType, "Potion")?.Invoke(holder, [null]);
            AccessTools.Field(holderType, "_disabledUntilPotionRemoved")?.SetValue(holder, false);
            if (holder is CanvasItem canvasItem)
                canvasItem.Modulate = Colors.White;
        }
        catch { }
    }

    private static IEnumerable<Node> FindNodesByType(Node root, Type type)
    {
        if (type.IsInstanceOfType(root))
            yield return root;

        foreach (var child in root.GetChildren())
        {
            if (child is Node node)
            {
                foreach (var match in FindNodesByType(node, type))
                    yield return match;
            }
        }
    }

    private static bool RestoreOrbsFromSnapshot(Player player, TurnSnapshot snapshot)
    {
        try
        {
            var savedQueue = snapshot.OrbQueues.FirstOrDefault(q => ValuesEqual(q.PlayerId, player.NetId));
            if (savedQueue is null || player.PlayerCombatState is null)
                return false;

            var queue = player.PlayerCombatState.OrbQueue;
            queue.Clear();
            var capacity = Math.Max(0, Math.Max(savedQueue.Capacity, savedQueue.Orbs.Count));
            queue.AddCapacity(capacity);

            var restored = 0;
            foreach (var savedOrb in savedQueue.Orbs)
            {
                if (savedOrb.Id is null)
                    continue;

                try
                {
                    var orb = CreateOrbModel(savedOrb.Id);
                    if (orb is null)
                    {
                        MainFile.Logger.Warn($"[TurnRewind] snapshot orb model not found: {savedOrb.DebugId}.");
                        continue;
                    }

                    orb.Owner = player;
                    ApplySavedOrbValues(orb, savedOrb);
                    queue.Insert(queue.Orbs.Count, orb);
                    restored++;
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Warn($"[TurnRewind] failed to restore snapshot orb {savedOrb.DebugId}: {ex.Message}");
                }
            }

            MainFile.Logger.Info($"[TurnRewind] restored orb queue from dedicated snapshot: capacity={queue.Capacity}, orbs={restored}, saved=[{DescribeOrbs(savedQueue.Orbs)}].");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] dedicated orb snapshot restore failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void RestoreOrbsReflective(Player player, IEnumerable savedOrbs)
    {
        var savedList = savedOrbs.Cast<object>().ToList();
        var queue = player.PlayerCombatState!.OrbQueue;
        queue.Clear();
        queue.AddCapacity(Math.Max(player.BaseOrbSlotCount, savedList.Count));
        var restored = 0;
        foreach (var saved in savedList)
        {
            var id = GetMember<object>(saved, "id", "Id");
            if (id is null)
                continue;
            try
            {
                var orb = CreateOrbModel(id);
                if (orb is not null)
                {
                    orb.Owner = player;
                    ApplySavedOrbValues(orb, saved);
                    queue.Insert(queue.Orbs.Count, orb);
                    restored++;
                }
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[TurnRewind] failed to restore orb {id}: {ex.Message}");
            }
        }
        MainFile.Logger.Info($"[TurnRewind] restored orb queue: capacity={queue.Capacity}, orbs={restored}.");
    }

    private static void ApplySavedOrbValues(OrbModel orb, object saved)
    {
        try
        {
            var savedPassive = GetNullableIntMember(saved, "passive", "Passive");
            var savedEvoke = GetNullableIntMember(saved, "evoke", "Evoke");

            if (savedPassive.HasValue)
                TrySetDecimalField(orb, "_passiveVal", savedPassive.Value);
            if (savedEvoke.HasValue)
                TrySetDecimalField(orb, "_evokeVal", savedEvoke.Value);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] orb value restore skipped for {SafeId(orb)}: {ex.Message}");
        }
    }

    private static void ApplySavedOrbValues(OrbModel orb, OrbSnapshot saved)
    {
        try
        {
            foreach (var kv in saved.DecimalFields)
                TrySetDecimalField(orb, kv.Key, kv.Value);

            if (saved.DecimalFields.Count == 0)
            {
                TrySetDecimalField(orb, "_passiveVal", saved.Passive);
                TrySetDecimalField(orb, "_evokeVal", saved.Evoke);
            }

            SetPropertyOrField(orb, "HasBeenRemovedFromState", false);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] dedicated orb value restore skipped for {saved.DebugId}: {ex.Message}");
        }
    }

    private static void TrySetDecimalField(object target, string fieldName, int value)
    {
        TrySetDecimalField(target, fieldName, (decimal)value);
    }

    private static void TrySetDecimalField(object target, string fieldName, decimal value)
    {
        try
        {
            var field = AccessTools.Field(target.GetType(), fieldName);
            if (field is null)
                return;

            object converted = field.FieldType == typeof(decimal)
                ? value
                : Convert.ChangeType(value, field.FieldType);
            field.SetValue(target, converted);
        }
        catch { }
    }

    private static string SafeId(OrbModel? orb)
    {
        if (orb is null)
            return "<null>";
        try { return orb.Id.ToString(); } catch { }
        return orb.GetType().FullName ?? orb.GetType().Name;
    }

    private static object? GetOrbId(OrbModel? orb)
    {
        if (orb is null)
            return null;
        try { return orb.Id; } catch { }
        return null;
    }

    private static int SafeInt(decimal value)
    {
        try { return (int)value; } catch { return 0; }
    }

    private static string DescribeOrbQueues(IEnumerable<OrbQueueSnapshot> queues)
    {
        try
        {
            return string.Join("; ", queues.Select(q => $"player={q.PlayerId}:cap={q.Capacity}:orbs=[{DescribeOrbs(q.Orbs)}]"));
        }
        catch { return "<describe-failed>"; }
    }

    private static string DescribeOrbs(IEnumerable<OrbSnapshot> orbs)
    {
        try
        {
            return string.Join(",", orbs.Select(o =>
            {
                var fields = o.DecimalFields.Count == 0
                    ? ""
                    : "{" + string.Join("/", o.DecimalFields.Select(kv => $"{kv.Key}={kv.Value:0}")) + "}";
                return $"{o.DebugId}(p={o.Passive},e={o.Evoke}){fields}";
            }));
        }
        catch { return "<describe-failed>"; }
    }

    private static void RebuildOrbUiAfterRestore(Player player)
    {
        try
        {
            var manager = NCombatRoom.Instance?.GetCreatureNode(player.Creature)?.OrbManager;
            if (manager is null || player.PlayerCombatState is null)
                return;

            var queue = player.PlayerCombatState.OrbQueue;
            var queueOrbs = queue.Orbs.ToList();
            var managerType = manager.GetType();
            var visualList = AccessTools.Field(managerType, "_orbs")?.GetValue(manager) as IList;
            var orbContainer = AccessTools.Field(managerType, "_orbContainer")?.GetValue(manager) as Node;
            if (visualList is null || orbContainer is null)
            {
                InvokeOrbManagerVisualRefresh(manager);
                MainFile.Logger.Warn("[TurnRewind] orb UI rebuild fell back to visual refresh; private fields not found.");
                return;
            }

            try { (AccessTools.Field(managerType, "_curTween")?.GetValue(manager) as Tween)?.Kill(); } catch { }

            foreach (var oldVisual in visualList.Cast<object>().ToList())
                RemoveOrbVisualNode(oldVisual);
            visualList.Clear();

            var orbNodeType = FindType("MegaCrit.Sts2.Core.Nodes.Orbs.NOrb");
            var createMethod = orbNodeType?.GetMethods()
                .FirstOrDefault(m =>
                {
                    if (m.Name != "Create" || !m.IsStatic)
                        return false;
                    var p = m.GetParameters();
                    return p.Length == 2 && p[0].ParameterType == typeof(bool);
                });
            if (createMethod is null)
            {
                MainFile.Logger.Warn("[TurnRewind] orb UI rebuild skipped: NOrb.Create(bool, OrbModel) not found.");
                return;
            }

            var isLocal = GetMember(manager, "IsLocal", "isLocal", true);
            for (var i = 0; i < queue.Capacity; i++)
            {
                var orb = i < queueOrbs.Count ? queueOrbs[i] : null;
                if (orb is not null)
                    orb.Owner = player;

                var node = createMethod.Invoke(null, [isLocal, orb]) as Node;
                if (node is null)
                    continue;

                orbContainer.AddChild(node);
                visualList.Add(node);
                if (node is Control control)
                    control.Position = Vector2.Zero;
                else if (node is Node2D node2D)
                    node2D.Position = Vector2.Zero;
            }

            InvokeByName(manager, "TweenLayout");
            InvokeByName(manager, "UpdateControllerNavigation");
            InvokeOrbManagerVisualRefresh(manager);
            MainFile.Logger.Info($"[TurnRewind] rebuilt orb UI after rewind: slots={queue.Capacity}, orbs={queueOrbs.Count}.");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[TurnRewind] orb UI rebuild skipped: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void RemoveOrbVisualNode(object? visual)
    {
        if (visual is not Node node)
            return;

        try
        {
            if (node is CanvasItem canvas)
            {
                canvas.Visible = false;
                canvas.Modulate = new Color(canvas.Modulate.R, canvas.Modulate.G, canvas.Modulate.B, 0f);
            }
        }
        catch { }

        try { node.GetParent()?.RemoveChild(node); } catch { }
        try { node.QueueFree(); } catch { }
    }

    private static void InvokeOrbManagerVisualRefresh(object manager)
    {
        try
        {
            var method = manager.GetType().GetMethods().FirstOrDefault(m => m.Name == "UpdateVisuals" && m.GetParameters().Length == 1);
            var paramType = method?.GetParameters()[0].ParameterType;
            if (method is not null && paramType is not null)
            {
                var none = paramType.IsEnum ? Enum.ToObject(paramType, 0) : Activator.CreateInstance(paramType);
                method.Invoke(manager, [none]);
            }
        }
        catch { }
    }

    private static OrbModel? CreateOrbModel(object id)
    {
        try
        {
            if (id is ModelId modelId)
                return ModelDb.GetById<OrbModel>(modelId).ToMutable();
        }
        catch { }

        var model = InvokeGenericModelDbGetById(typeof(OrbModel), id) ?? FindOrbModelByIdText(id.ToString());
        var toMutable = model?.GetType().GetMethod("ToMutable", Type.EmptyTypes);
        return toMutable?.Invoke(model, null) as OrbModel;
    }

    private static OrbModel? FindOrbModelByIdText(string? idText)
    {
        if (string.IsNullOrWhiteSpace(idText))
            return null;

        try
        {
            foreach (var orb in ModelDb.Orbs)
            {
                if (string.Equals(orb.Id.ToString(), idText, StringComparison.Ordinal) ||
                    string.Equals(orb.Id.Entry, idText, StringComparison.Ordinal) ||
                    idText.EndsWith("." + orb.Id.Entry, StringComparison.Ordinal))
                    return orb;
            }
        }
        catch { }

        try
        {
            if (AccessTools.Field(typeof(ModelDb), "_contentById")?.GetValue(null) is IDictionary content)
            {
                foreach (DictionaryEntry entry in content)
                {
                    if (entry.Value is not OrbModel orb)
                        continue;
                    var keyText = entry.Key?.ToString();
                    if (string.Equals(keyText, idText, StringComparison.Ordinal) ||
                        string.Equals(orb.Id.ToString(), idText, StringComparison.Ordinal) ||
                        string.Equals(orb.Id.Entry, idText, StringComparison.Ordinal) ||
                        idText.EndsWith("." + orb.Id.Entry, StringComparison.Ordinal))
                        return orb;
                }
            }
        }
        catch { }

        return null;
    }

    private static int CountPlayerPotions(Player? player)
    {
        if (player is null)
            return -1;
        try { return player.Potions.Count(); } catch { }
        try { return GetPotionSlots(player).Count; } catch { }
        return -1;
    }

    private static object? GetPotionId(object potion)
    {
        return GetMember<object>(potion, "Id", "id", "ModelId", "modelId");
    }

    private static IList GetPotionSlots(Player player)
    {
        if (AccessTools.Field(typeof(Player), "_potionSlots")?.GetValue(player) is IList slots)
            return slots;

        try
        {
            var slotsObj = AccessTools.Property(typeof(Player), "PotionSlots")?.GetValue(player);
            if (slotsObj is IList list)
                return list;
            if (slotsObj is IEnumerable enumerable)
                return enumerable.Cast<object>().ToList();
        }
        catch { }

        return ArrayList.ReadOnly(new ArrayList());
    }

    private static IEnumerable GetEnumerableMember(object target, params string[] names)
    {
        var value = GetMember<object>(target, names);
        if (value is IEnumerable enumerable)
            return enumerable;
        return ArrayList.ReadOnly(new ArrayList());
    }

    private static T? GetMember<T>(object target, params string[] names)
    {
        object? value = null;
        foreach (var name in names)
        {
            value = GetRawMember(target, name);
            if (value is not null)
                break;
        }
        if (value is null)
            return default;
        if (value is T typed)
            return typed;
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    private static T GetMember<T>(object target, string name1, string name2, T fallback)
    {
        var value = GetRawMember(target, name1) ?? GetRawMember(target, name2);
        if (value is null)
            return fallback;
        if (value is T typed)
            return typed;
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return fallback;
        }
    }

    private static int? GetNullableIntMember(object target, params string[] names)
    {
        var value = GetMember<object>(target, names);
        if (value is null)
            return null;
        if (value is int i)
            return i;
        try { return Convert.ToInt32(value); } catch { return null; }
    }

    private static object? GetRawMember(object target, string name)
    {
        var type = target.GetType();
        try { if (type.GetField(name)?.GetValue(target) is { } fieldValue) return fieldValue; } catch { }
        try { if (type.GetProperty(name)?.GetValue(target) is { } propValue) return propValue; } catch { }
        try { if (AccessTools.Field(type, name)?.GetValue(target) is { } accessFieldValue) return accessFieldValue; } catch { }
        try { if (AccessTools.Property(type, name)?.GetValue(target) is { } accessPropValue) return accessPropValue; } catch { }
        return null;
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        if (Equals(a, b))
            return true;
        return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }

    private static object? CreatePotionModel(object id)
    {
        var potionType = FindType("MegaCrit.Sts2.Core.Models.PotionModel") ?? typeof(PotionModel);
        if (potionType is null)
        {
            MainFile.Logger.Warn($"[TurnRewind] potion type not found while restoring {id}.");
            return null;
        }

        var model = InvokeGenericModelDbGetById(potionType, id);
        if (model is null)
        {
            MainFile.Logger.Warn($"[TurnRewind] potion model not found: {id}.");
            return null;
        }

        var toMutable = AccessTools.Method(model.GetType(), "ToMutable", []);
        if (toMutable is not null)
            return toMutable.Invoke(model, null);

        toMutable = model.GetType().GetMethods().FirstOrDefault(m => m.Name == "ToMutable" && m.GetParameters().Length == 0);
        return toMutable is not null ? toMutable.Invoke(model, null) : model;
    }

    private static object? InvokeGenericModelDbGetById(Type modelType, object id)
    {
        foreach (var method in typeof(ModelDb).GetMethods())
        {
            if (method.Name != "GetById" || !method.IsGenericMethodDefinition)
                continue;
            var parameters = method.GetParameters();
            if (parameters.Length != 1)
                continue;
            try
            {
                var arg = parameters[0].ParameterType.IsInstanceOfType(id) ? id : id.ToString();
                return method.MakeGenericMethod(modelType).Invoke(null, [arg]);
            }
            catch { }
        }

        return null;
    }

    private static Node? CreatePotionNodeReflective(object potion)
    {
        var nodeType = FindType("MegaCrit.Sts2.Core.Nodes.Potions.NPotion");
        if (nodeType is null)
            return null;

        foreach (var method in nodeType.GetMethods().Where(m => m.Name == "Create" && m.IsStatic))
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1)
                continue;
            if (!parameters[0].ParameterType.IsInstanceOfType(potion) && !parameters[0].ParameterType.IsAssignableFrom(potion.GetType()))
                continue;
            try
            {
                return method.Invoke(null, [potion]) as Node;
            }
            catch { }
        }

        return null;
    }

    private static void SetPropertyOrField(object target, string name, object? value)
    {
        var type = target.GetType();
        try
        {
            var setter = AccessTools.PropertySetter(type, name);
            if (setter is not null)
            {
                setter.Invoke(target, [value]);
                return;
            }
        }
        catch { }

        try { AccessTools.Property(type, name)?.SetValue(target, value); return; } catch { }
        try { AccessTools.Field(type, name)?.SetValue(target, value); return; } catch { }
        try { AccessTools.Field(type, $"<{name}>k__BackingField")?.SetValue(target, value); } catch { }
    }

    private static object? InvokeByName(object target, string methodName, params object?[] args)
    {
        var type = target.GetType();
        foreach (var method in AccessTools.GetDeclaredMethods(type).Concat(type.GetMethods()).Where(m => m.Name == methodName))
        {
            if (method.GetParameters().Length != args.Length)
                continue;
            try { return method.Invoke(target, args); } catch { }
        }
        return null;
    }

    private static Type? FindType(string fullName)
    {
        var type = Type.GetType(fullName);
        if (type is not null)
            return type;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                type = asm.GetType(fullName, throwOnError: false);
                if (type is not null)
                    return type;
            }
            catch { }
        }

        return null;
    }

    private static void ClearAwaitingHandQueue(NPlayerHand hand)
    {
        try
        {
            var awaiting = AccessTools.Field(typeof(NPlayerHand), "_holdersAwaitingQueue")?.GetValue(hand);
            awaiting?.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(awaiting, null);
        }
        catch { }
    }

    private static void ClearHolderContainer(Node container, NPlayerHand hand)
    {
        foreach (var holder in container.GetChildren().OfType<NCardHolder>().ToList())
        {
            try
            {
                hand.RemoveCardHolder(holder);
            }
            catch
            {
                try { holder.Clear(); } catch { }
                try { holder.QueueFree(); } catch { }
            }
        }
    }

    private static void ForceSetPileCount(Node pileButton, int count)
    {
        try
        {
            AccessTools.Field(typeof(NCombatCardPile), "_currentCount")?.SetValue(pileButton, count);
            var label = AccessTools.Field(typeof(NCombatCardPile), "_countLabel")?.GetValue(pileButton);
            label?.GetType().GetMethod("SetTextAutoSize", [typeof(string)])?.Invoke(label, [count.ToString()]);
        }
        catch { }
    }

}

