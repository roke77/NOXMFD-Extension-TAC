using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace TacModule
{
    // The officer's target board (docs/tac-plan.md): in memory for the mission, shared by every pane
    // showing the TAC page. Unit ids are the game's persistentID, opaque here. A target's name is the
    // one the page saw in telemetry when it was added, kept so a row stays readable while the
    // faction's picture is jammed and so it can travel to squad leaders with the assignment.
    internal static class TacBoard
    {
        private const int PriNormal = 1;
        private const int PriMax = 3;           // LOW, NORMAL, HIGH, CRITICAL
        private const int MaxNameLength = 64;
        private const int MaxSquadronLength = 32;

        private sealed class Assignee
        {
            public string Squadron = "";
            public bool Engaged;
        }

        private sealed class Entry
        {
            public uint Id;
            public string Name = "";
            public int Priority = PriNormal;
            public int Seq;                     // when it was added: the tiebreak inside a priority
            public readonly List<Assignee> Assignees = new List<Assignee>();
        }

        private static readonly List<Entry> _board = new List<Entry>();
        private static int _seq;

        [Serializable]
        private class Command
        {
            public string cmd = "";
            public uint id;
            public string name = "";
            public int p = -1;
            public string sq = "";
        }

        // NOXMFD runs command handlers on the Unity main thread (EXTENSIONS.md), and Clear runs from
        // sceneLoaded, also main thread — so the board needs no lock.
        internal static void HandleCommand(string json)
        {
            Command? c;
            try { c = JsonUtility.FromJson<Command>(json); }
            catch (Exception ex) { Plugin.Log?.LogWarning($"[TAC] malformed command: {ex.Message}"); return; }
            if (c == null) return;
            if (c.id == 0)
            {
                Plugin.Log?.LogWarning($"[TAC] '{c.cmd}' command without a unit id.");
                return;
            }

            Entry? e = Find(c.id);
            switch (c.cmd)
            {
                case "add":
                    if (!Add(c.id, c.name)) return;
                    break;
                case "remove":
                    if (!Remove(c.id)) return;
                    break;
                case "priority":
                    if (e == null || c.p < 0 || c.p > PriMax || e.Priority == c.p) return;
                    e.Priority = c.p;
                    break;
                case "assign":
                {
                    string sq = Clip(c.sq, MaxSquadronLength);
                    if (e == null || sq.Length == 0 || e.Assignees.Exists(a => a.Squadron == sq)) return;
                    e.Assignees.Add(new Assignee { Squadron = sq });
                    break;
                }
                case "unassign":
                    if (e == null || e.Assignees.RemoveAll(a => a.Squadron == c.sq) == 0) return;
                    break;
                case "locate":
                    NOXMFD.Api.SetSelectedUnit(c.id);
                    return;
                default:
                    Plugin.Log?.LogWarning($"[TAC] unknown command '{c.cmd}'.");
                    return;
            }
            TacSession.OnBoardChanged();
        }

        // The board lives for one mission and one officer (decision 11): emptied on a scene load and
        // whenever this client stops holding the role. The caller publishes.
        internal static void Clear() => _board.Clear();

        // ADD from MAP, PULL and a leader's REPORT all land here: one row per unit, however many ways
        // it arrives, at NORMAL. True when it was new.
        internal static bool Add(uint id, string? name)
        {
            if (id == 0 || Find(id) != null) return false;
            _board.Add(new Entry { Id = id, Name = Clip(name, MaxNameLength), Seq = ++_seq });
            return true;
        }

        internal static bool Remove(uint id) => _board.RemoveAll(e => e.Id == id) > 0;

        internal static List<uint> Ids() => _board.ConvertAll(e => e.Id);

        // A leader's ENGAGING for one of its squadron's targets. True when something changed.
        internal static bool SetEngaged(uint id, string squadron, bool on)
        {
            Assignee? a = Find(id)?.Assignees.Find(x => x.Squadron == squadron);
            if (a == null || a.Engaged == on) return false;
            a.Engaged = on;
            return true;
        }

        // A squadron that disbanded or went silent loses every assignment (decision 16).
        internal static bool RemoveSquadron(string squadron)
        {
            bool changed = false;
            foreach (Entry e in _board) changed |= e.Assignees.RemoveAll(a => a.Squadron == squadron) > 0;
            return changed;
        }

        // One squadron's targets as the "tasks" message its leader receives: board order (priority,
        // then when added), each with its ENGAGING state so a new leader after a handover inherits it.
        // Capped so the message stays well inside NOXMFD's 4096-byte limit even with long names.
        internal static string TasksMessage(string squadron)
        {
            const int MaxTasks = 40, MaxSentName = 40;
            var mine = _board.FindAll(e => e.Assignees.Exists(a => a.Squadron == squadron));
            mine.Sort((a, b) => b.Priority != a.Priority ? b.Priority.CompareTo(a.Priority) : a.Seq.CompareTo(b.Seq));
            if (mine.Count > MaxTasks)
            {
                Plugin.Log?.LogWarning($"[TAC] {squadron} has {mine.Count} targets; sending its first {MaxTasks}.");
                mine.RemoveRange(MaxTasks, mine.Count - MaxTasks);
            }
            var sb = new StringBuilder(32 + mine.Count * 64);
            sb.Append("{\"t\":\"tasks\",\"l\":[");
            for (int i = 0; i < mine.Count; i++)
            {
                Entry e = mine[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(e.Id.ToString(CultureInfo.InvariantCulture)).Append(",\"n\":");
                TacJson.AppendString(sb, Clip(e.Name, MaxSentName));
                sb.Append(",\"p\":").Append(e.Priority)
                  .Append(",\"e\":").Append(e.Assignees.Find(a => a.Squadron == squadron)!.Engaged ? 1 : 0).Append('}');
            }
            return sb.Append("]}").ToString();
        }

        private static Entry? Find(uint id) => _board.Find(e => e.Id == id);

        private static string Clip(string? s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length <= max ? s : s.Substring(0, max);
        }

        // The board's part of the slice TacSession publishes; "sq" is the squadron roster (the
        // matrix columns).
        internal static void AppendJson(StringBuilder sb, List<string> squadrons)
        {
            sb.Append("\"board\":[");
            for (int i = 0; i < _board.Count; i++)
            {
                Entry e = _board[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(e.Id.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"n\":"); TacJson.AppendString(sb, e.Name);
                sb.Append(",\"p\":").Append(e.Priority)
                  .Append(",\"s\":").Append(e.Seq)
                  .Append(",\"a\":[");
                for (int j = 0; j < e.Assignees.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append("{\"q\":"); TacJson.AppendString(sb, e.Assignees[j].Squadron);
                    sb.Append(",\"e\":").Append(e.Assignees[j].Engaged ? 1 : 0).Append('}');
                }
                sb.Append("]}");
            }
            sb.Append("],\"sq\":[");
            for (int i = 0; i < squadrons.Count; i++)
            {
                if (i > 0) sb.Append(',');
                TacJson.AppendString(sb, squadrons[i]);
            }
            sb.Append(']');
        }
    }
}
