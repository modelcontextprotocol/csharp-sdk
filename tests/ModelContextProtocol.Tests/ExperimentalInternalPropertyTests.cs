using System.Reflection;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Validates the internal property pattern used for experimental MCP properties.
/// </summary>
/// <remarks>
/// Experimental properties on stable protocol types must use an internal serialization
/// property to hide the experimental type from external source generators. The public
/// property is marked [Experimental][JsonIgnore] and delegates to an internal *Core
/// property marked [JsonInclude][JsonPropertyName].
/// </remarks>
public class ExperimentalInternalPropertyTests
{
    [Fact]
    public void ExperimentalProperties_MustBeHiddenFromSourceGenerator()
    {
        // [Experimental] properties on stable protocol types must use the internal property
        // pattern so the STJ source generator does not reference experimental types in
        // generated code (which would trigger MCPEXP001 for consumers).
        //
        // Required pattern:
        //   1. Mark the public property [Experimental][JsonIgnore]
        //   2. Add an internal *Core property with [JsonInclude][JsonPropertyName]
        //      that the public property delegates to
        //
        // To stabilize:
        //   1. Remove [Experimental] and [JsonIgnore] from the public property
        //   2. Add [JsonPropertyName] to the public property
        //   3. Convert to auto-property
        //   4. Remove the internal *Core property

        foreach (var (type, prop) in GetExperimentalPropertiesOnStableTypes())
        {
            Assert.True(
                prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null,
                $"{type.Name}.{prop.Name} is [Experimental] but missing [JsonIgnore].");

            Assert.True(
                prop.GetCustomAttribute<JsonPropertyNameAttribute>() is null,
                $"{type.Name}.{prop.Name} is [Experimental] and must not have [JsonPropertyName].");
        }
    }

    private static IEnumerable<(Type Type, PropertyInfo Property)> GetExperimentalPropertiesOnStableTypes()
    {
        var protocolTypes = typeof(Tool).Assembly.GetTypes()
            .Where(t => t.Namespace == "ModelContextProtocol.Protocol" && t.IsClass && !t.IsAbstract);

        foreach (var type in protocolTypes)
        {
            if (type.GetCustomAttributes().Any(a => a.GetType().Name == "ExperimentalAttribute"))
            {
                continue;
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetCustomAttributes().Any(a => a.GetType().Name == "ExperimentalAttribute"))
                {
                    yield return (type, prop);
                }
            }
        }
    }
}
