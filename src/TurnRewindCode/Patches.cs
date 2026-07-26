using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace TurnRewind;

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
internal static class CombatUiActivatePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance, CombatState state)
    {
        try
        {
            RewindBar.Attach(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[TurnRewind] failed to attach rewind bar: {ex}");
        }
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Deactivate))]
internal static class CombatUiDeactivatePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance)
    {
        try
        {
            __instance.GetNodeOrNull<RewindBar>(RewindBar.NodeName)?.QueueFree();
        }
        catch { }
    }
}

[HarmonyPatch(typeof(CombatManager), "SetupPlayerTurn")]
internal static class CombatManagerSetupPlayerTurnPatch
{
    [HarmonyPostfix]
    private static void Postfix(Task __result, Player player, HookPlayerChoiceContext playerChoiceContext)
    {
        SnapshotManager.CaptureAfterPlayerTurnSetup(__result, player);
    }
}

