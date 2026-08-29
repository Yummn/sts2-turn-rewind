using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CodexTurnRewindV0121RaceTest;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    internal static MegaCrit.Sts2.Core.Logging.Logger Log { get; } = new("CodexTurnRewindV0121RaceTest", LogType.Generic);
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
            for (var i = 0; i < 2400 && !CombatManager.Instance.IsInProgress; i++) await Wait(.25);
            if (!CombatManager.Instance.IsInProgress) throw new InvalidOperationException("combat unavailable");
            await Wait(2);
            _rewind = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "TurnRewind")
                .GetType("TurnRewind.SnapshotManager", true)!;

            await TestFinalTurnSnapshot();
            await TestMidActionRestore();
            await TestOncePerCombatEnchantment();
            MainFile.Log.Info("[CodexTurnRewindV0121RaceTest] PASS: final turn snapshot, action-boundary restore, card visual cleanup and monotonic enchantment state all passed.");
        }
        catch (Exception ex) { MainFile.Log.Error($"[CodexTurnRewindV0121RaceTest] FAIL: {ex}"); }
    }

    private async Task TestFinalTurnSnapshot()
    {
        var state = State();
        var player = state.Players[0];
        var enemy = state.Enemies.First(e => e.IsAlive);
        ResetCaptureKey();
        Capture(state, player, "v0121 pre-AfterSide baseline");
        await PowerCmd.Apply<DoomPower>(new BlockingPlayerChoiceContext(), enemy, 7m, player.Creature, null);
        AccessTools.Method(_rewind, "OnTurnStarted")!.Invoke(null, [state]);
        if (Snapshots().Count < 1) throw new InvalidOperationException("final snapshot absent");
        await PowerCmd.Remove<DoomPower>(enemy);
        Restore(Snapshots()[Snapshots().Count - 1]!);
        await WaitForRestore();
        state = State();
        enemy = state.Enemies.First(e => e.IsAlive);
        if (enemy.GetPower<DoomPower>()?.Amount != 7m)
            throw new InvalidOperationException("TurnStarted replacement did not retain post-Countdown Doom");
        MainFile.Log.Info("[CodexTurnRewindV0121RaceTest] TURN-START PASS: final snapshot retained Doom applied after SetupPlayerTurn.");
    }

    private async Task TestMidActionRestore()
    {
        var state = State();
        var player = state.Players[0];
        ResetCaptureKey();
        Capture(state, player, "v0121 action race baseline");
        var snapshot = Snapshots()[Snapshots().Count - 1]!;
        var action = new PreviewAction(player.NetId, player.PlayerCombatState!.Hand.Cards.First());
        RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(action);
        for (var i = 0; i < 120 && RunManager.Instance.ActionExecutor.CurrentlyRunningAction != action; i++) await Wait(.01);
        if (RunManager.Instance.ActionExecutor.CurrentlyRunningAction != action) throw new InvalidOperationException("preview action did not start");
        Restore(snapshot);
        await Wait(.1);
        if ((bool)AccessTools.Field(_rewind, "_restoring")!.GetValue(null)!)
            throw new InvalidOperationException("state mutation began while card preview action was active");
        await WaitForRestore();

        var ui = NCombatRoom.Instance!.Ui;
        var handCount = State().Players[0].PlayerCombatState!.Hand.Cards.Count;
        var visualCount = Descendants(ui).OfType<NCard>().Count();
        var queue = (IList)AccessTools.Field(typeof(NCardPlayQueue), "_playQueue")!.GetValue(ui.PlayQueue)!;
        if (queue.Count != 0) throw new InvalidOperationException($"play queue retained {queue.Count} card(s)");
        if (visualCount != handCount) throw new InvalidOperationException($"stale card visuals remain: nodes={visualCount}, hand={handCount}");
        if (AccessTools.Field(ui.Hand.GetType(), "_currentCardPlay")?.GetValue(ui.Hand) is not null)
            throw new InvalidOperationException("hand remained locked after restore");
        MainFile.Log.Info("[CodexTurnRewindV0121RaceTest] ACTION PASS: rewind waited for active card/status preview and left no stuck card or hand lock.");
    }

    private async Task TestOncePerCombatEnchantment()
    {
        var state = State();
        var player = state.Players[0];
        var card = player.PlayerCombatState!.Hand.Cards.First(c => c.Enchantment is null);
        CardCmd.Enchant<Glam>(card, 1m);
        var pile = card.Pile!.Type;
        var index = card.Pile.Cards.ToList().IndexOf(card);
        ResetCaptureKey();
        Capture(state, player, "v0121 enchant baseline");
        var snapshot = Snapshots()[Snapshots().Count - 1]!;
        var glam = (Glam)card.Enchantment!;
        SetGlamUsed(glam, true);
        glam.Status = EnchantmentStatus.Disabled;
        Restore(snapshot);
        await WaitForRestore();

        var restored = pile.GetPile(State().Players[0]).Cards[index];
        if (restored.Enchantment is not Glam restoredGlam || !GetGlamUsed(restoredGlam) || restoredGlam.Status != EnchantmentStatus.Disabled)
            throw new InvalidOperationException("rewind re-enabled a once-per-combat enchantment");
        MainFile.Log.Info("[CodexTurnRewindV0121RaceTest] ENCHANT PASS: consumed Glam remained consumed after rewinding to its pre-use snapshot.");
    }

    private CombatState State() => CombatManager.Instance.DebugOnlyGetState() ?? throw new InvalidOperationException("combat state unavailable");
    private IList Snapshots() => (IList)AccessTools.Field(_rewind, "_snapshots")!.GetValue(null)!;
    private void ResetCaptureKey() => AccessTools.Field(_rewind, "_lastCaptureKey")!.SetValue(null, null);
    private void Capture(CombatState state, MegaCrit.Sts2.Core.Entities.Players.Player player, string reason) =>
        AccessTools.Method(_rewind, "CapturePlayerTurnSnapshot")!.Invoke(null, [state, player, reason]);
    private void Restore(object snapshot) => AccessTools.Method(_rewind, "Restore")!.Invoke(null, [snapshot]);
    private static bool GetGlamUsed(Glam glam) => (bool)(AccessTools.Property(glam.GetType(), "UsedThisCombat")?.GetValue(glam)
        ?? AccessTools.Field(glam.GetType(), "_usedThisCombat")!.GetValue(glam)!);
    private static void SetGlamUsed(Glam glam, bool value)
    {
        AccessTools.PropertySetter(glam.GetType(), "UsedThisCombat")?.Invoke(glam, [value]);
        AccessTools.Field(glam.GetType(), "_usedThisCombat")?.SetValue(glam, value);
    }

    private async Task WaitForRestore()
    {
        for (var i = 0; i < 1200; i++)
        {
            if (!(bool)AccessTools.Field(_rewind, "_restorePending")!.GetValue(null)!) { await Wait(.2); return; }
            await Wait(.01);
        }
        throw new TimeoutException("restore did not complete");
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}

public sealed class PreviewAction(ulong ownerId, CardModel model) : GameAction
{
    public override ulong OwnerId => ownerId;
    public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;
    protected override async Task ExecuteAction()
    {
        var node = NCard.Create(model);
        NCombatRoom.Instance!.Ui.AddChild(node);
        await NCombatRoom.Instance.ToSignal(NCombatRoom.Instance.GetTree().CreateTimer(.65), SceneTreeTimer.SignalName.Timeout);
        if (GodotObject.IsInstanceValid(node)) node.QueueFree();
    }
    public override INetAction ToNetAction() => null!;
}
