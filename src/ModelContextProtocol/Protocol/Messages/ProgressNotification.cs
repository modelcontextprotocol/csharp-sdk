using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Represents an out-of-band notification used to inform the receiver of a progress update for a long-running request.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for more details.
/// </remarks>
[JsonConverter(typeof(Converter))]
public class ProgressNotification
{
    /// <summary>
    /// Gets or sets the progress token which was given in the initial request, used to associate this notification with 
    /// the corresponding request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This token acts as a correlation identifier that links progress updates to their corresponding request.
    /// </para>
    /// <para>
    /// When an endpoint initiates a request with a <see cref="ProgressToken"/> in its metadata, 
    /// the receiver can send progress notifications using this same token. This allows both sides to 
    /// correlate the notifications with the original request.
    /// </para>
    /// </remarks>
    public required ProgressToken ProgressToken { get; init; }

    /// <summary>
    /// Gets or sets the progress thus far.
    /// </summary>
    /// <remarks>
    /// This should increase for each notification issued as part of the same request, even if the total is unknown.
    /// </remarks>
    public required ProgressNotificationValue Progress { get; init; }

    /// <summary>
    /// Provides a <see cref="JsonConverter"/> for <see cref="ProgressNotification"/>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class Converter : JsonConverter<ProgressNotification>
    {
        /// <inheritdoc />
        public override ProgressNotification? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            ProgressToken? progressToken = null;
            float? progress = null;
            float? total = null;
            string? message = null;
            
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var propertyName = reader.GetString();
                    reader.Read();
                    switch (propertyName)
                    {
                        case "progressToken":
                            progressToken = (ProgressToken)JsonSerializer.Deserialize(ref reader, options.GetTypeInfo(typeof(ProgressToken)))!;
                            break;

                        case "progress":
                            progress = reader.GetSingle();
                            break;

                        case "total":
                            total = reader.GetSingle();
                            break;

                        case "message":
                            message = reader.GetString();
                            break;
                    }
                }
            }

            if (progress is null)
            {
                throw new JsonException("Missing required property 'progress'.");
            }

            if (progressToken is null)
            {
                throw new JsonException("Missing required property 'progressToken'.");
            }

            return new ProgressNotification
            {
                ProgressToken = progressToken.GetValueOrDefault(),
                Progress = new ProgressNotificationValue()
                {
                    Progress = progress.GetValueOrDefault(),
                    Total = total,
                    Message = message
                }
            };
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, ProgressNotification value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("progressToken");
            JsonSerializer.Serialize(writer, value.ProgressToken, options.GetTypeInfo(typeof(ProgressToken)));

            writer.WriteNumber("progress", value.Progress.Progress);

            if (value.Progress.Total is { } total)
            {
                writer.WriteNumber("total", total);
            }

            if (value.Progress.Message is { } message)
            {
                writer.WriteString("message", message);
            }

            writer.WriteEndObject();
        }
    }
}
