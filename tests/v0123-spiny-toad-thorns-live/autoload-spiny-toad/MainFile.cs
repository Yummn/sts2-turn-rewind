using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using System.Reflection;

namespace CodexAutoLoadSpinyToad;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    internal static MegaCrit.Sts2.Core.Logging.Logger Log { get; } = new("CodexAutoLoadSpinyToad", LogType.Generic);
    public static void Initialize() { new Harmony("CodexAutoLoadSpinyToad").PatchAll(); Log.Info("[CodexAutoLoadSpinyToad] test bridge loaded."); }
}

[HarmonyPatch]
internal static class AutoLoadPatch
{
    private static int _started;
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var a = AccessTools.Method(typeof(NGame), "LoadMainMenu", [typeof(bool)]); if (a != null) yield return a;
        var b = AccessTools.Method(typeof(NGame), "LaunchMainMenu", [typeof(bool)]); if (b != null) yield return b;
    }
    [HarmonyPostfix]
    private static void Postfix(ref Task __result) => __result = After(__result);
    private static async Task After(Task original)
    {
        await original;
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        await Task.Delay(1500);
        try
        {
            // Use the game's real Continue handler rather than reproducing its
            // evolving setup sequence. This keeps the temporary test bridge
            // aligned with the exact current PC build.
            NMainMenu? menu = null;
            for (var attempt = 0; attempt < 40 && menu == null; attempt++)
            {
                menu = Descendants(NGame.Instance.GetTree().Root).OfType<NMainMenu>().FirstOrDefault();
                if (menu == null)
                    await Task.Delay(250);
            }
            if (menu == null)
            {
                MainFile.Log.Warn("[CodexAutoLoadSpinyToad] main menu node unavailable.");
                return;
            }

            var continueMethod = AccessTools.Method(typeof(NMainMenu), "OnContinueButtonPressedAsync")
                ?? throw new MissingMethodException(typeof(NMainMenu).FullName, "OnContinueButtonPressedAsync");
            MainFile.Log.Info("[CodexAutoLoadSpinyToad] invoking the game's Continue handler.");
            if (continueMethod.Invoke(menu, null) is Task task)
                await task;
            MainFile.Log.Info("[CodexAutoLoadSpinyToad] current run loaded for test.");
            var fight = new DevConsole(shouldAllowDebugCommands: true)
                .ProcessCommand("fight SPINY_TOAD_NORMAL");
            MainFile.Log.Info(
                $"[CodexAutoLoadSpinyToad] forced multi-enemy fight success={fight.success} message={fight.msg}");
        }
        catch (Exception ex) { MainFile.Log.Error($"[CodexAutoLoadSpinyToad] failed: {ex}"); }
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        var pending = new Stack<Node>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;
            foreach (var child in node.GetChildren())
                pending.Push(child);
        }
    }
}


