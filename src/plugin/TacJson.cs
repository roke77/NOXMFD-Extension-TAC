using System.Globalization;
using System.Text;

namespace TacModule
{
    // JSON string writing for the slice and messages. Names come from the game and from other
    // players, so every one is escaped. BCL-only; tools/tests covers it.
    internal static class TacJson
    {
        internal static void AppendString(StringBuilder sb, string? s)
        {
            sb.Append('"');
            foreach (char ch in s ?? "")
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
