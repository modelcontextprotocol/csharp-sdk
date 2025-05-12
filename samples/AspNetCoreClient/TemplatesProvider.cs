namespace AspNetCoreClient
{
    public interface ITemplatesProvider
    {
        Task<string> GetSystemMessage(string name);
    }
    public class TemplatesProvider : ITemplatesProvider
    {
        public async Task <string> GetSystemMessage(string name)
        {
            var systemMessage = await File.ReadAllTextAsync($"Templates/systemMessage-{name}.txt");
            return systemMessage;   
        }
    }
}