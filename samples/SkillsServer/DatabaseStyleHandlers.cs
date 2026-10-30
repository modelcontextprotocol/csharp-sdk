using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Security.Claims;
using System.Text;

namespace SkillsServer;

// An illustrative application repository backed by in-memory rows, not a database implementation.
// In a real repository, push tenant/permission/cursor predicates and Take into the database query.
internal static class DatabaseStyleHandlers
{
    public static IMcpServerBuilder WithRepositoryHandlers(this IMcpServerBuilder builder)
    {
        builder.Services.AddScoped<ExampleSkillRepository>();
        return builder
            .WithSkills(options =>
            {
                options.ListSkillsHandler = (context, cancellationToken) =>
                    context.Services!.GetRequiredService<ExampleSkillRepository>()
                        .ListSkillsAsync(context.User, context.Params.Cursor, cancellationToken);
                options.GetSkillHandler = async (context, cancellationToken) => new GetSkillResult
                {
                    Skill = await context.Services!.GetRequiredService<ExampleSkillRepository>()
                        .FindSkillAsync(context.User, context.Params.Uri, cancellationToken) ??
                        throw new McpProtocolException("Skill is unavailable.", McpErrorCode.InvalidParams),
                };
            })
            .WithListResourcesHandler((context, cancellationToken) =>
                context.Services!.GetRequiredService<ExampleSkillRepository>()
                    .ListFilesAsync(context.User, context.Params.Cursor, cancellationToken))
            .WithReadResourceHandler(async (context, cancellationToken) => new ReadResourceResult
            {
                Contents =
                [
                    await context.Services!.GetRequiredService<ExampleSkillRepository>()
                        .ReadFileAsync(context.User, context.Params.Uri, cancellationToken) ??
                        throw new McpProtocolException("Resource is unavailable.", McpErrorCode.InvalidParams),
                ],
            });
    }
}

internal sealed class ExampleSkillRepository(ExampleSkillStore store)
{
    private const int PageSize = 1;
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    public ValueTask<ListSkillsResult> ListSkillsAsync(ClaimsPrincipal? user, string? cursor, CancellationToken cancellationToken)
    {
        var (rows, nextCursor) = QueryPage(user, cursor, cancellationToken);
        return new(new ListSkillsResult
        {
            Skills = rows.Select(row => row.Skill.ProtocolSkill).ToList(),
            NextCursor = nextCursor,
        });
    }

    public ValueTask<ListResourcesResult> ListFilesAsync(ClaimsPrincipal? user, string? cursor, CancellationToken cancellationToken)
    {
        var (rows, nextCursor) = QueryPage(user, cursor, cancellationToken);
        return new(new ListResourcesResult
        {
            Resources = rows.Select(row => new Resource
            {
                Uri = row.Skill.Id,
                Name = row.Skill.ProtocolSkill.Name!,
                MimeType = "text/markdown",
            }).ToList(),
            NextCursor = nextCursor,
        });
    }

    public ValueTask<Skill?> FindSkillAsync(ClaimsPrincipal? user, string uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(VisibleRows(user).SingleOrDefault(row => row.Skill.Id == uri)?.Skill.ProtocolSkill);
    }

    public ValueTask<ResourceContents?> ReadFileAsync(ClaimsPrincipal? user, string uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = VisibleRows(user).SingleOrDefault(row => row.Skill.Id == uri);
        return new(row is null ? null : new TextResourceContents
        {
            Uri = row.Skill.Id,
            MimeType = "text/markdown",
            Text = row.Markdown,
        });
    }

    private (ExampleSkillRow[] Rows, string? NextCursor) QueryPage(
        ClaimsPrincipal? user, string? cursor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? after = DecodeCursor(cursor);
        var rows = VisibleRows(user)
            .Where(row => row.Listed && (after is null || string.CompareOrdinal(row.Skill.Id, after) > 0))
            .OrderBy(row => row.Skill.Id, StringComparer.Ordinal)
            .Take(PageSize + 1)
            .ToArray();
        bool hasMore = rows.Length > PageSize;
        var page = hasMore ? rows.Take(PageSize).ToArray() : rows;
        return (page, hasMore ? Convert.ToBase64String(Encoding.UTF8.GetBytes(page[page.Length - 1].Skill.Id)) : null);
    }

    private IEnumerable<ExampleSkillRow> VisibleRows(ClaimsPrincipal? user)
    {
        string? tenant = user?.FindFirst("tenant")?.Value;
        if (user?.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(tenant))
        {
            throw new McpProtocolException("An authenticated tenant is required.", McpErrorCode.InvalidRequest);
        }
        return store.Rows.Where(row => row.Tenant == tenant && user.IsInRole(row.RequiredRole));
    }

    private static string? DecodeCursor(string? cursor)
    {
        if (cursor is null)
        {
            return null;
        }
        try
        {
            return s_strictUtf8.GetString(Convert.FromBase64String(cursor));
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            throw new McpProtocolException("Invalid cursor.", McpErrorCode.InvalidParams);
        }
    }
}

internal sealed class ExampleSkillStore(IReadOnlyList<ExampleSkillRow> rows)
{
    public IReadOnlyList<ExampleSkillRow> Rows { get; } = rows;
}

// One SKILL.md file per row keeps the example focused on request services and query semantics.
internal sealed record ExampleSkillRow(string Tenant, string RequiredRole, bool Listed, McpServerSkill Skill, string Markdown)
{
    public static ExampleSkillRow Create(string tenant, string name, string requiredRole = "reader", bool listed = true)
    {
        string markdown = $"---\nname: {name}\ndescription: Instructions for {tenant}\n---\n# {name}\n";
        var skill = McpServerSkill.Create($"skill://store/{tenant}/{name}/SKILL.md",
            [McpServerSkillFile.FromText("SKILL.md", markdown)]);
        return new(tenant, requiredRole, listed, skill, markdown);
    }
}
