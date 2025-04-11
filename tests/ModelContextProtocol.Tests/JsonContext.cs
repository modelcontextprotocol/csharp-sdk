using System.Text.Json;
using System.Text.Json.Serialization;

public class ComplexObject
{
    public string? Name { get; set; }
    public int Age { get; set; }
}

[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(ComplexObject))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(JsonElement))]
partial class JsonContext : JsonSerializerContext;