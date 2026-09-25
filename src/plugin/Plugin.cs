using BepInEx;
using BepInEx.Logging;

namespace TacModule
{
    // A real, separate BepInEx plugin — not part of NOXMFD.dll. Registers itself with NOXMFD's
    // public Api at Awake() (EXTENSIONS.md) instead of anything in NOXMFD's own source knowing
    // this mod exists. Placeholder page for now — see docs/ for the planned TAC page design.
    [BepInPlugin("com.roque.tac-module", "NOXMFD: TAC Module Extension", MyPluginInfo.PLUGIN_VERSION)]
    // Pinned to the version Api.cs (the only surface this extension touches) first shipped in —
    // the true floor for RegisterExtension, per EXTENSIONS.md's Versioning section.
    [BepInDependency("com.roque.NOXMFD", "0.23.0")]
    [BepInProcess("NuclearOption.exe")]
    [BepInProcess("NuclearOptionServer.exe")]
    public class Plugin : BaseUnityPlugin
    {
        internal const string ExtId = "tac";
        internal static ManualLogSource? Log;

        private void Awake()
        {
            Log = Logger;

            bool ok = NOXMFD.Api.RegisterExtension(ExtId, "TAC", TacPageAssets.Resolve);
            if (!ok)
            {
                Log.LogError("[TAC] failed to register with NOXMFD (id already taken?) — extension disabled.");
                return;
            }

            Log.LogInfo("TAC loaded.");
        }
    }
}
