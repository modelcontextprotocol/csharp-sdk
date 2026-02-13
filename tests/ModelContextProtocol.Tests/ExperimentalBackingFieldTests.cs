using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Tests that enforce the experimental backing field pattern for MCP protocol types.
/// </summary>
/// <remarks>
/// <para>
/// Experimental properties on serialized protocol types use an "object backing field" pattern to
/// prevent MCPEXP001 diagnostics from surfacing in consumer-generated code. The pattern is:
/// </para>
/// <list type="bullet">
///   <item>The typed property has <c>[Experimental]</c> and <c>[JsonIgnore]</c>.</item>
///   <item>A <c>public object?</c> backing field handles serialization with <c>[JsonInclude]</c>,
///         <c>[JsonPropertyName]</c>, <c>[JsonConverter(typeof(ExperimentalJsonConverter&lt;T&gt;))]</c>,
///         and <c>[EditorBrowsable(Never)]</c>.</item>
/// </list>
/// <para>
/// <strong>Stabilization lifecycle:</strong> When an experimental API becomes stable, do NOT
/// remove the backing field immediately. Follow these steps across multiple releases:
/// </para>
/// <list type="number">
///   <item>
///     <strong>Stabilize:</strong> Remove <c>[Experimental]</c> and <c>[JsonIgnore]</c> from the
///     typed property. Add <c>[JsonPropertyName]</c> to it. On the backing field: remove
///     <c>[JsonInclude]</c>, <c>[JsonPropertyName]</c>, <c>[JsonConverter]</c>; add
///     <c>[JsonIgnore]</c> and <c>[Obsolete]</c>. Change the registry entry from
///     <see cref="ExperimentalProperty"/> to <see cref="StabilizedProperty"/>.
///   </item>
///   <item>
///     <strong>Cleanup (after 2+ releases):</strong> Remove the backing field and the entry from
///     <see cref="ExperimentalPropertyRegistry"/>. Consumers who compiled against the
///     stabilized version will have generated code that no longer references the backing field.
///   </item>
/// </list>
/// </remarks>
public class ExperimentalBackingFieldTests
{
    /// <summary>
    /// Registry of experimental properties and their lifecycle state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every experimental property that participates in JSON serialization must be in this list.
    /// When stabilizing, change the entry from <see cref="ExperimentalProperty"/> to
    /// <see cref="StabilizedProperty"/> — do NOT remove it. Only remove after 2+ releases
    /// when consumers have recompiled.
    /// </para>
    /// </remarks>
    private static readonly ExperimentalPropertyEntry[] ExperimentalPropertyRegistry =
    [
        new ExperimentalProperty(typeof(Tool), nameof(Tool.Execution), "_execution"),
        new ExperimentalProperty(typeof(ServerCapabilities), nameof(ServerCapabilities.Tasks), "_tasks"),
        new ExperimentalProperty(typeof(ClientCapabilities), nameof(ClientCapabilities.Tasks), "_tasks"),
        new ExperimentalProperty(typeof(CallToolResult), nameof(CallToolResult.Task), "_task"),
        new ExperimentalProperty(typeof(CallToolRequestParams), nameof(CallToolRequestParams.Task), "_task"),
        new ExperimentalProperty(typeof(CreateMessageRequestParams), nameof(CreateMessageRequestParams.Task), "_task"),
        new ExperimentalProperty(typeof(ElicitRequestParams), nameof(ElicitRequestParams.Task), "_task"),
    ];

    /// <summary>
    /// Verifies that each registered property has the correct backing field pattern for its lifecycle state.
    /// </summary>
    [Fact]
    public void RegisteredProperties_FollowBackingFieldPattern()
    {
        foreach (var entry in ExperimentalPropertyRegistry)
        {
            if (entry is IgnoredExperimentalProperty)
            {
                continue;
            }

            var property = entry.Type.GetProperty(entry.PropertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.True(property is not null,
                $"{entry.Type.Name} should have a public property '{entry.PropertyName}'.");

            var fieldName = entry switch
            {
                ExperimentalProperty e => e.FieldName,
                StabilizedProperty s => s.FieldName,
                _ => throw new InvalidOperationException($"Unexpected entry type: {entry.GetType().Name}"),
            };

            var field = entry.Type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.True(field is not null,
                $"{entry.Type.Name} should have a public backing field '{fieldName}' for property '{entry.PropertyName}'.");

            // Field must be object?
            Assert.True(field!.FieldType == typeof(object),
                $"{entry.Type.Name}.{fieldName} should be of type 'object?' but is '{field.FieldType.Name}'.");

            // Field must have [EditorBrowsable(Never)]
            var editorBrowsable = field.GetCustomAttribute<EditorBrowsableAttribute>();
            Assert.True(editorBrowsable is not null && editorBrowsable.State == EditorBrowsableState.Never,
                $"{entry.Type.Name}.{fieldName} should have [EditorBrowsable(EditorBrowsableState.Never)].");

            switch (entry)
            {
                case ExperimentalProperty:
                    AssertExperimentalPattern(entry.Type, property, field, fieldName);
                    break;
                case StabilizedProperty:
                    AssertStabilizedPattern(entry.Type, property, field, fieldName);
                    break;
            }
        }
    }

    /// <summary>
    /// Verifies that any experimental property participating in JSON serialization is registered
    /// in <see cref="ExperimentalPropertyRegistry"/>.
    /// </summary>
    /// <remarks>
    /// A property requires registration if it has <c>[Experimental]</c> and either
    /// <c>[JsonPropertyName]</c> or <c>[JsonIgnore]</c>. If such a property is missing from
    /// the registry, this test fails.
    /// </remarks>
    [Fact]
    public void ExperimentalJsonProperties_AreInRegistry()
    {
        var registeredSet = new HashSet<(Type, string)>(ExperimentalPropertyRegistry.Select(e => (e.Type, e.PropertyName)));
        var protocolAssembly = typeof(Tool).Assembly;
        var protocolTypes = protocolAssembly.GetTypes()
            .Where(t => t.Namespace == "ModelContextProtocol.Protocol" && t.IsClass);

        foreach (var type in protocolTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                bool hasExperimental = HasAttribute(property, "ExperimentalAttribute");
                bool hasJsonPropertyName = property.GetCustomAttribute<JsonPropertyNameAttribute>() is not null;
                bool hasJsonIgnore = property.GetCustomAttribute<JsonIgnoreAttribute>() is not null;

                if (hasExperimental && (hasJsonPropertyName || hasJsonIgnore))
                {
                    Assert.True(registeredSet.Contains((type, property.Name)),
                        $"{type.Name}.{property.Name} is an experimental JSON property but is not registered in " +
                        $"{nameof(ExperimentalPropertyRegistry)}. Add it to the registry using " +
                        $"{nameof(ExperimentalProperty)}, {nameof(StabilizedProperty)}, or " +
                        $"{nameof(IgnoredExperimentalProperty)}.");
                }
            }
        }
    }

    /// <summary>
    /// Verifies that the typed property getter reads from the backing field and the setter writes to it.
    /// </summary>
    [Fact]
    public void RegisteredProperties_GetAndSetBackingField()
    {
        foreach (var entry in ExperimentalPropertyRegistry)
        {
            if (entry is IgnoredExperimentalProperty)
            {
                continue;
            }

            var fieldName = entry switch
            {
                ExperimentalProperty e => e.FieldName,
                StabilizedProperty s => s.FieldName,
                _ => throw new InvalidOperationException($"Unexpected entry type: {entry.GetType().Name}"),
            };

            var property = entry.Type.GetProperty(entry.PropertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.True(property is not null,
                $"{entry.Type.Name} should have a public property '{entry.PropertyName}'.");

            var field = entry.Type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.True(field is not null,
                $"{entry.Type.Name} should have a public backing field '{fieldName}' for property '{entry.PropertyName}'.");

            var instance = Activator.CreateInstance(entry.Type)!;
            var testValue = Activator.CreateInstance(property!.PropertyType)!;

            // Setting the property should write to the backing field
            property.SetValue(instance, testValue);
            Assert.Same(testValue, field!.GetValue(instance));

            // Setting the backing field should be readable via the property getter
            var anotherValue = Activator.CreateInstance(property.PropertyType)!;
            field.SetValue(instance, anotherValue);
            Assert.Same(anotherValue, property.GetValue(instance));

            // Setting the property to null should null the backing field
            property.SetValue(instance, null);
            Assert.Null(field.GetValue(instance));
        }
    }

    private static void AssertExperimentalPattern(Type type, PropertyInfo property, FieldInfo field, string fieldName)
    {
        // Property must have [Experimental]
        Assert.True(HasAttribute(property, "ExperimentalAttribute"),
            $"{type.Name}.{property.Name} is registered as {nameof(ExperimentalProperty)} but does not have " +
            $"[Experimental]. If this API has been stabilized, change its registry entry to {nameof(StabilizedProperty)}.");

        // Property must have [JsonIgnore]
        Assert.True(property.GetCustomAttribute<JsonIgnoreAttribute>() is not null,
            $"{type.Name}.{property.Name} is registered as Experimental and should have [JsonIgnore].");

        // Field must have [JsonInclude]
        Assert.True(field.GetCustomAttribute<JsonIncludeAttribute>() is not null,
            $"{type.Name}.{fieldName} is registered as Experimental and should have [JsonInclude].");

        // Field must have [JsonPropertyName]
        Assert.True(field.GetCustomAttribute<JsonPropertyNameAttribute>() is not null,
            $"{type.Name}.{fieldName} is registered as Experimental and should have [JsonPropertyName].");

        // Field must have [JsonConverter]
        Assert.True(field.GetCustomAttribute<JsonConverterAttribute>() is not null,
            $"{type.Name}.{fieldName} is registered as Experimental and should have [JsonConverter].");
    }

    private static void AssertStabilizedPattern(Type type, PropertyInfo property, FieldInfo field, string fieldName)
    {
        // Property must NOT have [JsonIgnore] (it's now the primary serialization target)
        Assert.True(property.GetCustomAttribute<JsonIgnoreAttribute>() is null,
            $"{type.Name}.{property.Name} is registered as Stabilized and should NOT have [JsonIgnore].");

        // Property must have [JsonPropertyName]
        Assert.True(property.GetCustomAttribute<JsonPropertyNameAttribute>() is not null,
            $"{type.Name}.{property.Name} is registered as Stabilized and should have [JsonPropertyName].");

        // Field must have [JsonIgnore]
        Assert.True(field.GetCustomAttribute<JsonIgnoreAttribute>() is not null,
            $"{type.Name}.{fieldName} is registered as Stabilized and should have [JsonIgnore].");

        // Field must have [Obsolete]
        Assert.True(HasAttribute(field, "ObsoleteAttribute"),
            $"{type.Name}.{fieldName} is registered as Stabilized and should have [Obsolete].");
    }

    /// <summary>
    /// Checks for an attribute by name to avoid CS0436 conflicts with polyfill types on net472.
    /// </summary>
    private static bool HasAttribute(MemberInfo member, string attributeName) =>
        member.CustomAttributes.Any(a => a.AttributeType.Name == attributeName);

    /// <summary>Base type for entries in the experimental property registry.</summary>
    private abstract record ExperimentalPropertyEntry(Type Type, string PropertyName);

    /// <summary>
    /// An experimental property with a backing field that handles serialization.
    /// The property has <c>[Experimental]</c> + <c>[JsonIgnore]</c>. The backing field has
    /// <c>[JsonInclude]</c>, <c>[JsonPropertyName]</c>, <c>[JsonConverter]</c>, and <c>[EditorBrowsable(Never)]</c>.
    /// </summary>
    private record ExperimentalProperty(Type Type, string PropertyName, string FieldName)
        : ExperimentalPropertyEntry(Type, PropertyName);

    /// <summary>
    /// A recently-stabilized property whose backing field must remain for binary compatibility.
    /// The property has <c>[JsonPropertyName]</c> (no <c>[Experimental]</c>, no <c>[JsonIgnore]</c>).
    /// The backing field has <c>[JsonIgnore]</c>, <c>[Obsolete]</c>, and <c>[EditorBrowsable(Never)]</c>.
    /// </summary>
    private record StabilizedProperty(Type Type, string PropertyName, string FieldName)
        : ExperimentalPropertyEntry(Type, PropertyName);

    /// <summary>
    /// An experimental property that is excluded from backing field validation. It has <c>[Experimental]</c>
    /// and <c>[JsonIgnore]</c> but does not require a backing field. The <paramref name="Reason"/> must
    /// justify why this property is excluded.
    /// </summary>
    private record IgnoredExperimentalProperty(Type Type, string PropertyName, string Reason)
        : ExperimentalPropertyEntry(Type, PropertyName);
}
