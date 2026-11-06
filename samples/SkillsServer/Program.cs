// Demonstrates serving Agent Skills over MCP with the Skills extension (SEP-2640) from a Streamable HTTP server.
//
// Each skill is an immediate child directory of ./Skills containing a SKILL.md and, optionally, supporting files.
// WithSkillsFromSubdirectories discovers skills in those immediate child directories, not deeper directories.
// Selected files within each skill are collected recursively; a nested SKILL.md is not a separate registration.
// The method parses the YAML frontmatter of each skill's root SKILL.md, computes the
// SHA-256 digest and size of every file, and registers both the skills' entries (what skills/list and skills/get
// return) and the resources that serve their files (what resources/read returns).

using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);

// Every immediate subdirectory of ./Skills that contains a SKILL.md becomes a skill. The frontmatter is read from
// each SKILL.md, and the skill's URI is skill://{name}/SKILL.md. Use McpServerSkillPackage.CreateFromDirectory or
// McpServerSkillPackage.Create with WithSkillPackages for finer control, for example an organizational URI per skill.
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
    .WithSkillsFromSubdirectories(skillsRoot);

var app = builder.Build();

app.MapMcp();

app.Run();
