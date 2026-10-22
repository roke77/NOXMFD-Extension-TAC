using System;
using System.Collections.Generic;
using System.Text;
using NOXMFD;
using NuclearOption.Networking;
using UnityEngine;

namespace TacModule
{
    // The live side of TAC (docs/tac-plan.md): drives TacRole and TacRoster from the game and
    // NOXMFD's API, carries the messages between the officer and squad leaders, gates the board to
    // the role's holder, and publishes the page's slice. Everything here runs on the Unity main
    // thread: TacWorker.Update, the command handler and the message handler.
    //
    // Messages ({"t": type, ...}, NOXMFD's ext.tac wire type):
    //   claim / release   holder -> faction       who holds TAC (every 3 s while held)
    //   hello             leader -> holder        answers each claim: "I lead a squad and run TAC"
    //   tasks             holder -> leader        the squadron's whole target list, on change and per hello
    //   engaging          leader -> holder        ENGAGING on/off for one assigned target
    //   pull / locks      holder -> leader -> holder   PULL: the leader's locked enemies right now
    //   report            leader -> holder        REPORT: one of the leader's locked enemies
    internal static class TacSession
    {
        private const float TickSeconds = 0.25f;
        private const float GoneCheckSeconds = 1f;

        // Officer: each pulled squadron's reply, until the next pull of that squadron.
        private static readonly Dictionary<string, TacLeader.Task[]> _pulls = new Dictionary<string, TacLeader.Task[]>(StringComparer.Ordinal);
        private static float _nextGoneCheck;

        private static readonly TacRole _role = new TacRole();
        private static readonly TacRoster _roster = new TacRoster();
        private static readonly Dictionary<string, string> _sentTasks = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly List<string> _dropped = new List<string>();
        private static float _nextTick;
        private static int _noteSeq;
        private static string _note = "";
        private static string _lastSlice = "";
        private static ulong _lastHolder;

        [Serializable]
        private class Message
        {
            public string t = "";
            public uint id;
            public string n = "";
            public bool on;
            public TacLeader.Task[]? l;
        }

        [Serializable]
        private class Command
        {
            public string cmd = "";
            public string sq = "";
        }

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (now < _nextTick) return;
            _nextTick = now + TickSeconds;

            ulong self = Api.LocalSteamId;
            SquadRole squadRole = Api.GetLocalSquad().Role;
            bool inSquad = squadRole != SquadRole.None;
            // A leader who handed over, left or disbanded no longer gets this squadron's targets.
            if (squadRole != SquadRole.Leader) TacLeader.Clear();
            switch (_role.Update(self, inSquad, Faction(), now))
            {
                case TacRole.Tick.SendClaim:
                    Broadcast("claim");
                    break;
                case TacRole.Tick.SendRelease:
                    Broadcast("release");
                    Note(inSquad ? "TAC released: you joined a squad" : "TAC released: you left the faction");
                    break;
                case TacRole.Tick.HolderGone:
                    Note("The TAC officer went silent — the role is free");
                    break;
            }
            if (_role.IsMine(self))
            {
                _dropped.Clear();
                _roster.Expire(now, _dropped);
                foreach (string sq in _dropped) DropSquadron(sq);
                if (now >= _nextGoneCheck)
                {
                    // Destroyed targets leave the board and every leader's list (decision 10).
                    _nextGoneCheck = now + GoneCheckSeconds;
                    foreach (uint id in TacBoard.Ids()) if (TacGame.IsGone(id)) TacBoard.Remove(id);
                }
                SyncTasks();   // also carries any removal above to the leaders
            }
            OnHolderMaybeChanged();
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
                    break;
                case "pull":
                    if (!_role.IsMine(self) || !_roster.TryLeader(c.sq, out ulong leader)) return;
                    _pulls.Remove(c.sq);   // the page shows "waiting" until the reply lands
                    Api.SendMessage(Plugin.ExtId, leader, MessageJson("pull"));
                    break;
                default:
                    if (TacLeader.IsLeaderCommand(c.cmd))
                    {
                        TacLeader.HandleCommand(json, _role.IsMine(self) ? 0 : _role.Holder);
                        break;
                    }
                    // Everything else edits the board, which only the role's holder has.
                    if (!_role.IsMine(self))
                    {
                        Plugin.Log?.LogInfo($"[TAC] '{c.cmd}' ignored: you don't hold the TAC role.");
                        return;
                    }
                    if (c.cmd == "assign" && !_roster.TryLeader(c.sq, out _)) return;   // a squadron that just left
                    TacBoard.HandleCommand(json);
                    return;
            }
            OnHolderMaybeChanged();
            Publish();
        }

        // Another player's copy of this extension. `from` is Steam-authenticated; the payload is not.
        internal static void HandleMessage(ulong from, string payload)
        {
            Message? m;
            try { m = JsonUtility.FromJson<Message>(payload); }
            catch { return; }
            if (m == null) return;
            FactionPilot? sender = FindPilot(from);
            if (sender == null) return;

            ulong self = Api.LocalSteamId;
            float now = Time.unscaledTime;
            switch (m.t)
            {
                case "claim":
                    if (sender.SquadLeaderId != 0) return;   // only a player outside any squad can hold TAC
                    switch (_role.OnClaim(from, self, now))
                    {
                        case TacRole.Heard.SteppedDown:
                            Note("TAC taken by " + Label(sender) + " (their claim won the race)");
                            break;
                        case TacRole.Heard.Contested:
                            Api.SendMessage(Plugin.ExtId, from, MessageJson("claim"));
                            break;
                    }
                    if (_role.Holder == from && TacLeader.IsLeader()) Api.SendMessage(Plugin.ExtId, from, MessageJson("hello"));
                    break;
                case "release":
                    if (_role.OnRelease(from, self)) Note(Label(sender) + " released TAC");
                    break;
                case "hello":
                    // Believed only from the leader of the squad its own presence beat announces.
                    if (!_role.IsMine(self) || sender.SquadLeaderId != from || sender.SquadCallsign.Length == 0) return;
                    string key = TacRoster.Key(sender.SquadCallsign, sender.SquadFlight);
                    _dropped.Clear();
                    _roster.Note(key, from, now, _dropped);
                    foreach (string sq in _dropped) DropSquadron(sq);
                    _sentTasks.Remove(key);   // every hello gets the full list: a handover or a lost message heals here
                    SyncTasks();
                    break;
                case "tasks":
                    if (_role.Holder != from || _role.IsMine(self) || !TacLeader.IsLeader()) return;
                    TacLeader.SetTasks(m.l);
                    break;
                case "engaging":
                {
                    if (!_role.IsMine(self)) return;
                    string? squadron = _roster.KeyOf(from);
                    if (squadron != null && TacBoard.SetEngaged(m.id, squadron, m.on)) OnBoardChanged();
                    break;
                }
                case "pull":
                    // Answered without a press (decision 8), with a note so the leader knows.
                    if (_role.Holder != from || _role.IsMine(self) || !TacLeader.IsLeader()) return;
                    Api.SendMessage(Plugin.ExtId, from, TacLeader.LocksMessage());
                    TacLeader.NotePulled();
                    break;
                case "locks":
                {
                    if (!_role.IsMine(self)) return;
                    string? squadron = _roster.KeyOf(from);
                    if (squadron == null) return;
                    _pulls[squadron] = m.l ?? Array.Empty<TacLeader.Task>();
                    break;
                }
                case "report":
                {
                    // From a squadron on the roster, and an enemy as this officer's own game sees it.
                    if (!_role.IsMine(self)) return;
                    string? squadron = _roster.KeyOf(from);
                    if (squadron == null || !TacGame.IsEnemy(m.id)) return;
                    if (TacBoard.Add(m.id, m.n))
                    {
                        Note(squadron + " reported " + (m.n.Length > 0 ? m.n : "a target"));
                        OnBoardChanged();
                    }
                    break;
                }
            }
            OnHolderMaybeChanged();
            Publish();
        }

        // A new scene: the last match's role, roster and board are gone (decision 11).
        internal static void OnSceneLoaded()
        {
            if (_role.IsMine(Api.LocalSteamId)) Broadcast("release");
            _role.Reset();
            OnHolderMaybeChanged();
            Publish();
        }

        internal static void OnBoardChanged()
        {
            SyncTasks();
            Publish();
        }

        // Whenever the holder changes, what belonged to the old one goes: the officer's board and
        // roster when the role leaves this client, a leader's assignments when the holder changes.
        private static void OnHolderMaybeChanged()
        {
            if (_role.Holder == _lastHolder) return;
            ulong self = Api.LocalSteamId;
            if (_lastHolder == self && self != 0)
            {
                TacBoard.Clear();
                _roster.Clear();
                _sentTasks.Clear();
                _pulls.Clear();
            }
            TacLeader.Clear();
            _lastHolder = _role.Holder;
        }

        // Officer: each squadron's leader gets its "tasks" list whenever it differs from what it last got.
        private static void SyncTasks()
        {
            if (!_role.IsMine(Api.LocalSteamId)) return;
            foreach (string sq in _roster.Keys())
            {
                string tasks = TacBoard.TasksMessage(sq);
                if (_sentTasks.TryGetValue(sq, out string? sent) && sent == tasks) continue;
                if (_roster.TryLeader(sq, out ulong leader) && Api.SendMessage(Plugin.ExtId, leader, tasks))
                    _sentTasks[sq] = tasks;
            }
        }

        private static void DropSquadron(string squadron)
        {
            _sentTasks.Remove(squadron);
            if (TacBoard.RemoveSquadron(squadron)) Note(squadron + " left — its assignments were withdrawn");
        }

        // ── Slice ──────────────────────────────────────────────────────────────────────────────
        // "r": the role as this client sees it — mine, holder label, whether APPLY is offered, own
        // squad role (0 none, 1 leader, 2 member). "note": the latest notice and its sequence, so
        // the page shows each once. "lead": a leader's assignments. Published only when it changes.
        internal static void Publish()
        {
            ulong self = Api.LocalSteamId;
            LocalSquad sq = Api.GetLocalSquad();
            bool mine = _role.IsMine(self);
            string holder = _role.Holder == 0 ? "" : mine ? "YOU" : Label(FindPilot(_role.Holder));
            bool can = _role.CanApply(self, sq.Role != SquadRole.None, Faction());
            int squadRole = sq.Role == SquadRole.Leader ? 1 : sq.Role == SquadRole.Member ? 2 : 0;

            var sb = new StringBuilder(512);
            sb.Append("{\"r\":{\"mine\":").Append(mine ? 1 : 0)
              .Append(",\"h\":"); TacJson.AppendString(sb, holder);
            sb.Append(",\"can\":").Append(can ? 1 : 0)
              .Append(",\"sr\":").Append(squadRole)
              .Append("},\"note\":{\"s\":").Append(_noteSeq).Append(",\"t\":"); TacJson.AppendString(sb, _note);
            sb.Append("},");
            TacBoard.AppendJson(sb, mine ? _roster.Keys() : new List<string>());
            sb.Append(",\"pull\":{");
            bool first = true;
            if (mine)
            {
                foreach (var kv in _pulls)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    TacJson.AppendString(sb, kv.Key);
                    sb.Append(":[");
                    for (int i = 0; i < kv.Value.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append("{\"id\":").Append(kv.Value[i].id).Append(",\"n\":");
                        TacJson.AppendString(sb, kv.Value[i].n);
                        sb.Append('}');
                    }
                    sb.Append(']');
                }
            }
            sb.Append("},");
            TacLeader.AppendJson(sb);
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
