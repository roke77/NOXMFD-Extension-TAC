using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NOXMFD;
using UnityEngine;

namespace TacModule
{
    // A squad leader's side of TAC (docs/tac-plan.md, "Squad leader view"): the targets the officer
    // assigned to this leader's squadron, and ENGAGING going back. Main thread only, like TacSession.
    internal static class TacLeader
    {
        internal class Task
        {
            public uint id;
            public string n = "";
            public int p;
            public int e;
        }

        private static List<Task> _tasks = new List<Task>();

        [Serializable]
        private class Command
        {
            public string cmd = "";
            public uint id;
            public bool on;
        }

        internal static bool IsLeaderCommand(string cmd) =>
            cmd == "engaging" || cmd == "acquire" || cmd == "acquire-all" || cmd == "report";

        internal static bool IsLeader() => Api.GetLocalSquad().Role == SquadRole.Leader;

        internal static string SquadronKey()
        {
            LocalSquad sq = Api.GetLocalSquad();
            return sq.Role == SquadRole.Leader ? TacRoster.Key(sq.Callsign, sq.Flight) : "";
        }

        // The officer's full list for this squadron, replacing whatever was here: every "tasks"
        // message is the whole state, so a lost or reordered one heals with the next.
        internal static void SetTasks(Task[]? list)
        {
            _tasks = new List<Task>();
            if (list == null) return;
            foreach (Task t in list)
            {
                if (t == null || t.id == 0 || _tasks.Exists(x => x.id == t.id)) continue;
                t.n ??= "";
                t.n = t.n.Length > 64 ? t.n.Substring(0, 64) : t.n;
                t.p = Math.Max(0, Math.Min(3, t.p));
                t.e = t.e != 0 ? 1 : 0;
                _tasks.Add(t);
            }
        }

        internal static void Clear() => _tasks = new List<Task>();

        internal static int Count => _tasks.Count;

        // Page commands a leader sends. Ignored unless this player leads a squad and the officer it
        // linked to has confirmed (`officer` is 0 otherwise).
        internal static void HandleCommand(string json, ulong officer)
        {
            Command? c;
            try { c = JsonUtility.FromJson<Command>(json); }
            catch (Exception ex) { Plugin.Log?.LogWarning($"[TAC] malformed command: {ex.Message}"); return; }
            if (c == null || officer == 0 || !IsLeader()) return;

            switch (c.cmd)
            {
                case "engaging":
                {
                    Task? t = _tasks.Find(x => x.id == c.id);
                    if (t == null) return;
                    t.e = c.on ? 1 : 0;   // shown at once; the officer's next "tasks" confirms it
                    bool sent = Api.SendMessage(Plugin.ExtId, officer,
                        "{\"t\":\"engaging\",\"id\":" + Id(c.id) + ",\"on\":" + (c.on ? "true" : "false") + "}");
                    TacSession.Diag($"ENGAGING {(c.on ? "on" : "off")} for '{t.n}' ({c.id}) -> {TacSession.Who(officer)}: {(sent ? "sent" : "send refused")}");
                    break;
                }
                case "acquire":
                    if (_tasks.Exists(x => x.id == c.id))
                        TacSession.Diag($"ACQUIRE {c.id}: {(Api.SelectTarget(c.id) ? "selected" : "not selected (NOXMFD logs why)")}");
                    break;
                case "acquire-all":
                {
                    int selected = 0;
                    foreach (Task t in _tasks) if (Api.SelectTarget(t.id)) selected++;
                    TacSession.Diag($"ACQUIRE ALL: selected {selected} of {_tasks.Count} (the rest already selected or not selectable)");
                    break;
                }
                case "report":
                {
                    // Only one of this leader's own locked enemies, named as the game names it — never
                    // whatever id or name the page sent.
                    List<TacGame.Lock> locks = TacGame.LockedEnemies();
                    int i = locks.FindIndex(l => l.Id == c.id);
                    if (i < 0)
                    {
                        TacSession.Info($"REPORT of {c.id} refused: not one of your locked enemies right now");
                        return;
                    }
                    TacSession.Diag($"REPORT '{locks[i].Name}' ({c.id}) -> {TacSession.Who(officer)}");
                    // A target object with the message type added: {"id":..,"n":"..","t":"report"}.
                    var sb = TacJson.AppendTarget(new StringBuilder(), c.id, TacJson.Clip(locks[i].Name, MaxSentName));
                    Api.SendMessage(Plugin.ExtId, officer, sb.Append(",\"t\":\"report\"}").ToString());
                    break;
                }
                default:
                    Plugin.Log?.LogWarning($"[TAC] unknown leader command '{c.cmd}'.");
                    break;
            }
        }

        private static string Id(uint id) => id.ToString(CultureInfo.InvariantCulture);

        private const int MaxSentName = 40;

        // The leader's part of the slice: its squadron and assignments, in the officer's order.
        internal static void AppendJson(StringBuilder sb)
        {
            sb.Append("\"lead\":{\"q\":");
            TacJson.AppendString(sb, SquadronKey());
            sb.Append(",\"l\":[");
            for (int i = 0; i < _tasks.Count; i++)
            {
                Task t = _tasks[i];
                if (i > 0) sb.Append(',');
                TacJson.AppendTarget(sb, t.id, t.n).Append(",\"p\":").Append(t.p).Append(",\"e\":").Append(t.e).Append('}');
            }
            sb.Append("]}");
        }
    }
}
