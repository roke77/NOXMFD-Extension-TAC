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
    // NOXMFD's API, carries the messages between officers and squad leaders, gates the board to an
    // officer, and publishes the page's slice. Everything here runs on the Unity main thread:
    // TacWorker.Update, the command handler and the message handler.
    //
    // Messages ({"t": type, ...}, NOXMFD's ext.tac wire type):
    //   claim / release   officer -> faction      who is a TAC officer (every 3 s while one)
    //   hello             leader -> officer       from a leader linked to that officer: at once on LINK,
    //                                             then answering each of its claims
    //   unlink            leader -> officer       the leader linked elsewhere or pressed UNLINK
    //   tasks             officer -> leader       the squadron's whole target list, on change and per
    //                                             hello; the first one confirms a new link
    //   engaging          leader -> officer       ENGAGING on/off for one assigned target
    //   report            leader -> officer       REPORT: one of the leader's locked enemies, into its PULL bucket
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
        private static bool _wasMine;
        private static ulong _lastLink;
        private static readonly List<ulong> _gone = new List<ulong>();

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
            public string of = "";   // LINK: the officer's SteamID, as a string (a JS number can't hold it)
        }

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (now < _nextTick) return;
            _nextTick = now + TickSeconds;

            ulong self = Api.LocalSteamId;
            SquadRole squadRole = Api.GetLocalSquad().Role;
            bool inSquad = squadRole != SquadRole.None;
            // A leader who handed over, left or disbanded drops the link without telling the officer:
            // the squadron's new leader may link to the same officer and keep its assignments
            // (decision 16); if nobody does, the officer drops the squadron after the silence.
            if (squadRole != SquadRole.Leader && _role.Linked != 0)
            {
                Info($"link to {Who(_role.Linked)} dropped: you no longer lead a squad");
                _role.Unlink();
            }
            _gone.Clear();
            switch (_role.Update(self, inSquad, Faction(), now, _gone))
            {
                case TacRole.Tick.SendClaim:
                    Broadcast("claim");
                    break;
                case TacRole.Tick.SendRelease:
                    Broadcast("release");
                    Note(inSquad ? "TAC released: you joined a squad" : "TAC released: you left the faction");
                    Info(inSquad ? "role released: you joined a squad" : "role released: you left the faction (now: " + (Faction() ?? "none") + ")");
                    break;
            }
            foreach (ulong id in _gone)
            {
                Info($"TAC officer {Who(id)} went silent for {TacRole.OfficerTimeoutSeconds:0} s — treated as gone");
                if (id == _lastLink) Note("Your officer went silent — link to another");
            }
            if (_role.IsMine(self))
            {
                _dropped.Clear();
                _roster.Expire(now, _dropped);
                foreach (string sq in _dropped) DropSquadron(sq, $"silent {TacRoster.SilentSeconds:0} s");
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
            OnRoleMaybeChanged();
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
                            : _role.IsMine(self) ? "you're already an officer"
                            : inSquad ? "you're in a squad" : "you're not in a faction"));
                    _nextTick = 0f;   // claim on the next frame rather than up to a tick later
                    break;
                }
                case "release":
                    if (!_role.Release(self)) return;
                    Broadcast("release");
                    Info("you released TAC");
                    break;
                case "link":
                {
                    // The leader's choice is enough (decision 24): the officer's client confirms by
                    // sending its task list in answer to the hello.
                    if (!TacLeader.IsLeader() || !ulong.TryParse(c.of, out ulong officer))
                    {
                        Info("LINK refused: " + (TacLeader.IsLeader() ? $"'{c.of}' is not an officer id" : "only a squad leader links to a TAC officer"));
                        return;
                    }
                    if (!_role.LinkTo(officer, out ulong previous))
                    {
                        if (officer != _role.Linked) Info($"LINK refused: {Who(officer)} is not a TAC officer (any more)");
                        return;
                    }
                    if (previous != 0) Api.SendMessage(Plugin.ExtId, previous, MessageJson("unlink"));
                    bool sent = Api.SendMessage(Plugin.ExtId, officer, MessageJson("hello"));
                    Info($"linking to TAC officer {Who(officer)}" + (previous != 0 ? $", leaving {Who(previous)}" : "") +
                         $": hello {(sent ? "sent" : "send refused")}, waiting for their task list");
                    break;
                }
                case "unlink":
                    if (_role.Linked == 0) return;
                    Api.SendMessage(Plugin.ExtId, _role.Linked, MessageJson("unlink"));
                    Info($"you unlinked from {Who(_role.Linked)}");
                    _role.Unlink();
                    break;
                default:
                    if (TacLeader.IsLeaderCommand(c.cmd))
                    {
                        TacLeader.HandleCommand(json, _role.LinkState == TacRole.Link.Linked ? _role.Linked : 0);
                        break;
                    }
                    // Everything else edits the board, which only an officer has.
                    if (!_role.IsMine(self))
                    {
                        Plugin.Log?.LogInfo($"[TAC] '{c.cmd}' ignored: you're not a TAC officer.");
                        return;
                    }
                    if (c.cmd == "assign" && !_roster.TryLeader(c.sq, out _)) return;   // a squadron that just left
                    TacBoard.HandleCommand(json);
                    return;
            }
            OnRoleMaybeChanged();
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
                    if (_role.OnClaim(from, self, now)) Info($"TAC officer {Who(from)} is available");
                    if (_role.Linked == from && TacLeader.IsLeader())
                    {
                        bool sent = Api.SendMessage(Plugin.ExtId, from, MessageJson("hello"));
                        Once("hello-out|" + from, $"answering {Who(from)}'s claims with hello as {TacLeader.SquadronKey()} ({(sent ? "sent" : "send refused")})");
                    }
                    break;
                case "release":
                    if (!_role.OnRelease(from)) return;
                    Info($"TAC officer {Who(from)} released the role");
                    if (from == _lastLink) Note(Label(sender) + " released TAC — link to another officer");
                    break;
                case "unlink":
                {
                    if (!_role.IsMine(self)) return;
                    string? key = _roster.Remove(from);
                    if (key != null) DropSquadron(key, $"its leader {Who(from)} unlinked");
                    break;
                }
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
                    foreach (string sq in _dropped) DropSquadron(sq, $"its leader {Who(from)} now leads {key}");
                    if (heard == TacRoster.Heard.New) Info($"squadron {key} joined the matrix (leader {Who(from)})");
                    else if (heard == TacRoster.Heard.HandedOver) Info($"squadron {key} handed over to {Who(from)} — re-sending its tasks");
                    _sentTasks.Remove(key);   // every hello gets the full list: a handover or a lost message heals here
                    SyncTasks();
                    break;
                }
                case "tasks":
                {
                    if (from != _role.Linked || _role.IsMine(self) || !TacLeader.IsLeader()) return;
                    if (_role.Confirm(from))
                    {
                        Info($"link to TAC officer {Who(from)} confirmed");
                        Note("Linked to TAC " + Label(sender));
                    }
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
            OnRoleMaybeChanged();
            Publish();
        }

        // A new scene: the last match's role, roster and board are gone (decision 11).
        internal static void OnSceneLoaded()
        {
            if (_role.IsMine(Api.LocalSteamId)) Broadcast("release");
            _role.Reset();
            OnRoleMaybeChanged();
            Publish();
        }

        internal static void OnBoardChanged()
        {
            _reports.Prune(TacBoard.Has);   // a pulled (or otherwise added) report has done its job
            SyncTasks();
            Publish();
        }

        // What belonged to a standing goes with it: the board, roster and PULL buckets when this
        // client stops being an officer, a leader's assignments whenever the link changes.
        private static void OnRoleMaybeChanged()
        {
            bool mine = _role.IsMine(Api.LocalSteamId);
            if (_wasMine && !mine)
            {
                TacBoard.Clear();
                _roster.Clear();
                _sentTasks.Clear();
                _loggedTasks.Clear();
                _reports.Clear();
            }
            _wasMine = mine;
            if (_role.Linked == _lastLink) return;
            Info($"TAC link: {Who(_lastLink)} -> {Who(_role.Linked)}");
            TacLeader.Clear();
            _lastLink = _role.Linked;
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

        private static void DropSquadron(string squadron, string why)
        {
            _sentTasks.Remove(squadron);
            _loggedTasks.Remove(squadron);
            _reports.RemoveSquadron(squadron);
            bool withdrew = TacBoard.RemoveSquadron(squadron);
            if (withdrew) Note(squadron + " left — assignments withdrawn");
            Info($"squadron {squadron} left the matrix ({why})" +
                 (withdrew ? " — its assignments were withdrawn" : ""));
        }

        // ── Slice ──────────────────────────────────────────────────────────────────────────────
        // "r": this client's standing — officer or not, whether APPLY is offered, own squad role
        // (0 none, 1 leader, 2 member), the faction's other officers (SteamIDs as strings), the
        // officer this leader links to and the link state (0 none, 1 pending, 2 linked). "note": the
        // latest notice and its sequence, so the page shows each once. "lead": a leader's
        // assignments. Published only when it changes.
        internal static void Publish()
        {
            ulong self = Api.LocalSteamId;
            LocalSquad sq = Api.GetLocalSquad();
            bool mine = _role.IsMine(self);
            bool can = _role.CanApply(self, sq.Role != SquadRole.None, Faction());
            int squadRole = sq.Role == SquadRole.Leader ? 1 : sq.Role == SquadRole.Member ? 2 : 0;

            var sb = new StringBuilder(512);
            sb.Append("{\"r\":{\"mine\":").Append(mine ? 1 : 0)
              .Append(",\"can\":").Append(can ? 1 : 0)
              .Append(",\"sr\":").Append(squadRole)
              .Append(",\"offs\":[");
            List<ulong> officers = _role.Officers();
            for (int i = 0; i < officers.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":\"").Append(officers[i]).Append("\",\"n\":");
                TacJson.AppendString(sb, Label(FindPilot(officers[i])));
                sb.Append('}');
            }
            sb.Append("],\"link\":\"").Append(_role.Linked).Append("\",\"lk\":").Append((int)_role.LinkState)
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
