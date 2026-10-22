using BepInEx;
using BepInEx.Logging;
using UnityEngine.SceneManagement;

namespace TacModule
{
    // A real, separate BepInEx plugin — not part of NOXMFD.dll. Registers itself with NOXMFD's
    // public Api at Awake() (EXTENSIONS.md) instead of anything in NOXMFD's own source knowing
    // this mod exists. The officer's target board (TacBoard.cs) — see docs/tac-plan.md.
    [BepInPlugin("com.roque.tac-module", "NOXMFD: TAC Module Extension", MyPluginInfo.PLUGIN_VERSION)]
    // Pinned to 0.52.0, the release that added SetSelectedUnit (LOCATE), per EXTENSIONS.md's
    // Versioning section.
    [BepInDependency("com.roque.NOXMFD", "0.52.0")]
    [BepInProcess("NuclearOption.exe")]
    [BepInProcess("NuclearOptionServer.exe")]
    public class Plugin : BaseUnityPlugin
    {
        internal const string ExtId = "tac";
        internal static ManualLogSource? Log;

        private void Awake()
        {
            Log = Logger;

            bool ok = NOXMFD.Api.RegisterExtension(ExtId, "TAC", TacPageAssets.Resolve, TacBoard.HandleCommand);
            if (!ok)
            {
                Log.LogError("[TAC] failed to register with NOXMFD (id already taken?) — extension disabled.");
                return;
            }
            SceneManager.sceneLoaded += (_, __) => TacBoard.Clear();

            Log.LogInfo("TAC loaded.");
        }
    }
}
