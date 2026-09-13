using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace CodexTurnRewindThornsTest;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    internal static readonly MegaCrit.Sts2.Core.Logging.Logger Log = new("CodexTurnRewindThornsTest", LogType.Generic);
    public static void Initialize() { if (Engine.GetMainLoop() is SceneTree tree) tree.Root.CallDeferred(Node.MethodName.AddChild, new Runner()); }
}

public partial class Runner : Node
{
    public override void _Ready() => _ = RunAsync();
    private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task RunAsync()
    {
        try
        {
            for (var i = 0; i < 2400 && !CombatManager.Instance.IsInProgress; i++) await Wait(.05);
            if (!CombatManager.Instance.IsInProgress) throw new InvalidOperationException("combat unavailable");
            await Wait(2);
            var state = CombatManager.Instance.DebugOnlyGetState()!;
            var creature = state.Enemies.SingleOrDefault(e => e.Monster is SpinyToad)
                ?? throw new InvalidOperationException($"SpinyToad fixture unavailable: [{string.Join(',', state.Enemies.Select(e => e.ModelId.Entry))}]");
            var toad = (SpinyToad)creature.Monster!;
            var machine = toad.MoveStateMachine!;

            // Execute vanilla protrude, roll the vanilla explosion intent, and
            // capture the exact player turn where the enemy visibly has thorns.
            await toad.PerformMove();
            toad.RollMove(state.PlayerCreatures);
            if (creature.GetPower<ThornsPower>()?.Amount != 5 || !toad.IsSpiny || toad.NextMove.Id != "SPIKE_EXPLOSION_MOVE")
                throw new InvalidOperationException($"fixture setup failed thorns={creature.GetPower<ThornsPower>()?.Amount} spiny={toad.IsSpiny} next={toad.NextMove.Id}");

            var manager = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "TurnRewind").GetType("TurnRewind.SnapshotManager", true)!;
            AccessTools.Field(manager, "_lastCaptureKey")!.SetValue(null, null);
            AccessTools.Method(manager, "CapturePlayerTurnSnapshot")!.Invoke(null, [state, state.Players[0], "Spiny Toad thorn turn"]);
            var snapshots = (IList)AccessTools.Field(manager, "_snapshots")!.GetValue(null)!;
            var snapshot = snapshots[snapshots.Count - 1]!;

            // Move to the naked state, then rewind to prove power, model flag,
            // intent and the stateful body animation are restored together.
            await toad.PerformMove();
            toad.RollMove(state.PlayerCreatures);
            if (creature.GetPower<ThornsPower>() is not null || toad.IsSpiny)
                throw new InvalidOperationException("vanilla explosion did not remove thorns before rewind");
            AccessTools.Method(manager, "Restore")!.Invoke(null, [snapshot]);
            for (var i = 0; i < 1200 && (bool)AccessTools.Field(manager, "_restorePending")!.GetValue(null)!; i++) await Wait(.01);
            await Wait(.8);

            var restoredPower = creature.GetPower<ThornsPower>();
            var current = AccessTools.Field(typeof(MonsterMoveStateMachine), "_currentState")!.GetValue(machine) as MonsterState;
            var node = NCombatRoom.Instance!.GetCreatureNode(creature)!;
            var animator = AccessTools.Field(typeof(NCreature), "_spineAnimator")!.GetValue(node)!;
            var animState = AccessTools.Field(animator.GetType(), "_currentState")!.GetValue(animator)!;
            var animId = (string)AccessTools.Property(animState.GetType(), "Id")!.GetValue(animState)!;
            if (restoredPower?.Amount != 5 || !toad.IsSpiny || toad.NextMove.Id != "SPIKE_EXPLOSION_MOVE" || current?.Id != "SPIKE_EXPLOSION_MOVE" || animId != "idle_loop")
                throw new InvalidOperationException($"restore mismatch thorns={restoredPower?.Amount} spiny={toad.IsSpiny} current={current?.Id} next={toad.NextMove.Id} anim={animId}");

            // The restored explosion must still remove exactly the restored
            // thorns once; this catches stale move delegates/power instances.
            await toad.PerformMove();
            if (creature.GetPower<ThornsPower>() is not null || toad.IsSpiny)
                throw new InvalidOperationException($"post-rewind explosion left stale thorns={creature.GetPower<ThornsPower>()?.Amount} spiny={toad.IsSpiny}");

            // A request made while a monster task owns combat must wait instead
            // of restoring underneath the move's pending power mutation.
            AccessTools.Field(typeof(MegaCrit.Sts2.Core.Models.MonsterModel), "_isPerformingMove")!.SetValue(toad, true);
            var before = creature.CurrentHp;
            AccessTools.Method(manager, "Restore")!.Invoke(null, [snapshot]);
            await Wait(.2);
            if (!(bool)AccessTools.Field(manager, "_restorePending")!.GetValue(null)!)
                throw new InvalidOperationException("rewind did not wait for active monster move");
            AccessTools.Field(typeof(MegaCrit.Sts2.Core.Models.MonsterModel), "_isPerformingMove")!.SetValue(toad, false);
            for (var i = 0; i < 1200 && (bool)AccessTools.Field(manager, "_restorePending")!.GetValue(null)!; i++) await Wait(.01);
            if (creature.GetPower<ThornsPower>()?.Amount != 5)
                throw new InvalidOperationException("deferred monster-boundary rewind failed to restore thorns");

            MainFile.Log.Info("[CodexTurnRewindThornsTest] PASS: thorn power, SpinyToad body/intent and post-rewind unspike lifecycle are exact; active monster moves are deferred.");
        }
        catch (Exception ex) { MainFile.Log.Error($"[CodexTurnRewindThornsTest] FAIL: {ex}"); }
        finally { await Wait(1); GetTree().Quit(); }
    }
}
