using TacModule;
using static TacModule.TacRole;

namespace TacModule.Tests
{
    public class TacRoleTests
    {
        private const ulong Low = 100, Me = 200, High = 300;
        private const string Blue = "BDF";

        private static TacRole Holding(ulong self = Me)
        {
            var r = new TacRole();
            Assert.True(r.Apply(self, inSquad: false, Blue, 0f));
            return r;
        }

        [Fact]
        public void Apply_needs_a_free_role_no_squad_and_a_faction()
        {
            Assert.False(new TacRole().Apply(Me, inSquad: true, Blue, 0f));
            Assert.False(new TacRole().Apply(Me, inSquad: false, null, 0f));
            Assert.False(new TacRole().Apply(0, inSquad: false, Blue, 0f));
            var taken = new TacRole();
            taken.OnClaim(High, Me, 0f);
            Assert.False(taken.Apply(Me, inSquad: false, Blue, 1f));
            Assert.True(Holding().IsMine(Me));
        }

        [Fact]
        public void A_holder_claims_at_once_then_every_interval()
        {
            var r = Holding();
            Assert.Equal(Tick.SendClaim, r.Update(Me, false, Blue, 0f));
            Assert.Equal(Tick.None, r.Update(Me, false, Blue, ClaimIntervalSeconds - 0.1f));
            Assert.Equal(Tick.SendClaim, r.Update(Me, false, Blue, ClaimIntervalSeconds));
        }

        [Fact]
        public void Lower_steamid_wins_a_claim_race()
        {
            var r = Holding();
            Assert.Equal(Heard.Contested, r.OnClaim(High, Me, 1f));   // we keep it and answer at once
            Assert.True(r.IsMine(Me));
            Assert.Equal(Heard.SteppedDown, r.OnClaim(Low, Me, 1f));
            Assert.Equal(Low, r.Holder);
        }

        [Fact]
        public void An_observer_keeps_the_lower_of_two_racing_claimers()
        {
            var r = new TacRole();
            r.OnClaim(Low, Me, 0f);
            Assert.Equal(Heard.Ignored, r.OnClaim(High, Me, 1f));
            Assert.Equal(Low, r.Holder);
            // Once the lower one goes silent past the timeout, the other's claim takes over.
            Assert.Equal(Heard.Noted, r.OnClaim(High, Me, HolderTimeoutSeconds + 1f));
            Assert.Equal(High, r.Holder);
        }

        [Fact]
        public void Joining_a_squad_or_changing_faction_releases_the_role()
        {
            Assert.Equal(Tick.SendRelease, Holding().Update(Me, inSquad: true, Blue, 1f));
            Assert.Equal(Tick.SendRelease, Holding().Update(Me, false, "PALA", 1f));
            Assert.Equal(Tick.SendRelease, Holding().Update(Me, false, null, 1f));
            var r = Holding();
            r.Update(Me, inSquad: true, Blue, 1f);
            Assert.Equal(0ul, r.Holder);
        }

        [Fact]
        public void A_silent_holder_is_gone_after_the_timeout()
        {
            var r = new TacRole();
            r.OnClaim(High, Me, 0f);
            Assert.Equal(Tick.None, r.Update(Me, false, Blue, HolderTimeoutSeconds - 0.1f));
            Assert.Equal(Tick.HolderGone, r.Update(Me, false, Blue, HolderTimeoutSeconds));
            Assert.Equal(0ul, r.Holder);
        }

        [Fact]
        public void Release_only_from_the_holder_frees_the_role()
        {
            var r = new TacRole();
            r.OnClaim(High, Me, 0f);
            Assert.False(r.OnRelease(Low, Me));
            Assert.True(r.OnRelease(High, Me));
            Assert.Equal(0ul, r.Holder);
            Assert.False(new TacRole().Release(Me));
            Assert.True(Holding().Release(Me));
        }
    }
}
