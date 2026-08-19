using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace CodexTurnRewindIntentFacingTest;
[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    internal static MegaCrit.Sts2.Core.Logging.Logger Log { get; } = new("CodexTurnRewindIntentFacingTest", LogType.Generic);
    public static void Initialize() { if (Engine.GetMainLoop() is SceneTree tree) tree.Root.CallDeferred(Node.MethodName.AddChild, new Runner()); }
}
public partial class Runner : Node
{
    public override void _Ready() => _ = RunAsync();
    private async Task Wait(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
    private async Task RunAsync()
    {
        try
        {
            for (var i=0;i<2400 && !CombatManager.Instance.IsInProgress;i++) await Wait(.25);
            if(!CombatManager.Instance.IsInProgress) throw new InvalidOperationException("combat unavailable");
            await Wait(3);
            var state=CombatManager.Instance.DebugOnlyGetState()!;
            if (state.Enemies.Any(e => e.Monster is CorpseSlug)) TestSlugs(state);
            else if (state.Players[0].Creature.Powers.Any(p => p.GetType().Name=="SurroundedPower")) await TestFacing(state);
            else throw new InvalidOperationException($"unsupported fixture enemies=[{string.Join(',',state.Enemies.Select(e=>e.ModelId.Entry))}]");
        }
        catch(Exception ex){MainFile.Log.Error($"[CodexTurnRewindIntentFacingTest] FAIL: {ex}");}
    }
    private static void TestSlugs(CombatState state)
    {
        var manager=Manager();
        var slugs=state.Enemies.Where(e=>e.Monster is CorpseSlug).Select(e=>(CorpseSlug)e.Monster!).ToList();
        if(slugs.Count<2) throw new InvalidOperationException($"slug count={slugs.Count}");
        var expected=slugs.Select(s=>new { Starter=s.StarterMoveIdx, Next=s.NextMove.Id, Current=Current(s)?.Id }).ToList();
        var snapshot=Capture(manager,state,"corpse slug initial intent");
        foreach(var slug in slugs)
        {
            var other=slug.MoveStateMachine!.States.Values.OfType<MoveState>().First(m=>m.Id!=slug.NextMove.Id);
            AccessTools.Field(typeof(CorpseSlug),"_starterMoveIdx")!.SetValue(slug, slug.StarterMoveIdx+7);
            AccessTools.Field(typeof(MonsterMoveStateMachine),"_currentState")!.SetValue(slug.MoveStateMachine,other);
            AccessTools.Field(typeof(MegaCrit.Sts2.Core.Models.MonsterModel),"<NextMove>k__BackingField")!.SetValue(slug,other);
        }
        Restore(manager,snapshot);
        for(var i=0;i<slugs.Count;i++)
        {
            var s=slugs[i]; var e=expected[i]; var current=Current(s);
            if(s.StarterMoveIdx!=e.Starter || s.NextMove.Id!=e.Next || current?.Id!=e.Current)
                throw new InvalidOperationException($"slug{i} expected={e.Starter}/{e.Current}/{e.Next} actual={s.StarterMoveIdx}/{current?.Id}/{s.NextMove.Id}");
            if(s.MoveStateMachine!.States.TryGetValue(e.Next,out var live) && !ReferenceEquals(s.NextMove,live))
                throw new InvalidOperationException($"slug{i} NextMove is stale object");
        }
        if(expected[0].Next==expected[1].Next) throw new InvalidOperationException("fixture did not start with distinct intentions");
        MainFile.Log.Info($"[CodexTurnRewindIntentFacingTest] PASS SLUG: exact first-turn random intents restored [{string.Join(',',expected.Select(e=>$"{e.Starter}:{e.Next}"))}].");
    }
    private async Task TestFacing(CombatState state)
    {
        var manager=Manager(); var creature=state.Players[0].Creature;
        var power=creature.Powers.First(p=>p.GetType().Name=="SurroundedPower");
        var field=AccessTools.Field(power.GetType(),"_facing")!;
        var face=AccessTools.Method(power.GetType(),"FaceDirection")!;
        var body=NCombatRoom.Instance!.GetCreatureNode(creature)!.Body;
        var original=field.GetValue(power)!; var originalSign=Math.Sign(body.Scale.X);
        var values=Enum.GetValues(field.FieldType).Cast<object>().ToList();
        var opposite=values.First(v=>!Equals(v,original));
        var snapshot=Capture(manager,state,"surrounded facing");
        if(face.Invoke(power,[opposite]) is Task turnAway) await turnAway;
        await Wait(.2);
        if(Equals(field.GetValue(power),original) || Math.Sign(body.Scale.X)==originalSign) throw new InvalidOperationException("fixture failed to turn away");
        Restore(manager,snapshot); await Wait(.3);
        if(!Equals(field.GetValue(power),original) || Math.Sign(body.Scale.X)!=originalSign)
            throw new InvalidOperationException($"restore mismatch facing={field.GetValue(power)}/{original} sign={Math.Sign(body.Scale.X)}/{originalSign}");
        if(face.Invoke(power,[opposite]) is Task turnAgain) await turnAgain;
        await Wait(.2);
        if(!Equals(field.GetValue(power),opposite) || Math.Sign(body.Scale.X)==originalSign)
            throw new InvalidOperationException("could not turn after rewind");
        MainFile.Log.Info($"[CodexTurnRewindIntentFacingTest] PASS CRAB: Surrounded facing and sprite restored, then sprite turned normally after rewind ({original}->{opposite}).");
    }
    private static MonsterState? Current(CorpseSlug s)=>AccessTools.Field(typeof(MonsterMoveStateMachine),"_currentState")?.GetValue(s.MoveStateMachine) as MonsterState;
    private static Type Manager()=>AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="TurnRewind").GetType("TurnRewind.SnapshotManager",true)!;
    private static object Capture(Type manager,CombatState state,string reason){AccessTools.Field(manager,"_lastCaptureKey")!.SetValue(null,null);AccessTools.Method(manager,"CapturePlayerTurnSnapshot")!.Invoke(null,[state,state.Players[0],reason]);var list=(IList)AccessTools.Field(manager,"_snapshots")!.GetValue(null)!;return list[list.Count-1]!;}
    private static void Restore(Type manager,object snapshot)=>AccessTools.Method(manager,"Restore")!.Invoke(null,[snapshot]);
}



