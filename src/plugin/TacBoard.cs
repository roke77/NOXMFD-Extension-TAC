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
            if (c.cmd == "pull")
            {
                // Answered by the squadron's leader over messaging (docs/tac-plan.md, build step 3);
                // until then there is nobody to ask.
                Plugin.Log?.LogInfo($"[TAC] PULL from '{c.sq}' not sent: squad messaging isn't available yet.");
                return;
            }
            if (c.id == 0)
            {
                Plugin.Log?.LogWarning($"[TAC] '{c.cmd}' command without a unit id.");
                return;
            }

            Entry? e = Find(c.id);
            switch (c.cmd)
            {
                case "add":
                    if (e != null) return;   // one row per unit, however many ways it arrives
                    _board.Add(new Entry { Id = c.id, Name = Clip(c.name, MaxNameLength), Seq = ++_seq });
                    break;
                case "remove":
                    if (e == null || !_board.Remove(e)) return;
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
            Publish();
        }

        // The board lives for one mission (decision 11): any scene load — a new mission or the way
        // back to the menu — starts it empty.
        internal static void Clear()
        {
            if (_board.Count == 0) return;
            _board.Clear();
            Publish();
        }

        private static Entry? Find(uint id) => _board.Find(e => e.Id == id);

        private static string Clip(string? s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length <= max ? s : s.Substring(0, max);
        }

        // Re-published on every change rather than on a timer: NOXMFD folds the last-published slice
        // into every telemetry frame (EXTENSIONS.md's PublishSlice), so a page that opens later gets
        // the current board on its first frame. "sq" is the squadron roster, filled once squad
        // leaders announce themselves over messaging (docs/tac-plan.md, build step 3).
        private static void Publish()
        {
            var sb = new StringBuilder(64 + _board.Count * 64);
            sb.Append("{\"board\":[");
            for (int i = 0; i < _board.Count; i++)
            {
                Entry e = _board[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(e.Id.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"n\":"); AppendString(sb, e.Name);
                sb.Append(",\"p\":").Append(e.Priority)
                  .Append(",\"s\":").Append(e.Seq)
                  .Append(",\"a\":[");
                for (int j = 0; j < e.Assignees.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append("{\"q\":"); AppendString(sb, e.Assignees[j].Squadron);
                    sb.Append(",\"e\":").Append(e.Assignees[j].Engaged ? 1 : 0).Append('}');
                }
                sb.Append("]}");
            }
            sb.Append("],\"sq\":[]}");
            NOXMFD.Api.PublishSlice(Plugin.ExtId, sb.ToString());
        }

        private static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
