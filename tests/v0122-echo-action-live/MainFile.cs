using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CodexTurnRewindEchoActionTest;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    internal static readonly MegaCrit.Sts2.Core.Logging.Logger Log = new("CodexTurnRewindEchoActionTest", LogType.Generic);
    public static void Initialize() { if (Engine.GetMainLoop() is SceneTree tree) tree.Root.CallDeferred(Node.MethodName.AddChild, new Runner()); }
}

public partial class Runner : Node
{
    private IList? _upgradeKeys;
    private List<string>? _savedUpgradeKeys;
    private string? _echoKey;
    public override void _Ready() => _ = RunAsync();
    private async Task Wait(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
    private async Task RunAsync()
    {
        try
        {
            for (var i=0;i<1200 && !CombatManager.Instance.IsInProgress;i++) await Wait(.05);
            if (!CombatManager.Instance.IsInProgress) throw new InvalidOperationException("combat unavailable");
            await Wait(2);
            var state=CombatManager.Instance.DebugOnlyGetState()!;
            var player=state.Players[0];
            ForceTransformedEcho();
            // Frozen Snake Eye / debug starts can fill all ten hand slots.  Free one
            // slot before inserting Echo Form so this test exercises the real card
            // play path instead of failing in NPlayerHand.Add.
            if (player.PlayerCombatState!.Hand.Cards.Count >= 10)
            {
                var displaced=player.PlayerCombatState.Hand.Cards[0];
                NPlayerHand.Instance!.Remove(displaced);
                player.PlayerCombatState.Hand.RemoveInternal(displaced,silent:false);
                player.PlayerCombatState.DiscardPile.AddInternal(displaced,silent:false);
            }
            var echo=ModelDb.Card<EchoForm>().ToMutable();
            state.AddCard(echo,player);
            echo.AfterCreated();
            player.PlayerCombatState!.Hand.AddInternal(echo,silent:false);
            NPlayerHand.Instance!.Add(NCard.Create(echo));
            player.PlayerCombatState.Energy=99;
            var rewind=AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="TurnRewind").GetType("TurnRewind.SnapshotManager",true)!;
            var echoAction=new PlayCardAction(echo,null);
            RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(echoAction);
            var echoCompleted=await Task.WhenAny(echoAction.CompletionTask,Task.Delay(8000));
            if(echoCompleted!=echoAction.CompletionTask) throw new TimeoutException($"Echo Form play stuck before rewind: {echoAction.State}");
            await echoAction.CompletionTask;
            await Wait(.5);
            if (player.Creature.GetPower<EchoFormPower>() is null) throw new InvalidOperationException("Echo Form was not applied");
            player.PlayerCombatState!.Energy=99;
            AccessTools.Field(rewind,"_lastCaptureKey")!.SetValue(null,null);
            AccessTools.Method(rewind,"CapturePlayerTurnSnapshot")!.Invoke(null,[state,player,"post transformed Echo baseline"]);
            var snapshots=(IList)AccessTools.Field(rewind,"_snapshots")!.GetValue(null)!;
            var snapshot=snapshots[snapshots.Count-1]!;

            // Dirty the current-turn Echo/history/card-play state before restoring
            // the snapshot that already contains Echo Form. This matches rewinding
            // a later turn while the power is active, which was not covered by the
            // old pre-power test.
            var dirtyEnemy=state.Enemies.First(e=>e.IsAlive);
            var dirtyCard=player.PlayerCombatState!.Hand.Cards.First(c=>c.Type==MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack && c.CanPlay(out _,out _) && c.IsValidTarget(dirtyEnemy));
            var dirtyAction=new PlayCardAction(dirtyCard,dirtyEnemy);
            RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(dirtyAction);
            if(await Task.WhenAny(dirtyAction.CompletionTask,Task.Delay(8000))!=dirtyAction.CompletionTask)
                throw new TimeoutException($"dirty transformed-Echo card stuck: {dirtyAction.State}");
            await dirtyAction.CompletionTask;
            var lingeringVisual=NCard.Create(dirtyCard) ?? throw new InvalidOperationException("could not create lingering Echo visual");
            NCombatRoom.Instance!.Ui.PlayContainer.AddChild(lingeringVisual);
            _=ReleaseVisualAfter(lingeringVisual,.6);
            MainFile.Log.Info($"[CodexTurnRewindEchoActionTest] requesting rewind immediately after Echo-replayed card; playNodes={PlayNodeCount()}, queueVisuals={QueueCount()}.");
            var restoreWait=System.Diagnostics.Stopwatch.StartNew();
            AccessTools.Method(rewind,"Restore")!.Invoke(null,[snapshot]);
            for(var i=0;i<1200 && (bool)AccessTools.Field(rewind,"_restorePending")!.GetValue(null)!;i++) await Wait(.01);
            if(restoreWait.Elapsed<TimeSpan.FromSeconds(.5)) throw new InvalidOperationException($"rewind did not wait for lingering Echo visual: {restoreWait.Elapsed.TotalMilliseconds:0}ms");
            await Wait(.25);

            state=CombatManager.Instance.DebugOnlyGetState()!; player=state.Players[0];
            if (player.Creature.GetPower<EchoFormPower>() is null) throw new InvalidOperationException("Echo Form disappeared from post-power rewind");
            var enemy=state.Enemies.First(e=>e.IsAlive);
            var card=player.PlayerCombatState!.Hand.Cards.First(c=>c.Type==MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack && c.CanPlay(out _,out _) && c.IsValidTarget(enemy));
            var action=new PlayCardAction(card,enemy);
            MainFile.Log.Info($"[CodexTurnRewindEchoActionTest] enqueueing post-rewind card {card.Id}, hand={player.PlayerCombatState.Hand.Cards.Count}, queueVisuals={QueueCount()}.");
            RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(action);
            var completed=await Task.WhenAny(action.CompletionTask,Task.Delay(8000));
            if (completed!=action.CompletionTask)
                throw new TimeoutException($"post-rewind PlayCardAction stuck: state={action.State}, executor={RunManager.Instance.ActionExecutor.CurrentlyRunningAction}, queueVisuals={QueueCount()}");
            await action.CompletionTask;
            await Wait(.5);
            if (QueueCount()!=0) throw new InvalidOperationException($"card completed but queue visual remains: {QueueCount()}");
            MainFile.Log.Info("[CodexTurnRewindEchoActionTest] PASS: post-Echo rewind card action completed and left the screen.");
        }
        catch(Exception ex){MainFile.Log.Error($"[CodexTurnRewindEchoActionTest] FAIL: {ex}");}
        finally { RestoreUpgradeState(); }
    }

    private void ForceTransformedEcho()
    {
        var better=AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="BetterDefect");
        var upgradeState=better.GetType("BetterDefect.BdCardUpgradeState",true)!;
        var state=AccessTools.Field(upgradeState,"_state")!.GetValue(null)!;
        _upgradeKeys=(IList)AccessTools.Property(state.GetType(),"UpgradedCards")!.GetValue(state)!;
        _savedUpgradeKeys=_upgradeKeys.Cast<string>().ToList();
        _echoKey=(string)AccessTools.Method(upgradeState,"CardKey")!.Invoke(null,[ModelDb.Card<EchoForm>()])!;
        for(var i=_upgradeKeys.Count-1;i>=0;i--) if(string.Equals(_upgradeKeys[i] as string,_echoKey,StringComparison.Ordinal)) _upgradeKeys.RemoveAt(i);
        _upgradeKeys.Add(_echoKey);
        MainFile.Log.Info("[CodexTurnRewindEchoActionTest] forced transformed Echo Form.");
    }

    private void RestoreUpgradeState()
    {
        if(_upgradeKeys is null || _savedUpgradeKeys is null) return;
        _upgradeKeys.Clear();
        foreach(var key in _savedUpgradeKeys) _upgradeKeys.Add(key);
    }
    private async Task ReleaseVisualAfter(NCard card,double seconds)
    {
        await Wait(seconds);
        if(GodotObject.IsInstanceValid(card)) card.QueueFree();
    }
    private static int QueueCount()=>((IList)AccessTools.Field(typeof(NCardPlayQueue),"_playQueue")!.GetValue(NCardPlayQueue.Instance)!).Count;
    private static int PlayNodeCount()=>NCombatRoom.Instance!.Ui.PlayContainer.GetChildren().OfType<NCard>().Count();
}
