using System;
using System.Collections.Generic;
using System.Text;
using NOXMFD;
using Newtonsoft.Json;
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
    //   report            leader -> holder        REPORT: one of the leader's locked enemies, into its PULL bucket
    internal static class TacSession
    {
        private const float TickSeconds = 0.25f;
        private const float GoneCheckSeconds = 1f;

        // Officer: each squadron's reported targets, waiting for a PULL.
        private static readonly TacReports _reports = new TacReports();
        private static float _nextGoneCheck;

        private static readonly TacRole _role = new TacRole();
        private static readonly TacRoster _roster = new TacRoster();
        private static readonly Dictionary<string, string> _sentTasks = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly List<string> _dropped = new List<string>();
        private static readonly HashSet<ulong> _badSenders = new HashSet<ulong>();
        private static readonly Dictionary<string, string> _loggedTasks = new Dictionary<string, string>(StringComparer.Ordinal);
        private static float _nextTick;
        private static int _noteSeq;
        private static string _note = "";
        private static string _lastSlice = "";
        private static ulong _lastHolder;

        // Read with Newtonsoft, not JsonUtility: JsonUtility leaves `l` (an array of objects) empty in
        // the game's Mono runtime. Nulls are ignored so a hostile `"n":null` keeps the "" default.
        private static readonly JsonSerializerSettings MessageSettings =
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

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
                    Info(inSquad ? "role released: you joined a squad" : "role released: you left the faction (now: " + (Faction() ?? "none") + ")");
                    break;
                case TacRole.Tick.HolderGone:
                    Note("The TAC officer went silent — the role is free");
                    Info($"no claim for {TacRole.HolderTimeoutSeconds:0} s — the officer is treated as gone");
                    break;
            }
            if (_role.IsMine(self))
            {
                _dropped.Clear();
                _roster.Expire(now, _dropped);
                foreach (string sq in _dropped) DropSquadron(sq);
                if (now >= _nextGoneCheck)
                {
                    // Destroyed targets leave the board, every leader's list and the PULL buckets (decision 10).
                    _nextGoneCheck = now + GoneCheckSeconds;
                    foreach (uint id in TacBoard.Ids())
                        if (TacGame.IsGone(id) && TacBoard.Remove(id)) Info($"target {id} destroyed — removed from the board");
                    _reports.Prune(TacGame.IsGone);
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
                {
                    bool inSquad = Api.GetLocalSquad().Role != SquadRole.None;
                    string? faction = Faction();
                    if (_role.Apply(self, inSquad, faction, Time.unscaledTime))
                        Info($"applied for TAC in faction '{faction}' — claiming now");
                    else
                        Info("APPLY FOR TAC refused: " + (self == 0 ? "Steam isn't available"
                            : _role.Holder != 0 ? "the role is held by " + Who(_role.Holder)
                            : inSquad ? "you're in a squad" : "you're not in a faction"));
                    _nextTick = 0f;   // claim on the next frame rather than up to a tick later
                    break;
                }
                case "release":
                    if (!_role.Release(self)) return;
                    Broadcast("release");
                    Info("you released TAC");
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
            try { m = JsonConvert.DeserializeObject<Message>(payload, MessageSettings); }
            catch (Exception ex)
            {
                // Once per sender: another player's broken or newer client must not flood the log.
                if (_badSenders.Add(from)) Plugin.Log?.LogWarning($"[TAC] unreadable message from {from}: {ex.Message}");
                return;
            }
            if (m == null) return;
            FactionPilot? sender = FindPilot(from);
            if (sender == null)
            {
                // The usual cause: NOXMFD hasn't heard this player's presence beat yet (or they're in
                // another faction). Logged once; later messages from them are dropped quietly.
                Once("nopilot|" + from, $"'{m.t}' from {from} ignored: NOXMFD doesn't list them as a faction-mate running it (yet)");
                return;
            }

            ulong self = Api.LocalSteamId;
            float now = Time.unscaledTime;
            switch (m.t)
            {
                case "claim":
                    if (sender.SquadLeaderId != 0)   // only a player outside any squad can hold TAC
                    {
                        Once("sqclaim|" + from, $"claim from {Who(from)} ignored: the faction table shows them in a squad");
                        return;
                    }
                    switch (_role.OnClaim(from, self, now))
                    {
                        case TacRole.Heard.SteppedDown:
                            Note("TAC taken by " + Label(sender) + " (their claim won the race)");
                            Info($"stepped down: {Who(from)} claimed too, and the lower SteamID wins");
                            break;
                        case TacRole.Heard.Contested:
                            Api.SendMessage(Plugin.ExtId, from, MessageJson("claim"));
                            Diag($"claim from {Who(from)} while we hold TAC — answered with ours (our SteamID is lower)");
                            break;
                    }
                    if (_role.Holder == from && TacLeader.IsLeader())
                    {
                        bool sent = Api.SendMessage(Plugin.ExtId, from, MessageJson("hello"));
                        Once("hello-out|" + from, $"answering {Who(from)}'s claims with hello as {TacLeader.SquadronKey()} ({(sent ? "sent" : "send refused")})");
                    }
                    break;
                case "release":
                    if (_role.OnRelease(from, self)) Note(Label(sender) + " released TAC");
                    break;
                case "hello":
                {
                    // Believed only from the leader of the squad its own presence beat announces.
                    if (!_role.IsMine(self)) return;
                    if (sender.SquadLeaderId != from || sender.SquadCallsign.Length == 0)
                    {
                        Once("badhello|" + from + "|" + sender.SquadLeaderId,
                            $"hello from {Who(from)} ignored: the faction table shows them " +
                            (sender.SquadLeaderId == 0 ? "in no squad" : "as a member of " + Who(sender.SquadLeaderId) + "'s squad") +
                            " — normal for a few seconds after a squad is created");
                        return;
                    }
                    string key = TacRoster.Key(sender.SquadCallsign, sender.SquadFlight);
                    _dropped.Clear();
                    TacRoster.Heard heard = _roster.Note(key, from, now, _dropped);
                    foreach (string sq in _dropped) DropSquadron(sq);
                    if (heard == TacRoster.Heard.New) Info($"squadron {key} joined the matrix (leader {Who(from)})");
                    else if (heard == TacRoster.Heard.HandedOver) Info($"squadron {key} handed over to {Who(from)} — re-sending its tasks");
                    _sentTasks.Remove(key);   // every hello gets the full list: a handover or a lost message heals here
                    SyncTasks();
                    break;
                }
                case "tasks":
                {
                    if (_role.Holder != from || _role.IsMine(self) || !TacLeader.IsLeader()) return;
                    int before = TacLeader.Count;
                    TacLeader.SetTasks(m.l);
                    if (TacLeader.Count != before) Diag($"tasks from {Who(from)}: {TacLeader.Count} target(s) (was {before})");
                    break;
                }
                case "engaging":
                {
                    if (!_role.IsMine(self)) return;
                    string? squadron = _roster.KeyOf(from);
                    Diag($"engaging {(m.on ? "on" : "off")} for target {m.id} from {Who(from)} ({squadron ?? "not on the roster"})");
                    if (squadron != null && TacBoard.SetEngaged(m.id, squadron, m.on)) OnBoardChanged();
                    break;
                }
                case "report":
                {
                    // From a squadron on the roster, and an enemy as this officer's own game sees it.
                    // It waits in that squadron's PULL bucket; only the officer puts it on the board.
                    if (!_role.IsMine(self)) return;
                    string? squadron = _roster.KeyOf(from);
                    bool enemy = TacGame.IsEnemy(m.id);
                    string verdict = squadron == null ? "ignored, not on the roster"
                        : !enemy ? "ignored, not an enemy (or gone) here"
                        : TacBoard.Has(m.id) ? "ignored, already on the board"
                        : _reports.Add(squadron, m.id, TacJson.Clip(m.n, TacBoard.MaxNameLength)) ? $"waiting in {squadron}'s PULL ({_reports.Count(squadron)})"
                        : "already waiting in its PULL";
                    Diag($"REPORT of '{m.n}' ({m.id}) from {Who(from)}: {verdict}");
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
            _reports.Prune(TacBoard.Has);   // a pulled (or otherwise added) report has done its job
            SyncTasks();
            Publish();
        }

        // Whenever the holder changes, what belonged to the old one goes: the officer's board and
        // roster when the role leaves this client, a leader's assignments when the holder changes.
        private static void OnHolderMaybeChanged()
        {
            if (_role.Holder == _lastHolder) return;
            ulong self = Api.LocalSteamId;
            Info($"TAC holder: {Who(_lastHolder)} -> {Who(_role.Holder)}");
            if (_lastHolder == self && self != 0)
            {
                TacBoard.Clear();
                _roster.Clear();
                _sentTasks.Clear();
                _loggedTasks.Clear();
                _reports.Clear();
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
                if (!_roster.TryLeader(sq, out ulong leader)) continue;
                if (!Api.SendMessage(Plugin.ExtId, leader, tasks))
                {
                    Once("tasksfail|" + sq + "|" + leader, $"tasks for {sq} to {Who(leader)}: send refused (retried each tick)");
                    continue;
                }
                _sentTasks[sq] = tasks;
                // Logged only when the list itself changed, not on the re-send that follows each hello.
                if (!_loggedTasks.TryGetValue(sq, out string? logged) || logged != tasks)
                {
                    _loggedTasks[sq] = tasks;
                    Diag($"tasks -> {sq} ({Who(leader)}): {tasks.Length} bytes");
                }
            }
        }

        private static void DropSquadron(string squadron)
        {
            _sentTasks.Remove(squadron);
            _loggedTasks.Remove(squadron);
            _reports.RemoveSquadron(squadron);
            bool withdrew = TacBoard.RemoveSquadron(squadron);
            if (withdrew) Note(squadron + " left — its assignments were withdrawn");
            Info($"squadron {squadron} left the matrix (renamed, disbanded or silent {TacRoster.SilentSeconds:0} s)" +
                 (withdrew ? " — its assignments were withdrawn" : ""));
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
            sb.Append(",\"pull\":");
            _reports.AppendJson(sb);   // emptied whenever the role leaves this client
            sb.Append(',');
            TacLeader.AppendJson(sb);
            sb.Append('}');

            string slice = sb.ToString();
            if (slice == _lastSlice) return;
            _lastSlice = slice;
            Api.PublishSlice(Plugin.ExtId, slice);
        }

        // ── Logging ────────────────────────────────────────────────────────────────────────────
        // Info: role and roster changes and refusals — rare, and what a player's log should show.
        // Once: the same, for a cause that can repeat every few seconds (a sender NOXMFD doesn't
        // list yet, a hello the faction table doesn't back), logged the first time per key.
        private static readonly HashSet<string> _onceKeys = new HashSet<string>(StringComparer.Ordinal);
        internal static void Info(string msg) => Plugin.Log?.LogInfo("[TAC] " + msg);
        internal static void Once(string key, string msg) { if (_onceKeys.Add(key)) Info(msg); }

        // ponytail: message-traffic diagnostics for the first two-player test, wall-clock stamped so
        // two players' logs line up with each other and NOXMFD's [NOXMFD ext-link] lines. Remove
        // (grep "Diag(") once TAC has shipped.
        internal static void Diag(string msg) => Plugin.Log?.LogInfo($"[TAC diag {DateTime.Now:HH:mm:ss}] {msg}");

        internal static string Who(ulong steamId) =>
            steamId == 0 ? "nobody" : steamId == Api.LocalSteamId ? "you" : $"{Label(FindPilot(steamId))} ({steamId})";

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
