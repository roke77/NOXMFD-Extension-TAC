using System;
using System.Collections.Generic;

namespace TacModule
{
    // The officer's squadron roster (docs/tac-plan.md: decision 16, startup steps 2-5): one entry per
    // squadron whose leader runs TAC, keyed by callsign + flight ("VIPER 1") so a squadron keeps its
    // identity when its leadership changes hands. Pure: time is passed in; tools/tests drives it.
    internal sealed class TacRoster
    {
        // Leaders answer every claim (3 s apart); three missed answers and the squadron is gone.
        internal const float SilentSeconds = 10f;

        internal enum Heard { Known, New, HandedOver }

        private sealed class Entry
        {
            public ulong Leader;
            public float LastHeard;
        }

        private readonly Dictionary<string, Entry> _byKey = new Dictionary<string, Entry>(StringComparer.Ordinal);

        internal static string Key(string callsign, int flight) => callsign + " " + flight;

        // A verified hello. A leader who now answers for another squadron (renamed, or moved) stops
        // answering for the old one, which is dropped at once rather than after the silence.
        internal Heard Note(string key, ulong leader, float now, List<string> dropped)
        {
            foreach (var kv in _byKey)
                if (kv.Key != key && kv.Value.Leader == leader) dropped.Add(kv.Key);
            foreach (string k in dropped) _byKey.Remove(k);

            if (!_byKey.TryGetValue(key, out Entry? e))
            {
                _byKey[key] = new Entry { Leader = leader, LastHeard = now };
                return Heard.New;
            }
            e.LastHeard = now;
            if (e.Leader == leader) return Heard.Known;
            e.Leader = leader;
            return Heard.HandedOver;
        }

        // Squadrons whose leader stopped answering: removed, and added to `dropped`.
        internal void Expire(float now, List<string> dropped)
        {
            foreach (var kv in _byKey)
                if (now - kv.Value.LastHeard >= SilentSeconds) dropped.Add(kv.Key);
            foreach (string k in dropped) _byKey.Remove(k);
        }

        internal bool TryLeader(string key, out ulong leader)
        {
            leader = _byKey.TryGetValue(key, out Entry? e) ? e.Leader : 0;
            return leader != 0;
        }

        // The squadron a leader currently answers for, if any.
        internal string? KeyOf(ulong leader)
        {
            foreach (var kv in _byKey) if (kv.Value.Leader == leader) return kv.Key;
            return null;
        }

        // Callsign order, then flight, for the matrix columns.
        internal List<string> Keys()
        {
            var keys = new List<string>(_byKey.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        internal void Clear() => _byKey.Clear();
    }
}
