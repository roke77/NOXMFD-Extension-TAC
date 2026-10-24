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

        private static Tick Update(TacRole r, bool inSquad, string? faction, float now, List<ulong>? gone = null) =>
            r.Update(Me, inSquad, faction, now, gone ?? new List<ulong>());

        [Fact]
        public void Apply_needs_no_squad_and_a_faction_but_not_a_free_role()
        {
            Assert.False(new TacRole().Apply(Me, inSquad: true, Blue, 0f));
            Assert.False(new TacRole().Apply(Me, inSquad: false, null, 0f));
            Assert.False(new TacRole().Apply(0, inSquad: false, Blue, 0f));
            var withOthers = new TacRole();
            withOthers.OnClaim(Low, Me, 0f);
            withOthers.OnClaim(High, Me, 0f);
            Assert.True(withOthers.Apply(Me, inSquad: false, Blue, 1f));
            Assert.Equal(new List<ulong> { Low, High }, withOthers.Officers());
        }

        [Fact]
        public void An_officer_claims_at_once_then_every_interval()
        {
            var r = Holding();
            Assert.Equal(Tick.SendClaim, Update(r, false, Blue, 0f));
            Assert.Equal(Tick.None, Update(r, false, Blue, ClaimIntervalSeconds - 0.1f));
            Assert.Equal(Tick.SendClaim, Update(r, false, Blue, ClaimIntervalSeconds));
        }

        [Fact]
        public void Joining_a_squad_or_changing_faction_releases_the_role()
        {
            Assert.Equal(Tick.SendRelease, Update(Holding(), inSquad: true, Blue, 1f));
            Assert.Equal(Tick.SendRelease, Update(Holding(), false, "PALA", 1f));
            Assert.Equal(Tick.SendRelease, Update(Holding(), false, null, 1f));
            var r = Holding();
            Update(r, inSquad: true, Blue, 1f);
            Assert.False(r.IsMine(Me));
        }

        [Fact]
        public void A_silent_officer_is_gone_after_the_timeout_and_takes_the_link()
        {
            var r = new TacRole();
            r.OnClaim(High, Me, 0f);
            r.OnClaim(Low, Me, 10f);
            Assert.True(r.LinkTo(High, out _));
            var gone = new List<ulong>();
            Update(r, false, Blue, OfficerTimeoutSeconds - 0.1f, gone);
            Assert.Empty(gone);
            Update(r, false, Blue, OfficerTimeoutSeconds, gone);
            Assert.Equal(new List<ulong> { High }, gone);
            Assert.Equal(new List<ulong> { Low }, r.Officers());
            Assert.Equal(Link.None, r.LinkState);
        }

        [Fact]
        public void A_link_is_pending_until_the_officer_confirms()
        {
            var r = new TacRole();
            Assert.False(r.LinkTo(High, out _));   // not an officer
            r.OnClaim(High, Me, 0f);
            Assert.True(r.LinkTo(High, out ulong previous));
            Assert.Equal(0ul, previous);
            Assert.Equal(Link.Pending, r.LinkState);
            Assert.False(r.Confirm(Low));
            Assert.True(r.Confirm(High));
            Assert.False(r.Confirm(High));
            Assert.Equal(Link.Linked, r.LinkState);
        }

        [Fact]
        public void Switching_officers_names_the_one_left_and_starts_pending()
        {
            var r = new TacRole();
            r.OnClaim(Low, Me, 0f);
            r.OnClaim(High, Me, 0f);
            r.LinkTo(Low, out _);
            r.Confirm(Low);
            Assert.False(r.LinkTo(Low, out _));
            Assert.True(r.LinkTo(High, out ulong previous));
            Assert.Equal(Low, previous);
            Assert.Equal(High, r.Linked);
            Assert.Equal(Link.Pending, r.LinkState);
        }

        [Fact]
        public void An_officers_release_drops_them_and_their_link()
        {
            var r = new TacRole();
            r.OnClaim(High, Me, 0f);
            r.LinkTo(High, out _);
            Assert.False(r.OnRelease(Low));
            Assert.True(r.OnRelease(High));
            Assert.Empty(r.Officers());
            Assert.Equal(0ul, r.Linked);
            Assert.False(new TacRole().Release(Me));
            Assert.True(Holding().Release(Me));
        }
    }
}
