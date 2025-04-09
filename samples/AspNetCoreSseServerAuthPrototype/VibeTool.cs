using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AspNetCoreSseServerAuthPrototype
{
	[McpServerToolType]
	public class VibeTool
	{
		private readonly ILogger<VibeTool> logger;

		public VibeTool(ILogger<VibeTool> logger)
		{
			this.logger = logger;
		}

		[McpServerTool, Description("Gets the vibe in the provided location.")]
		public string GetVibe(string location)
		{
			this.logger.LogInformation("Getting vibe in {location}.", location);
			return $"Curious vibes in {location}.";
		}
	}
}
