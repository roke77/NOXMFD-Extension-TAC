using System;
using System.Collections.Generic;
using System.Text;
using NOXMFD;
using NuclearOption.Networking;
using UnityEngine;

namespace TacModule
{
    // The live side of TAC (docs/tac-plan.md): drives TacRole from the game and NOXMFD's API, carries
    // the claim/release messages, gates the board to the role's holder, and publishes the page's
    // slice (role + board). Everything here runs on the Unity main thread: TacWorker.Update, the
    // command handler and the message handler.
    internal static class TacSession
    {
        private const float TickSeconds = 0.25f;

        private static readonly TacRole _role = new TacRole();
        private static float _nextTick;
        private static int _noteSeq;
        private static string _note = "";
        private static string _lastSlice = "";

        [Serializable]
        private class Message
        {
            public string t = "";
        }

        [Serializable]
        private class Command
        {
            public string cmd = "";
        }

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (now < _nextTick) return;
            _nextTick = now + TickSeconds;

            ulong self = Api.LocalSteamId;
            bool inSquad = Api.GetLocalSquad().Role != SquadRole.None;
            switch (_role.Update(self, inSquad, Faction(), now))
            {
                case TacRole.Tick.SendClaim:
                    Broadcast("claim");
                    break;
                case TacRole.Tick.SendRelease:
                    Broadcast("release");
                    TacBoard.Clear();
                    Note(inSquad ? "TAC released: you joined a squad" : "TAC released: you left the faction");
                    break;
                case TacRole.Tick.HolderGone:
                    Note("The TAC officer went silent — the role is free");
                    break;
            }
            Publish();
        }

        internal static void HandleCommand(string json)
        {
            Command? c;
            try { c = JsonUtility.FromJson<Command>(json); }
            catch (Exception ex) { Plugin.Log?.LogWarning($"[TAC] malformed command: {ex.Message}"); return; }
            if (c == null) return;

            ulong self = Api.LocalSteamId;
            switch (c.cmd)
            {
                case "apply":
                    if (!_role.Apply(self, Api.GetLocalSquad().Role != SquadRole.None, Faction(), Time.unscaledTime))
                        Plugin.Log?.LogInfo("[TAC] APPLY FOR TAC refused: the role is taken, or you're in a squad or no faction.");
                    _nextTick = 0f;   // claim on the next frame rather than up to a tick later
                    break;
                case "release":
                    if (!_role.Release(self)) return;
                    Broadcast("release");
                    TacBoard.Clear();
                    break;
                default:
                    // Everything else edits the board, which only the role's holder has.
                    if (!_role.IsMine(self))
                    {
                        Plugin.Log?.LogInfo($"[TAC] '{c.cmd}' ignored: you don't hold the TAC role.");
                        return;
                    }
                    TacBoard.HandleCommand(json);
                    return;
            }
            Publish();
        }

        // Another player's copy of this extension. `from` is Steam-authenticated; the payload is not,
        // and only a faction-mate outside any squad can hold the role (decision 3).
        internal static void HandleMessage(ulong from, string payload)
        {
            Message? m;
            try { m = JsonUtility.FromJson<Message>(payload); }
            catch { return; }
            if (m == null) return;
            FactionPilot? sender = FindPilot(from);
            if (sender == null) return;

            ulong self = Api.LocalSteamId;
            switch (m.t)
            {
                case "claim":
                    if (sender.SquadLeaderId != 0) return;
                    switch (_role.OnClaim(from, self, Time.unscaledTime))
                    {
                        case TacRole.Heard.SteppedDown:
                            TacBoard.Clear();
                            Note("TAC taken by " + Label(sender) + " (their claim won the race)");
                            break;
                        case TacRole.Heard.Contested:
                            Api.SendMessage(Plugin.ExtId, from, MessageJson("claim"));
                            break;
                    }
                    break;
                case "release":
                    if (_role.OnRelease(from, self)) Note(Label(sender) + " released TAC");
                    break;
            }
            Publish();
        }

        // A new scene: the last match's role and board are gone (decision 11).
        internal static void OnSceneLoaded()
        {
            if (_role.IsMine(Api.LocalSteamId)) Broadcast("release");
            _role.Reset();
            TacBoard.Clear();
            Publish();
        }

        // ── Slice ──────────────────────────────────────────────────────────────────────────────
        // "r": the role as this client sees it — mine, holder label, whether APPLY is offered, own
        // squad role (0 none, 1 leader, 2 member). "note": the latest notice and its sequence, so
        // the page shows each once. Published only when it changes.
        internal static void Publish()
        {
            ulong self = Api.LocalSteamId;
            LocalSquad sq = Api.GetLocalSquad();
            bool mine = _role.IsMine(self);
            string holder = _role.Holder == 0 ? "" : mine ? "YOU" : Label(FindPilot(_role.Holder));
            bool can = _role.CanApply(self, sq.Role != SquadRole.None, Faction());
            int squadRole = sq.Role == SquadRole.Leader ? 1 : sq.Role == SquadRole.Member ? 2 : 0;

            var sb = new StringBuilder(256);
            sb.Append("{\"r\":{\"mine\":").Append(mine ? 1 : 0)
              .Append(",\"h\":"); TacJson.AppendString(sb, holder);
            sb.Append(",\"can\":").Append(can ? 1 : 0)
              .Append(",\"sr\":").Append(squadRole)
              .Append("},\"note\":{\"s\":").Append(_noteSeq).Append(",\"t\":"); TacJson.AppendString(sb, _note);
            sb.Append("},");
            TacBoard.AppendJson(sb);
            sb.Append('}');

            string slice = sb.ToString();
            if (slice == _lastSlice) return;
            _lastSlice = slice;
            Api.PublishSlice(Plugin.ExtId, slice);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────────────
        private static void Note(string text)
        {
            _note = text;
            _noteSeq++;
        }

        private static void Broadcast(string type)
        {
            string payload = MessageJson(type);
            foreach (FactionPilot p in Api.GetFactionPilots()) Api.SendMessage(Plugin.ExtId, p.SteamId, payload);
        }

        private static string MessageJson(string type) => "{\"t\":\"" + type + "\"}";

        private static FactionPilot? FindPilot(ulong steamId)
        {
            foreach (FactionPilot p in Api.GetFactionPilots()) if (p.SteamId == steamId) return p;
            return null;
        }

        private static string Label(FactionPilot? p) =>
            p == null ? "UNKNOWN" : p.Designation.Length > 0 ? p.Designation : p.Name;

        // The local faction's name, or null outside one (main menu, loading, spectating before a pick).
        private static string? Faction() =>
            GameManager.GetLocalHQ(out FactionHQ hq) && hq != null ? hq.faction?.factionName ?? "?" : null;
    }
}
