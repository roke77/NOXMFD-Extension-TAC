using TacModule;
using static TacModule.TacRoster;

namespace TacModule.Tests
{
    public class TacRosterTests
    {
        private const ulong Alice = 1, Bob = 2;

        [Fact]
        public void A_first_hello_is_new_then_known()
        {
            var r = new TacRoster();
            var dropped = new List<string>();
            Assert.Equal(Heard.New, r.Note("VIPER 1", Alice, 0f, dropped));
            Assert.Equal(Heard.Known, r.Note("VIPER 1", Alice, 3f, dropped));
            Assert.Empty(dropped);
            Assert.True(r.TryLeader("VIPER 1", out ulong l));
            Assert.Equal(Alice, l);
        }

        [Fact]
        public void A_new_leader_for_the_same_squadron_is_a_handover()
        {
            var r = new TacRoster();
            var dropped = new List<string>();
            r.Note("VIPER 1", Alice, 0f, dropped);
            Assert.Equal(Heard.HandedOver, r.Note("VIPER 1", Bob, 1f, dropped));
            Assert.Equal("VIPER 1", r.KeyOf(Bob));
            Assert.Null(r.KeyOf(Alice));
        }

        [Fact]
        public void A_leader_answering_for_a_renamed_squadron_drops_the_old_name_at_once()
        {
            var r = new TacRoster();
            var dropped = new List<string>();
            r.Note("VIPER 1", Alice, 0f, dropped);
            Assert.Equal(Heard.New, r.Note("COBRA 2", Alice, 1f, dropped));
            Assert.Equal(new[] { "VIPER 1" }, dropped);
            Assert.Equal(new[] { "COBRA 2" }, r.Keys());
        }

        [Fact]
        public void A_silent_squadron_expires()
        {
            var r = new TacRoster();
            var dropped = new List<string>();
            r.Note("VIPER 1", Alice, 0f, dropped);
            r.Note("RAVEN 3", Bob, 5f, dropped);
            r.Expire(SilentSeconds, dropped);
            Assert.Equal(new[] { "VIPER 1" }, dropped);
            Assert.Equal(new[] { "RAVEN 3" }, r.Keys());
        }

        [Fact]
        public void Keys_sort_by_callsign_then_flight()
        {
            var r = new TacRoster();
            var dropped = new List<string>();
            r.Note("VIPER 2", Alice, 0f, dropped);
            r.Note("COBRA 1", Bob, 0f, dropped);
            r.Note("VIPER 1", 3, 0f, dropped);
            Assert.Equal(new[] { "COBRA 1", "VIPER 1", "VIPER 2" }, r.Keys());
        }
    }
}
