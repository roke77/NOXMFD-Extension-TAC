using System.Text;
using TacModule;

namespace TacModule.Tests
{
    public class TacJsonTests
    {
        private static string Write(string? s)
        {
            var sb = new StringBuilder();
            TacJson.AppendString(sb, s);
            return sb.ToString();
        }

        [Fact]
        public void Quotes_backslashes_and_control_characters_are_escaped()
        {
            Assert.Equal("\"SA-15 \\\"Tor\\\"\"", Write("SA-15 \"Tor\""));
            Assert.Equal("\"a\\\\b\"", Write("a\\b"));
            Assert.Equal("\"line\\u000abreak\"", Write("line\nbreak"));
            Assert.Equal("\"\"", Write(null));
        }

        [Fact]
        public void AppendTarget_opens_an_object_the_caller_closes()
        {
            var sb = TacJson.AppendTarget(new StringBuilder(), 4294967295u, "BMP-2").Append(",\"p\":1}");
            Assert.Equal("{\"id\":4294967295,\"n\":\"BMP-2\",\"p\":1}", sb.ToString());
        }

        [Fact]
        public void Clip_trims_and_cuts_to_the_limit()
        {
            Assert.Equal("SA-15", TacJson.Clip("  SA-15 Tor ", 5));
            Assert.Equal("SA-15 Tor", TacJson.Clip("  SA-15 Tor ", 40));
            Assert.Equal("", TacJson.Clip(null, 5));
        }
    }
}
