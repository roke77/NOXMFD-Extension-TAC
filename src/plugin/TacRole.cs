namespace TacModule
{
    // Who holds the TAC role in this faction, as this client sees it (docs/tac-plan.md: decisions 3,
    // 12 and 13; startup steps 1, 3, 6 and 7). Pure: SteamIDs, squad standing, faction and time are
    // passed in, so tools/tests drives it directly; TacSession.cs is the live glue that sends what it
    // asks for and feeds it what arrives.
    internal sealed class TacRole
    {
        internal const float ClaimIntervalSeconds = 3f;
        internal const float HolderTimeoutSeconds = 15f;

        internal enum Tick { None, SendClaim, SendRelease, HolderGone }
        internal enum Heard { Ignored, Noted, SteppedDown, Contested }

        internal ulong Holder { get; private set; }   // 0 = nobody holds the role
        private float _lastHeard;                    // when the current (other) holder last claimed
        private float _nextClaim;                    // ours: when the next periodic claim is due
        private string? _claimFaction;               // ours: the faction we took the role in

        internal bool IsMine(ulong self) => self != 0 && Holder == self;

        // APPLY FOR TAC: only with nobody holding it, only outside a squad, only inside a faction.
        internal bool CanApply(ulong self, bool inSquad, string? faction) =>
            self != 0 && Holder == 0 && !inSquad && faction != null;

        internal bool Apply(ulong self, bool inSquad, string? faction, float now)
        {
            if (!CanApply(self, inSquad, faction)) return false;
            Holder = self;
            _claimFaction = faction;
            _nextClaim = now;   // announce at once
            return true;
        }

        // RELEASE, and every automatic loss below: true when the caller should tell the faction.
        internal bool Release(ulong self)
        {
            if (!IsMine(self)) return false;
            Holder = 0;
            _claimFaction = null;
            return true;
        }

        // A claim from a faction-mate outside any squad (the caller checks both). Lower SteamID wins a
        // race (decision 12): a holder hearing a lower id steps down; hearing a higher one it answers
        // with its own claim at once, so the other side steps down without waiting a full interval.
        internal Heard OnClaim(ulong from, ulong self, float now)
        {
            if (from == 0 || from == self) return Heard.Ignored;
            if (IsMine(self))
            {
                if (from > self) return Heard.Contested;
                Holder = from;
                _claimFaction = null;
                _lastHeard = now;
                return Heard.SteppedDown;
            }
            bool current = Holder != 0 && now - _lastHeard < HolderTimeoutSeconds;
            if (current && Holder != from && from > Holder) return Heard.Ignored;   // the lower id keeps it
            Holder = from;
            _lastHeard = now;
            return Heard.Noted;
        }

        internal bool OnRelease(ulong from, ulong self)
        {
            if (from == 0 || from == self || Holder != from) return false;
            Holder = 0;
            return true;
        }

        // Called a few times a second. Ours: losing the role on joining a squad or leaving/changing
        // faction (decisions 3, 13), else the periodic claim. Someone else's: gone after a silence
        // (a crash or dropped connection never sends release).
        internal Tick Update(ulong self, bool inSquad, string? faction, float now)
        {
            if (IsMine(self))
            {
                if (inSquad || faction == null || faction != _claimFaction)
                {
                    Release(self);
                    return Tick.SendRelease;
                }
                if (now < _nextClaim) return Tick.None;
                _nextClaim = now + ClaimIntervalSeconds;
                return Tick.SendClaim;
            }
            if (Holder != 0 && now - _lastHeard >= HolderTimeoutSeconds)
            {
                Holder = 0;
                return Tick.HolderGone;
            }
            return Tick.None;
        }

        // A new scene (mission start or end): whatever we knew belongs to the last match.
        internal void Reset()
        {
            Holder = 0;
            _claimFaction = null;
        }
    }
}
