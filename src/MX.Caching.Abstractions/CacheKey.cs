using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MX.Caching.Abstractions;

/// <summary>
/// A normalized, versioned cache key.
/// </summary>
public readonly record struct CacheKey
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CacheKey"/> struct.
    /// </summary>
    /// <param name="value">The normalized cache key value.</param>
    public CacheKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>
    /// Gets the normalized key value.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Value;
    }
}

/// <summary>
/// Builds normalized, versioned cache keys from a client, method, and arguments.
/// </summary>
public static class CacheKeyBuilder
{
    /// <summary>
    /// Creates a cache key from the supplied operation identity and argument values.
    /// </summary>
    /// <param name="version">The cache key namespace version.</param>
    /// <param name="client">The client or service identity.</param>
    /// <param name="method">The operation identity.</param>
    /// <param name="arguments">The operation argument values.</param>
    /// <returns>A normalized cache key.</returns>
    public static CacheKey Create(
        string version,
        string client,
        string method,
        params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var builder = new StringBuilder();
        AppendSegment(builder, version);
        AppendSegment(builder, client);
        AppendSegment(builder, method);

        foreach (var argument in arguments ?? [])
        {
            AppendSegment(builder, Normalize(argument));
        }

        return new CacheKey(builder.ToString());
    }

    private static void AppendSegment(StringBuilder builder, string value)
    {
        if (builder.Length > 0)
        {
            _ = builder.Append(':');
        }

        _ = builder.Append(Uri.EscapeDataString(value.Trim()));
    }

    private static string Normalize(object? value)
    {
        var normalizedValue = value switch
        {
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D"),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            null => string.Empty,
            _ => SerializeCanonicalJson(value),
        };

        var typeName = value?.GetType().FullName ?? "null";
        return $"{typeName}|{normalizedValue}";
    }

    private static string SerializeCanonicalJson(object value)
    {
        var element = JsonSerializer.SerializeToElement(value, value.GetType());
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        WriteCanonicalJson(writer, element);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonicalJson(writer, property.Value);
            }

            writer.WriteEndObject();
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
            {
                WriteCanonicalJson(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        element.WriteTo(writer);
    }
}
