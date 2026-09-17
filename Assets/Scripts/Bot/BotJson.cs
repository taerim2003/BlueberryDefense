#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// 봇 기록용 최소 JSON 직렬화. JsonUtility는 Dictionary·중첩 가변 구조를 못 써서 따로 둔다.
// 값: null · bool · 정수/실수 · string · IDictionary(string 키) · IEnumerable. 그 밖은 ToString()을 문자열로.
public static class BotJson
{
    public static string Write(object value)
    {
        var sb = new StringBuilder();
        Append(sb, value);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, object v)
    {
        switch (v)
        {
            case null: sb.Append("null"); return;
            case bool b: sb.Append(b ? "true" : "false"); return;
            case string s: AppendString(sb, s); return;
            case int or long or short or byte: sb.Append(System.Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture)); return;
            case float f: AppendNumber(sb, f); return;
            case double d: AppendNumber(sb, d); return;
            case IDictionary dict:
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    AppendString(sb, e.Key.ToString());
                    sb.Append(':');
                    Append(sb, e.Value);
                }
                sb.Append('}');
                return;
            }
            case IEnumerable list:
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Append(sb, item);
                }
                sb.Append(']');
                return;
            }
            default: AppendString(sb, v.ToString()); return;
        }
    }

    // NaN·무한대는 JSON에 없다 — null로 쓴다(분석기에서 결측으로 다룬다).
    private static void AppendNumber(StringBuilder sb, double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
        sb.Append(d.ToString("0.####", CultureInfo.InvariantCulture));
    }

    private static void AppendString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    // 기록 코드가 짧아지게 — new Dictionary<string, object> 대신.
    public static Dictionary<string, object> Obj() => new Dictionary<string, object>();
}
#endif
