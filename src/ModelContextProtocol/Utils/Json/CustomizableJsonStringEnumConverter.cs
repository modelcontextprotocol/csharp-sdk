// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

// NOTE:
// This is a temporary workaround for lack of System.Text.Json's JsonStringEnumConverter<T>
// 9.x support for JsonStringEnumMemberNameAttribute. Once all builds use the System.Text.Json 9.x
// version, this whole file can be removed.

namespace System.Text.Json.Serialization;

/// <summary>
/// A JSON converter for enums that allows customizing the serialized string value of enum members
/// using the <see cref="JsonStringEnumMemberNameAttribute"/>.
/// </summary>
/// <typeparam name="TEnum">The enum type to convert.</typeparam>
/// <remarks>
/// This is a temporary workaround for lack of System.Text.Json's JsonStringEnumConverter&lt;T&gt;
/// 9.x support for custom enum member naming. It will be replaced by the built-in functionality
/// once .NET 9 is fully adopted.
/// </remarks>
internal sealed class CustomizableJsonStringEnumConverter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TEnum> :
    JsonStringEnumConverter<TEnum> where TEnum : struct, Enum
{
#if !NET9_0_OR_GREATER
    /// <summary>
    /// Initializes a new instance of the <see cref="CustomizableJsonStringEnumConverter{TEnum}"/> class.
    /// </summary>
    /// <remarks>
    /// The converter automatically detects any enum members decorated with <see cref="JsonStringEnumMemberNameAttribute"/>
    /// and uses those values during serialization and deserialization.
    /// </remarks>
    public CustomizableJsonStringEnumConverter() :
        base(namingPolicy: ResolveNamingPolicy())
    {
    }

    private static JsonNamingPolicy? ResolveNamingPolicy()
    {
        var map = typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (f.Name, AttributeName: f.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name))
            .Where(pair => pair.AttributeName != null)
            .ToDictionary(pair => pair.Name, pair => pair.AttributeName);

        return map.Count > 0 ? new EnumMemberNamingPolicy(map!) : null;
    }

    private sealed class EnumMemberNamingPolicy(Dictionary<string, string> map) : JsonNamingPolicy
    {
        public override string ConvertName(string name) => 
            map.TryGetValue(name, out string? newName) ? 
                newName :
                name;
    }
#endif
}

#if !NET9_0_OR_GREATER
/// <summary>
/// Determines the custom string value that should be used when serializing an enum member using JSON.
/// </summary>
/// <remarks>
/// This attribute is a temporary workaround for lack of System.Text.Json's support for custom enum member naming
/// in versions prior to .NET 9. It works together with <see cref="CustomizableJsonStringEnumConverter{TEnum}"/>
/// to provide customized string representations of enum values during JSON serialization and deserialization.
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
internal sealed class JsonStringEnumMemberNameAttribute : Attribute
{
    /// <summary>
    /// Creates new attribute instance with a specified enum member name.
    /// </summary>
    /// <param name="name">The name to apply to the current enum member when serialized to JSON.</param>
    public JsonStringEnumMemberNameAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the custom JSON name of the enum member.
    /// </summary>
    public string Name { get; }
}
#endif