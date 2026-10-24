using System;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TacModule
{
    // A real, separate BepInEx plugin — not part of NOXMFD.dll. Registers itself with NOXMFD's
    // public Api at Awake() (EXTENSIONS.md) instead of anything in NOXMFD's own source knowing
    // this mod exists. The TAC role and messaging (TacSession.cs) and the officer's target board
    // (TacBoard.cs) — see docs/tac-plan.md.
    [BepInPlugin("com.roque.tac-module", "NOXMFD: TAC Module Extension", MyPluginInfo.PLUGIN_VERSION)]
    // Raised to the NOXMFD release that ships extension API 8 (messaging) once it exists
    // (docs/tac-plan.md, build order step 4); until then Awake checks for the API itself.
    [BepInDependency("com.roque.NOXMFD", "0.52.0")]
    [BepInProcess("NuclearOption.exe")]
    [BepInProcess("NuclearOptionServer.exe")]
    public class Plugin : BaseUnityPlugin
    {
        internal const string ExtId = "tac";
        internal static ManualLogSource? Log;

        private static bool _sceneSubscribed;
        private static GameObject? _worker;

        private void Awake()
        {
            Log = Logger;

            // ApiVersion is a const, compiled into this DLL as a literal, so it can't tell which
            // NOXMFD is actually loaded; the methods themselves can. LocateOnMap is API 8's newest
            // addition, so finding it means the rest of API 8 is there too.
            if (typeof(NOXMFD.Api).GetMethod("SendMessage") == null || typeof(NOXMFD.Api).GetMethod("LocateOnMap") == null)
            {
                Log.LogError("[TAC] this NOXMFD has no extension messaging or LocateOnMap (API 8) — TAC disabled. Update NOXMFD.");
                return;
            }
            if (!Register()) return;

            if (!_sceneSubscribed)
            {
                _sceneSubscribed = true;
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
            Log.LogInfo($"TAC {MyPluginInfo.PLUGIN_VERSION} loaded (NOXMFD extension API {NOXMFD.Api.ApiVersion}+ found).");
        }

        // Kept out of Awake: the JIT compiles a method whole before running it, and this one names
        // API 8 members, so against an older NOXMFD it must only be compiled after the check passed.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool Register()
        {
            if (!NOXMFD.Api.RegisterExtension(ExtId, "TAC", TacPageAssets.Resolve, TacSession.HandleCommand))
            {
                Log?.LogError("[TAC] failed to register with NOXMFD (id already taken?) — extension disabled.");
                return false;
            }
            NOXMFD.Api.SetMessageHandler(ExtId, TacSession.HandleMessage);
            return true;
        }

        // The Plugin's own GameObject dies on the boot -> MainMenu scene change (NOXMFD's Plugin.cs
        // explains), so the per-frame driver is a GameObject of our own, made persistent from the
        // first scene load, where DontDestroyOnLoad holds.
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_worker == null)
            {
                _worker = new GameObject("TAC_Worker");
                DontDestroyOnLoad(_worker);
                _worker.AddComponent<TacWorker>();
            }
            TacSession.OnSceneLoaded();
        }
    }

    internal sealed class TacWorker : MonoBehaviour
    {
        private bool _failed;

        private void Update()
        {
            try { TacSession.Tick(); }
            catch (Exception ex)
            {
                // Logged once: a fault that repeats every frame would otherwise flood the log.
                if (_failed) return;
                _failed = true;
                Plugin.Log?.LogWarning($"[TAC] tick failed: {ex}");
            }
        }
    }
}
