using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExamSystem;

/// <summary>
/// 统一所有 DateTime 的 JSON 序列化行为：始终以 UTC（带 Z 后缀）输出，
/// 反序列化时把无时区字符串按 UTC 解析。
/// 这样前端用 new Date(x).toLocaleString() 即可正确还原为浏览器本地时间，
/// 避免 SQLite 读回 Kind=Unspecified 导致序列化丢时区、前端误判本地时间（+8h 偏差）。
/// </summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    private const string Fmt = "yyyy-MM-ddTHH:mm:ssZ";

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // SQLite 读回的 DateTime 常为 Kind=Unspecified（值实际为 UTC tick）；
        // 显式指定为 Utc 再格式化，避免 ToUniversalTime() 把 Unspecified 当本地再 -8h。
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        writer.WriteStringValue(utc.ToString(Fmt, CultureInfo.InvariantCulture));
    }

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (string.IsNullOrEmpty(s)) return default;
        // 前端 toISOString() 发来的是带 Z 的 UTC；本地 datetime-local 转 ISO 也可能不带 Z，
        // 统一按 Universal 解析，保证服务端存储与比较（DateTime.UtcNow）一致。
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
            return dt;
        return DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
    }
}
