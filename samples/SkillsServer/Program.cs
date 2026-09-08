// Demonstrates serving Agent Skills over MCP with the Skills extension (SEP-2640) from a Streamable HTTP server.
//
// Each skill is a directory under ./Skills containing a SKILL.md and, optionally, supporting files.
// McpServerSkill.CreateFromDirectory reads the files, computes the SHA-256 digest and size of each one, and
// produces both the skill's entry (what skills/list and skills/get return) and the resources that serve the files
// (what resources/read returns). WithSkills registers all of it.
//
// The frontmatter is supplied in code and must mirror the YAML frontmatter at the top of SKILL.md exactly. Hosts
// re-parse the fetched SKILL.md and compare it field by field against the entry, and refuse the skill on any
// discrepancy. The extension deliberately does not parse YAML.

using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);

string skillsRoot = Path.Combine(AppContext.BaseDirectory, "Skills");

var gitWorkflow = McpServerSkill.CreateFromDirectory(
    uri: "skill://git-workflow/SKILL.md",
    frontmatter: new JsonObject
    {
        ["name"] = "git-workflow",
        ["description"] = "Follow this team's Git conventions for branching, commit messages, and pull requests.",
        ["license"] = "MIT",
    },
    directoryPath: Path.Combine(skillsRoot, "git-workflow"));

// A nested skill path: the organizational prefix is "acme/billing" and the skill's name is "refunds".
var refunds = McpServerSkill.CreateFromDirectory(
    uri: "skill://acme/billing/refunds/SKILL.md",
    frontmatter: new JsonObject
    {
        ["name"] = "refunds",
        ["description"] = "Process customer refund requests per company policy.",
        ["metadata"] = new JsonObject { ["owner"] = "billing-team", ["version"] = "2.1.0" },
    },
    directoryPath: Path.Combine(skillsRoot, "refunds"));

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "SkillsServer", Version = "1.0.0" };

        // A server may point the agent at a skill directly from its instructions. A host confirms the URI with
        // skills/get and reads it with resources/read; no listing is required.
        options.ServerInstructions =
            "Before making commits in this repository, load the skill at skill://git-workflow/SKILL.md.";
    })
    .WithHttpTransport()
    .WithSkills([gitWorkflow, refunds], options =>
    {
        // Every caller sees the same catalog, so the listing may be shared by intermediaries for a while.
        options.TimeToLive = TimeSpan.FromMinutes(5);
        options.CacheScope = CacheScope.Public;
    });

var app = builder.Build();

app.MapMcp();

app.Run();
