using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace CodexTurnRewindFtlHistoryTest;
[ModInitializer(nameof(Initialize))]
public static class MainFile
{
 internal static MegaCrit.Sts2.Core.Logging.Logger Log {get;}=new("CodexTurnRewindFtlHistoryTest",LogType.Generic);
 public static void Initialize(){if(Engine.GetMainLoop() is SceneTree t)t.Root.CallDeferred(Node.MethodName.AddChild,new Runner());}
}
public partial class Runner:Node
{
 public override void _Ready()=>_=RunAsync();
 private async Task Wait(double s)=>await ToSignal(GetTree().CreateTimer(s),SceneTreeTimer.SignalName.Timeout);
 private async Task RunAsync(){try{
  for(var i=0;i<2400&&!CombatManager.Instance.IsInProgress;i++)await Wait(.25);
  if(!CombatManager.Instance.IsInProgress)throw new InvalidOperationException("combat unavailable"); await Wait(3);
  var state=CombatManager.Instance.DebugOnlyGetState()!;var player=state.Players[0];var enemy=state.Enemies.First(e=>e.IsAlive);
  var manager=AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="TurnRewind").GetType("TurnRewind.SnapshotManager",true)!;
  var initial=CurrentFinished(state,player);var snapshot=Capture(manager,state,"FTL history baseline");
  var owned=PileType.Hand.GetPile(player).Cards.First();
  for(var i=0;i<4;i++){var play=new CardPlay{Card=owned,Player=player,Target=enemy,ResultPile=PileType.Discard,Resources=new ResourceInfo{EnergySpent=0,EnergyValue=0,StarsSpent=0,StarValue=0},IsAutoPlay=false,PlayIndex=i,PlayCount=4};CombatManager.Instance.History.CardPlayFinished(state,play);}
  var polluted=CurrentFinished(state,player);if(polluted!=initial+4)throw new InvalidOperationException($"fixture pollution {initial}->{polluted}");
  Restore(manager,snapshot);await Wait(.5);var restored=CurrentFinished(state,player);
  if(restored!=initial)throw new InvalidOperationException($"finished-card count restored to {restored}, expected {initial}");
  var result=new DevConsole(true).ProcessCommand("card FTL");if(!result.success)throw new InvalidOperationException(result.msg);
  await Wait(.3);var ftl=PileType.Hand.GetPile(player).Cards.Last(c=>c is Ftl);var handBefore=PileType.Hand.GetPile(player).Cards.Count;var drawBefore=PileType.Draw.GetPile(player).Cards.Count;
  await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(),ftl,enemy);await Wait(1);
  var handAfter=PileType.Hand.GetPile(player).Cards.Count;var drawAfter=PileType.Draw.GetPile(player).Cards.Count;var afterCount=CurrentFinished(state,player);
  MainFile.Log.Info($"[CodexTurnRewindFtlHistoryTest] OBSERVE initial={initial} polluted={polluted} restored={restored} hand={handBefore}->{handAfter} draw={drawBefore}->{drawAfter} finished={afterCount}");
  if(initial<3 && drawBefore>0 && drawAfter!=drawBefore-1)throw new InvalidOperationException("FTL did not draw after rewind although current-turn count is below threshold");
  MainFile.Log.Info("[CodexTurnRewindFtlHistoryTest] PASS: CardPlaysFinished count and actual FTL draw behavior reset correctly after rewind.");
 }catch(Exception ex){MainFile.Log.Error($"[CodexTurnRewindFtlHistoryTest] FAIL: {ex}");}}
 private static int CurrentFinished(CombatState s,MegaCrit.Sts2.Core.Entities.Players.Player p)=>CombatManager.Instance.History.CardPlaysFinished.Count(e=>e.HappenedThisTurn(s)&&e.CardPlay.Player==p);
 private static object Capture(Type m,CombatState s,string r){AccessTools.Field(m,"_lastCaptureKey")!.SetValue(null,null);AccessTools.Method(m,"CapturePlayerTurnSnapshot")!.Invoke(null,[s,s.Players[0],r]);var l=(IList)AccessTools.Field(m,"_snapshots")!.GetValue(null)!;return l[l.Count-1]!;}
 private static void Restore(Type m,object s)=>AccessTools.Method(m,"Restore")!.Invoke(null,[s]);
}

