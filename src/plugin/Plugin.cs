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
    // 0.77.0 ships extension API 8 (faction roster, messaging, SelectTarget, LocateOnMap): BepInEx
    // won't load TAC against an older NOXMFD.
    [BepInDependency("com.roque.NOXMFD", "0.77.0")]
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
            if (!Register()) return;

            if (!_sceneSubscribed)
            {
                _sceneSubscribed = true;
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
            Log.LogInfo($"TAC {MyPluginInfo.PLUGIN_VERSION} loaded.");
        }

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
