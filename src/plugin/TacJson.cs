using System.Globalization;
using System.Text;

namespace TacModule
{
    // JSON string writing for the slice and messages. Names come from the game and from other
    // players, so every one is escaped. BCL-only; tools/tests covers it.
    internal static class TacJson
    {
        // Opens one target's object — {"id":<id>,"n":"<name>" — for the caller to add its own fields
        // and close. Every target list TAC writes (tasks, locks, the slice) starts this way.
        internal static StringBuilder AppendTarget(StringBuilder sb, uint id, string? name)
        {
            sb.Append("{\"id\":").Append(id.ToString(CultureInfo.InvariantCulture)).Append(",\"n\":");
            AppendString(sb, name);
            return sb;
        }

        // Names travel in messages capped at 4096 bytes and show in narrow columns: long ones are cut.
        internal static string Clip(string? s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length <= max ? s : s.Substring(0, max);
        }

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
