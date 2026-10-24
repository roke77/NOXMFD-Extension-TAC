using System.Text;
using TacModule;

namespace TacModule.Tests
{
    public class TacReportsTests
    {
        private static string Json(TacReports r)
        {
            var sb = new StringBuilder();
            r.AppendJson(sb);
            return sb.ToString();
        }

        [Fact]
        public void A_report_waits_in_its_squadrons_bucket_once()
        {
            var r = new TacReports();
            Assert.True(r.Add("VIPER 1", 40, "Helipad"));
            Assert.False(r.Add("VIPER 1", 40, "Helipad"));
            Assert.True(r.Add("COLT 2", 40, "Helipad"));
            Assert.Equal(1, r.Count("VIPER 1"));
            Assert.Equal(0, r.Count("HAWK 3"));
            Assert.Equal("{\"VIPER 1\":[{\"id\":40,\"n\":\"Helipad\"}],\"COLT 2\":[{\"id\":40,\"n\":\"Helipad\"}]}", Json(r));
        }

        [Fact]
        public void Pruned_reports_leave_every_bucket_and_empty_buckets_are_not_written()
        {
            var r = new TacReports();
            r.Add("VIPER 1", 40, "Helipad");
            r.Add("VIPER 1", 41, "Tanker");
            r.Add("COLT 2", 40, "Helipad");
            Assert.True(r.Prune(id => id == 40));
            Assert.False(r.Prune(id => id == 40));
            Assert.Equal("{\"VIPER 1\":[{\"id\":41,\"n\":\"Tanker\"}]}", Json(r));
        }

        [Fact]
        public void A_full_bucket_drops_its_oldest_report()
        {
            var r = new TacReports();
            for (uint id = 1; id <= TacReports.MaxPerSquadron + 1; id++) r.Add("VIPER 1", id, "");
            Assert.Equal(TacReports.MaxPerSquadron, r.Count("VIPER 1"));
            Assert.DoesNotContain("\"id\":1,", Json(r));
        }

        [Fact]
        public void A_squadron_that_leaves_takes_its_bucket()
        {
            var r = new TacReports();
            r.Add("VIPER 1", 40, "Helipad");
            r.RemoveSquadron("VIPER 1");
            Assert.Equal("{}", Json(r));
        }
    }
}
