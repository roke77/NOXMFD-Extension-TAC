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
    }
}
