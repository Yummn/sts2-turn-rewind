using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CodexTurnRewindRelicTest;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    internal static MegaCrit.Sts2.Core.Logging.Logger Log { get; } = new("CodexTurnRewindRelicTest", LogType.Generic);
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
            for (var i = 0; i < 2400 && !CombatManager.Instance.IsInProgress; i++) await Wait(.25);
            if (!CombatManager.Instance.IsInProgress) throw new InvalidOperationException("combat unavailable");
            await Wait(2);
            var state = CombatManager.Instance.DebugOnlyGetState() ?? throw new InvalidOperationException("combat state unavailable");
            var player = state.Players[0];
            var flower = EnsureRelic<HappyFlower>(player);
            var nunchaku = EnsureRelic<Nunchaku>(player);
            var penNib = EnsureRelic<PenNib>(player);

            flower.TurnsSeen = 1;
            nunchaku.AttacksPlayed = 4;
            SetSavedProperty(penNib, "AttacksPlayed", 7);
            flower.Status = RelicStatus.Active;

            var rewind = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "TurnRewind")
                .GetType("TurnRewind.SnapshotManager", true)!;
            AccessTools.Field(rewind, "_lastCaptureKey")!.SetValue(null, null);
            AccessTools.Method(rewind, "CapturePlayerTurnSnapshot")!.Invoke(null, [state, player, "relic counter regression baseline"]);
            var snapshots = (IList)AccessTools.Field(rewind, "_snapshots")!.GetValue(null)!;
            var snapshot = snapshots[snapshots.Count - 1]!;

            flower.TurnsSeen = 2;
            nunchaku.AttacksPlayed = 8;
            SetSavedProperty(penNib, "AttacksPlayed", 9);
            flower.Status = RelicStatus.Normal;
            AccessTools.Method(rewind, "Restore")!.Invoke(null, [snapshot]);
            await Wait(.5);

            if (flower.TurnsSeen != 1 || flower.DisplayAmount != 1) throw new InvalidOperationException($"HappyFlower={flower.TurnsSeen}/{flower.DisplayAmount}");
            if (nunchaku.AttacksPlayed != 4 || nunchaku.DisplayAmount != 4) throw new InvalidOperationException($"Nunchaku={nunchaku.AttacksPlayed}/{nunchaku.DisplayAmount}");
            var pen = (int)AccessTools.Property(typeof(PenNib), "AttacksPlayed")!.GetValue(penNib)!;
            if (pen != 7 || penNib.DisplayAmount != 7) throw new InvalidOperationException($"PenNib={pen}/{penNib.DisplayAmount}");
            if (flower.Status != RelicStatus.Active) throw new InvalidOperationException($"HappyFlower status={flower.Status}");
            MainFile.Log.Info("[CodexTurnRewindRelicTest] PASS: Happy Flower, Nunchaku and Pen Nib counters/status restored in live combat.");
        }
        catch (Exception ex) { MainFile.Log.Error($"[CodexTurnRewindRelicTest] FAIL: {ex}"); }
    }

    private static T EnsureRelic<T>(MegaCrit.Sts2.Core.Entities.Players.Player player) where T : RelicModel
    {
        var relic = player.Relics.OfType<T>().FirstOrDefault();
        if (relic is not null) return relic;
        relic = (T)ModelDb.Relic<T>().ToMutable();
        player.AddRelicInternal(relic, silent: false);
        return relic;
    }

    private static void SetSavedProperty(object target, string name, object value) =>
        AccessTools.Property(target.GetType(), name)!.SetValue(target, value);
}
