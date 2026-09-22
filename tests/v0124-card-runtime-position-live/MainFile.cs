using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CodexTurnRewindCardRuntimePositionTest;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    internal static readonly MegaCrit.Sts2.Core.Logging.Logger Log = new("CodexTurnRewindCardRuntimePositionTest", LogType.Generic);
    public static void Initialize()
    {
        if (Engine.GetMainLoop() is SceneTree tree)
            tree.Root.CallDeferred(Node.MethodName.AddChild, new Runner());
    }
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
            var player = state.Players[0];
            var enemy = state.Enemies.First(creature => creature.IsAlive);
            var originalPosition = NCombatRoom.Instance!.GetCreatureNode(enemy)!.GlobalPosition;

            if (player.PlayerCombatState!.Hand.Cards.Count >= 10)
            {
                var displaced = player.PlayerCombatState.Hand.Cards[0];
                NPlayerHand.Instance!.Remove(displaced);
                player.PlayerCombatState.Hand.RemoveInternal(displaced, silent: false);
                player.PlayerCombatState.DiscardPile.AddInternal(displaced, silent: false);
            }

            var blade = (SovereignBlade)ModelDb.Card<SovereignBlade>().ToMutable();
            state.AddCard(blade, player);
            blade.AfterCreated();
            blade.AddDamage(15);
            player.PlayerCombatState.Hand.AddInternal(blade, silent: false);
            NPlayerHand.Instance!.Add(NCard.Create(blade)!);
            player.PlayerCombatState.Energy = 99;

            var action = new PlayCardAction(blade, enemy);
            RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(action);
            if (await Task.WhenAny(action.CompletionTask, Task.Delay(8000)) != action.CompletionTask)
                throw new TimeoutException($"Sovereign Blade play stuck: {action.State}");
            await action.CompletionTask;
            await Wait(.4);

            var manager = AppDomain.CurrentDomain.GetAssemblies()
                .First(assembly => assembly.GetName().Name == "TurnRewind")
                .GetType("TurnRewind.SnapshotManager", true)!;
            AccessTools.Field(manager, "_lastCaptureKey")!.SetValue(null, null);
            AccessTools.Method(manager, "CapturePlayerTurnSnapshot")!.Invoke(null, [state, player, "cumulative card runtime and position"]);
            var snapshots = (IList)AccessTools.Field(manager, "_snapshots")!.GetValue(null)!;
            var snapshot = snapshots[snapshots.Count - 1]!;

            blade.AddDamage(20);
            NCombatRoom.Instance.GetCreatureNode(enemy)!.GlobalPosition = originalPosition + new Vector2(0, -180);
            if (blade.DynamicVars.Damage.BaseValue != 45)
                throw new InvalidOperationException($"mutation fixture failed: {blade.DynamicVars.Damage.BaseValue}");

            AccessTools.Method(manager, "Restore")!.Invoke(null, [snapshot]);
            for (var i = 0; i < 1200 && (bool)AccessTools.Field(manager, "_restorePending")!.GetValue(null)!; i++) await Wait(.01);
            await Wait(.5);

            state = CombatManager.Instance.DebugOnlyGetState()!;
            player = state.Players[0];
            var restoredBlade = player.PlayerCombatState!.AllCards.OfType<SovereignBlade>().Single();
            var restoredPosition = NCombatRoom.Instance!.GetCreatureNode(enemy)!.GlobalPosition;
            var currentDamage = (decimal)AccessTools.Field(typeof(SovereignBlade), "_currentDamage")!.GetValue(restoredBlade)!;
            var finished = CombatManager.Instance.History.CardPlaysFinished.Last(entry => entry.CardPlay.Card is SovereignBlade);

            if (restoredBlade.DynamicVars.Damage.BaseValue != 25 || currentDamage != 25)
                throw new InvalidOperationException($"cumulative forge state mismatch dynamic={restoredBlade.DynamicVars.Damage.BaseValue} field={currentDamage}");
            if (!ReferenceEquals(finished.CardPlay.Card, restoredBlade))
                throw new InvalidOperationException("combat history still references the pre-rewind card instance");
            if (restoredPosition.DistanceTo(originalPosition) > .01f)
                throw new InvalidOperationException($"monster position mismatch expected={originalPosition} actual={restoredPosition}");

            MainFile.Log.Info("[CodexTurnRewindCardRuntimePositionTest] PASS: Sovereign Blade retained 15 prior forge damage, history points at the restored card, and monster global position is exact.");
        }
        catch (Exception ex)
        {
            MainFile.Log.Error($"[CodexTurnRewindCardRuntimePositionTest] FAIL: {ex}");
        }
        finally
        {
            await Wait(1);
            GetTree().Quit();
        }
    }
}
