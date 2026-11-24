using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace ModelContextProtocol;

/// <summary>
/// Configures the McpServerOptions using additional services from DI.
/// </summary>
/// <param name="serverTools">The individually registered tools.</param>
/// <param name="serverPrompts">The individually registered prompts.</param>
/// <param name="serverResources">The individually registered resources.</param>
/// <remarks>
/// Options can be created many times from the same instance, for example once per request by a stateless HTTP server.
/// The registered primitives don't change for the lifetime of this instance, so they're collected once into template
/// collections, and each options instance gets an inexpensive, independently mutable copy of the relevant template.
/// </remarks>
internal sealed class McpServerOptionsSetup(
    IEnumerable<McpServerTool> serverTools,
    IEnumerable<McpServerPrompt> serverPrompts,
    IEnumerable<McpServerResource> serverResources) : IConfigureOptions<McpServerOptions>
{
    private Template<McpServerTool, McpServerPrimitiveCollection<McpServerTool>>? _toolTemplate;
    private Template<McpServerPrompt, McpServerPrimitiveCollection<McpServerPrompt>>? _promptTemplate;
    private Template<McpServerResource, McpServerResourceCollection>? _resourceTemplate;

    /// <summary>
    /// Configures the given McpServerOptions instance by setting server information
    /// and collecting registered server primitives.
    /// </summary>
    /// <param name="options">The options instance to be configured.</param>
    public void Configure(McpServerOptions options)
    {
        Throw.IfNull(options);

        // If the options already has a collection, add to it rather than replacing it. We want to maintain the identity
        // of an existing collection in case someone has provided their own derived type, wants change notifications, etc.
        // Otherwise, give the options its own copy of the registered primitives, if there are any.
        if (options.ToolCollection is { } toolCollection)
        {
            AddAll(toolCollection, serverTools);
        }
        else if (GetTemplate(ref _toolTemplate, serverTools, static () => []) is { IsEmpty: false } tools)
        {
            options.ToolCollection = new(tools);
        }

        if (options.PromptCollection is { } promptCollection)
        {
            AddAll(promptCollection, serverPrompts);
        }
        else if (GetTemplate(ref _promptTemplate, serverPrompts, static () => []) is { IsEmpty: false } prompts)
        {
            options.PromptCollection = new(prompts);
        }

        if (options.ResourceCollection is { } resourceCollection)
        {
            AddAll(resourceCollection, serverResources);
        }
        else if (GetTemplate(ref _resourceTemplate, serverResources, static () => []) is { IsEmpty: false } resources)
        {
            options.ResourceCollection = new(resources);
        }
    }

    private static void AddAll<T>(McpServerPrimitiveCollection<T> collection, IEnumerable<T> primitives)
        where T : IMcpServerPrimitive
    {
        foreach (var primitive in primitives)
        {
            collection.TryAdd(primitive);
        }
    }

    /// <summary>Gets the template collection for <paramref name="primitives"/>, building or rebuilding it if needed.</summary>
    private static TCollection GetTemplate<T, TCollection>(
        ref Template<T, TCollection>? field, IEnumerable<T> primitives, Func<TCollection> createCollection)
        where T : class, IMcpServerPrimitive
        where TCollection : McpServerPrimitiveCollection<T>
    {
        // Concurrent callers may each build a template; they're equivalent, so whichever is stored last wins.
        Template<T, TCollection>? template = Volatile.Read(ref field);
        if (template is null || !template.IsCurrent(primitives))
        {
            template = Template<T, TCollection>.Create(primitives, createCollection());
            Volatile.Write(ref field, template);
        }

        return template.Collection;
    }

    /// <summary>A collection of the registered primitives, together with what it was built from.</summary>
    private sealed class Template<T, TCollection>(T[] primitives, string[] ids, TCollection collection)
        where T : class, IMcpServerPrimitive
        where TCollection : McpServerPrimitiveCollection<T>
    {
        /// <summary>Gets the collection of registered primitives. It's never mutated, only copied.</summary>
        public TCollection Collection { get; } = collection;

        public static Template<T, TCollection> Create(IEnumerable<T> source, TCollection collection)
        {
            T[] primitives = source.ToArray();
            string[] ids = Array.ConvertAll(primitives, static p => p.Id);
            AddAll(collection, primitives);
            return new(primitives, ids, collection);
        }

        /// <summary>
        /// Determines whether the template still reflects <paramref name="source"/>: the same primitive instances in the
        /// same order, each with the name it had when the template was built. The name is checked because it's the
        /// primitive's key, and it can be backed by a mutable protocol property.
        /// </summary>
        public bool IsCurrent(IEnumerable<T> source)
        {
            if (source is T[] array)
            {
                if (array.Length != primitives.Length)
                {
                    return false;
                }

                for (int i = 0; i < array.Length; i++)
                {
                    if (!IsCurrent(i, array[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            int count = 0;
            foreach (T primitive in source)
            {
                if (count >= primitives.Length || !IsCurrent(count, primitive))
                {
                    return false;
                }

                count++;
            }

            return count == primitives.Length;
        }

        private bool IsCurrent(int index, T primitive) =>
            ReferenceEquals(primitive, primitives[index]) &&
            primitive.Id is var id &&
            (ReferenceEquals(id, ids[index]) || string.Equals(id, ids[index], StringComparison.Ordinal));
    }
}
