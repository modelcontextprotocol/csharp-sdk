// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

// NOTE:
// This is a temporary workaround for lack of System.Text.Json's JsonStringEnumConverter<T>
// 9.x support for JsonStringEnumMemberNameAttribute. Once all builds use the System.Text.Json 9.x
// version, this whole file can be removed.

namespace System.Text.Json.Serialization;

internal sealed class CustomizableJsonStringEnumConverter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TEnum> :
    JsonStringEnumConverter<TEnum> where TEnum : struct, Enum
{
#if !NET9_0_OR_GREATER
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
/// Determines the string value that should be used when serializing an enum member.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
internal sealed class JsonStringEnumMemberNameAttribute : Attribute
{
    /// <summary>
    /// Creates new attribute instance with a specified enum member name.
    /// </summary>
    /// <param name="name">The name to apply to the current enum member.</param>
    public JsonStringEnumMemberNameAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the name of the enum member.
    /// </summary>
    public string Name { get; }
}
#endif