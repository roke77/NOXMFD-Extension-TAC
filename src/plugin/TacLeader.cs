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
        [Serializable]
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

        // Page commands a leader sends. Ignored unless this player leads a squad and someone else
        // holds TAC.
        internal static void HandleCommand(string json, ulong holder)
        {
            Command? c;
            try { c = JsonUtility.FromJson<Command>(json); }
            catch (Exception ex) { Plugin.Log?.LogWarning($"[TAC] malformed command: {ex.Message}"); return; }
            if (c == null || holder == 0 || !IsLeader()) return;

            if (c.cmd == "engaging")
            {
                Task? t = _tasks.Find(x => x.id == c.id);
                if (t == null) return;
                t.e = c.on ? 1 : 0;   // shown at once; the officer's next "tasks" confirms it
                Api.SendMessage(Plugin.ExtId, holder,
                    "{\"t\":\"engaging\",\"id\":" + c.id.ToString(CultureInfo.InvariantCulture) + ",\"on\":" + (c.on ? "true" : "false") + "}");
            }
            else
            {
                Plugin.Log?.LogWarning($"[TAC] unknown leader command '{c.cmd}'.");
            }
        }

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
                sb.Append("{\"id\":").Append(t.id.ToString(CultureInfo.InvariantCulture)).Append(",\"n\":");
                TacJson.AppendString(sb, t.n);
                sb.Append(",\"p\":").Append(t.p).Append(",\"e\":").Append(t.e).Append('}');
            }
            sb.Append("]}");
        }
    }
}
