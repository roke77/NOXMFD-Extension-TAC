using System;
using System.Collections.Generic;
using System.Text;

namespace TacModule
{
    // The officer's PULL buckets (docs/tac-plan.md, decision 24): per squadron, the targets its leader
    // has REPORTed and the officer hasn't added yet. A report waits here until the officer pulls it
    // onto the board from the PULL panel; nothing about a leader's locks arrives any other way.
    // Pure; tools/tests drives it.
    internal sealed class TacReports
    {
        // The bucket rides every telemetry frame in the slice: past this, the oldest report goes.
        internal const int MaxPerSquadron = 40;

        private readonly Dictionary<string, List<KeyValuePair<uint, string>>> _bySquadron =
            new Dictionary<string, List<KeyValuePair<uint, string>>>(StringComparer.Ordinal);

        // False when the squadron already reported this target.
        internal bool Add(string squadron, uint id, string name)
        {
            if (id == 0) return false;
            if (!_bySquadron.TryGetValue(squadron, out var list)) _bySquadron[squadron] = list = new List<KeyValuePair<uint, string>>();
            if (list.Exists(r => r.Key == id)) return false;
            list.Add(new KeyValuePair<uint, string>(id, name));
            if (list.Count > MaxPerSquadron) list.RemoveAt(0);
            return true;
        }

        // Drops every report `drop` matches (on the board now, or destroyed). True when any went.
        internal bool Prune(Predicate<uint> drop)
        {
            bool changed = false;
            foreach (var list in _bySquadron.Values) changed |= list.RemoveAll(r => drop(r.Key)) > 0;
            return changed;
        }

        internal void RemoveSquadron(string squadron) => _bySquadron.Remove(squadron);

        internal void Clear() => _bySquadron.Clear();

        internal int Count(string squadron) => _bySquadron.TryGetValue(squadron, out var list) ? list.Count : 0;

        // {"VIPER 1":[{"id":..,"n":".."},..],..}, oldest report first; empty buckets are left out.
        internal void AppendJson(StringBuilder sb)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kv in _bySquadron)
            {
                if (kv.Value.Count == 0) continue;
                if (!first) sb.Append(',');
                first = false;
                TacJson.AppendString(sb, kv.Key);
                sb.Append(":[");
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    TacJson.AppendTarget(sb, kv.Value[i].Key, kv.Value[i].Value).Append('}');
                }
                sb.Append(']');
            }
            sb.Append('}');
        }
    }
}
