// Demonstrates serving Agent Skills over MCP with the Skills extension (SEP-2640) from a Streamable HTTP server.
//
// Each skill is a directory under ./Skills containing a SKILL.md and, optionally, supporting files.
// WithSkillsFromDirectory reads every skill directory, parses the YAML frontmatter of each SKILL.md, computes the
// SHA-256 digest and size of every file, and registers both the skills' entries (what skills/list and skills/get
// return) and the resources that serve their files (what resources/read returns).

using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);

// Every immediate subdirectory of ./Skills that contains a SKILL.md becomes a skill. The frontmatter is read from
// each SKILL.md, and the skill's URI is skill://{name}/SKILL.md. Use McpServerSkill.CreateFromDirectory or
// McpServerSkill.Create with WithSkills for finer control, for example an organizational URI prefix per skill.
string skillsRoot = Path.Combine(AppContext.BaseDirectory, "Skills");

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
    .WithSkillsFromDirectory(skillsRoot, configure: options =>
    {
        // Every caller sees the same catalog, so the listing may be shared by intermediaries for a while.
        options.TimeToLive = TimeSpan.FromMinutes(5);
        options.CacheScope = CacheScope.Public;
    });

var app = builder.Build();

app.MapMcp();

app.Run();
