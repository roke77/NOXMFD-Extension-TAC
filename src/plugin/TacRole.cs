using System.Collections.Generic;

namespace TacModule
{
    // The faction's TAC officers and this client's own standing, as this client sees them
    // (docs/tac-plan.md: decisions 3, 12, 13 and 24; startup steps 1, 3, 6 and 7): whether this
    // player is an officer, and which officer this squad leader is linked to. Pure: SteamIDs, squad
    // standing, faction and time are passed in, so tools/tests drives it directly; TacSession.cs is
    // the live glue that sends what it asks for and feeds it what arrives.
    internal sealed class TacRole
    {
        // NOXMFD's presence beat interval: a claim goes to every squad leader, so no faster than that.
        internal const float ClaimIntervalSeconds = 5f;
        internal const float OfficerTimeoutSeconds = 25f;   // five missed claims

        internal enum Tick { None, SendClaim, SendRelease }
        internal enum Link { None, Pending, Linked }

        private readonly Dictionary<ulong, float> _officers = new Dictionary<ulong, float>();   // other officers -> last claim
        private bool _mine;
        private float _nextClaim;                    // ours: when the next periodic claim is due
        private string? _claimFaction;               // ours: the faction we took the role in

        internal bool IsMine(ulong self) => self != 0 && _mine;

        // The officer this squad leader listens to (0 = none), and whether that officer has confirmed
        // the link by sending its task list.
        internal ulong Linked { get; private set; }
        internal Link LinkState => Linked == 0 ? Link.None : _confirmed ? Link.Linked : Link.Pending;
        private bool _confirmed;

        // Every other officer heard from, lowest SteamID first so the list doesn't reorder.
        internal List<ulong> Officers()
        {
            var ids = new List<ulong>(_officers.Keys);
            ids.Sort();
            return ids;
        }

        internal bool IsOfficer(ulong id) => _officers.ContainsKey(id);

        // APPLY FOR TAC: any player outside a squad and inside a faction, however many officers it
        // already has.
        internal bool CanApply(ulong self, bool inSquad, string? faction) =>
            self != 0 && !_mine && !inSquad && faction != null;

        internal bool Apply(ulong self, bool inSquad, string? faction, float now)
        {
            if (!CanApply(self, inSquad, faction)) return false;
            _mine = true;
            _claimFaction = faction;
            _nextClaim = now;   // announce at once
            return true;
        }

        // RELEASE, and every automatic loss below: true when the caller should tell the faction.
        internal bool Release(ulong self)
        {
            if (!IsMine(self)) return false;
            _mine = false;
            _claimFaction = null;
            return true;
        }

        // A claim from a faction-mate outside any squad (the caller checks both). True the first
        // time this officer is heard.
        internal bool OnClaim(ulong from, ulong self, float now)
        {
            if (from == 0 || from == self) return false;
            bool known = _officers.ContainsKey(from);
            _officers[from] = now;
            return !known;
        }

        // True when that officer was known; a link to them goes with them.
        internal bool OnRelease(ulong from)
        {
            if (!_officers.Remove(from)) return false;
            if (Linked == from) Unlink();
            return true;
        }

        // LINK on the squad leader's page: only to a current officer. Returns the officer this
        // replaces (0 when none), so the caller can tell them the squadron left.
        internal bool LinkTo(ulong officer, out ulong previous)
        {
            previous = 0;
            if (officer == 0 || !_officers.ContainsKey(officer) || officer == Linked) return false;
            previous = Linked;
            Linked = officer;
            _confirmed = false;
            return true;
        }

        // The linked officer's task list arrived: the handshake is done.
        internal bool Confirm(ulong from)
        {
            if (from == 0 || from != Linked || _confirmed) return false;
            _confirmed = true;
            return true;
        }

        internal void Unlink()
        {
            Linked = 0;
            _confirmed = false;
        }

        // Called a few times a second. Ours: losing the role on joining a squad or leaving/changing
        // faction (decisions 3, 13), else the periodic claim. Others: gone after a silence (a crash or
        // dropped connection never sends release), added to `gone`; a link to one of them goes too.
        internal Tick Update(ulong self, bool inSquad, string? faction, float now, List<ulong> gone)
        {
            foreach (var kv in _officers)
                if (now - kv.Value >= OfficerTimeoutSeconds) gone.Add(kv.Key);
            foreach (ulong id in gone)
            {
                _officers.Remove(id);
                if (Linked == id) Unlink();
            }

            if (!IsMine(self)) return Tick.None;
            if (inSquad || faction == null || faction != _claimFaction)
            {
                Release(self);
                return Tick.SendRelease;
            }
            if (now < _nextClaim) return Tick.None;
            _nextClaim = now + ClaimIntervalSeconds;
            return Tick.SendClaim;
        }

        // A new scene (mission start or end): whatever we knew belongs to the last match.
        internal void Reset()
        {
            _officers.Clear();
            _mine = false;
            _claimFaction = null;
            Unlink();
        }
    }
}
