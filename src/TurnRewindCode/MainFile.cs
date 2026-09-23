using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace TurnRewind;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "TurnRewind";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, LogType.Generic);

    public static void Initialize()
    {
        new Harmony(ModId).PatchAll();
        SnapshotManager.Initialize();
        Logger.Info("[TurnRewind] loaded v0.1.27: exact energy and star cost layers and temporary power state now follow the selected turn snapshot.");
    }
}
