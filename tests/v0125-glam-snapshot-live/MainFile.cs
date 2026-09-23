using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;

namespace CodexTurnRewindGlamSnapshotTest;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    internal static readonly MegaCrit.Sts2.Core.Logging.Logger Log = new("CodexTurnRewindGlamSnapshotTest", LogType.Generic);
    public static void Initialize()
    {
        if (Engine.GetMainLoop() is SceneTree tree)
            tree.Root.CallDeferred(Node.MethodName.AddChild, new Runner());
    }
}

public partial class Runner : Node
{
    private Type _rewind = null!;
    public override void _Ready() => _ = RunAsync();
    private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task RunAsync()
    {
        try
        {
            for (var i = 0; i < 2400 && !CombatManager.Instance.IsInProgress; i++) await Wait(.05);
            if (!CombatManager.Instance.IsInProgress) throw new InvalidOperationException("combat unavailable");
            await Wait(2);
            _rewind = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "TurnRewind")
                .GetType("TurnRewind.SnapshotManager", true)!;

            var state = CombatManager.Instance.DebugOnlyGetState()!;
            var player = state.Players[0];
            var pcs = player.PlayerCombatState!;
            var enemy = state.Enemies.First(e => e.IsAlive);
            while (pcs.Hand.Cards.Count >= 9)
            {
                var displaced = pcs.Hand.Cards[0];
                NPlayerHand.Instance!.Remove(displaced);
                pcs.Hand.RemoveInternal(displaced, silent: false);
                pcs.DiscardPile.AddInternal(displaced, silent: false);
            }

            var card = (Claw)ModelDb.Card<Claw>().ToMutable();
            state.AddCard(card, player);
            card.AfterCreated();
            pcs.Hand.AddInternal(card, silent: false);
            NPlayerHand.Instance!.Add(NCard.Create(card)!);
            CardCmd.Enchant<Glam>(card, 1m);
            var preIndex = pcs.Hand.Cards.ToList().IndexOf(card);
            var preGlam = (Glam)card.Enchantment!;
            var preStatus = preGlam.Status;
            if (GetUsed(preGlam)) throw new InvalidOperationException("fresh Glam was already consumed");

            ResetCaptureKey();
            Capture(state, player, "v0125 Glam before real play");
            var preSnapshot = Snapshots()[Snapshots().Count - 1]!;

            pcs.Energy = 99;
            var action = new PlayCardAction(card, enemy);
            RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(action);
            if (await Task.WhenAny(action.CompletionTask, Task.Delay(8000)) != action.CompletionTask)
                throw new TimeoutException($"Glam card play stuck: {action.State}");
            await action.CompletionTask;
            await Wait(.4);
            var consumedCard = pcs.AllCards.First(c => ReferenceEquals(c, card));
            if (consumedCard.Enchantment is not Glam consumedGlam || !GetUsed(consumedGlam) || consumedGlam.Status != EnchantmentStatus.Disabled)
                throw new InvalidOperationException("real card play did not consume Glam");
            var postPile = consumedCard.Pile!.Type;
            var postIndex = consumedCard.Pile.Cards.ToList().IndexOf(consumedCard);

            ResetCaptureKey();
            Capture(state, player, "v0125 Glam after real play");
            var postSnapshot = Snapshots()[Snapshots().Count - 1]!;

            Restore(preSnapshot);
            await WaitForRestore();
            state = CombatManager.Instance.DebugOnlyGetState()!;
            player = state.Players[0];
            var restoredPre = player.PlayerCombatState!.Hand.Cards[preIndex];
            if (restoredPre.Enchantment is not Glam restoredPreGlam || GetUsed(restoredPreGlam) || restoredPreGlam.Status != preStatus)
                throw new InvalidOperationException($"pre-use snapshot mismatch: used={GetUsed((Glam)restoredPre.Enchantment!)} status={restoredPre.Enchantment!.Status}");

            Restore(postSnapshot);
            await WaitForRestore();
            state = CombatManager.Instance.DebugOnlyGetState()!;
            player = state.Players[0];
            var restoredPost = player.PlayerCombatState!.AllPiles.First(pile => pile.Type == postPile).Cards[postIndex];
            if (restoredPost.Enchantment is not Glam restoredPostGlam || !GetUsed(restoredPostGlam) || restoredPostGlam.Status != EnchantmentStatus.Disabled)
                throw new InvalidOperationException("post-use snapshot did not retain consumed Glam");

            MainFile.Log.Info("[CodexTurnRewindGlamSnapshotTest] PASS: real Glam play consumed once; rewind before play restored availability; rewind after play retained consumption.");
        }
        catch (Exception ex)
        {
            MainFile.Log.Error($"[CodexTurnRewindGlamSnapshotTest] FAIL: {ex}");
        }
        finally
        {
            await Wait(1);
            GetTree().Quit();
        }
    }

    private IList Snapshots() => (IList)AccessTools.Field(_rewind, "_snapshots")!.GetValue(null)!;
    private void ResetCaptureKey() => AccessTools.Field(_rewind, "_lastCaptureKey")!.SetValue(null, null);
    private void Capture(CombatState state, MegaCrit.Sts2.Core.Entities.Players.Player player, string reason) =>
        AccessTools.Method(_rewind, "CapturePlayerTurnSnapshot")!.Invoke(null, [state, player, reason]);
    private void Restore(object snapshot) => AccessTools.Method(_rewind, "Restore")!.Invoke(null, [snapshot]);
    private static bool GetUsed(Glam glam) => (bool)(AccessTools.Property(glam.GetType(), "UsedThisCombat")?.GetValue(glam)
        ?? AccessTools.Field(glam.GetType(), "_usedThisCombat")!.GetValue(glam)!);

    private async Task WaitForRestore()
    {
        for (var i = 0; i < 1200; i++)
        {
            if (!(bool)AccessTools.Field(_rewind, "_restorePending")!.GetValue(null)!) { await Wait(.2); return; }
            await Wait(.01);
        }
        throw new TimeoutException("restore did not complete");
    }
}
