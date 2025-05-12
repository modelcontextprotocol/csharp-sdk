namespace AspNetCoreClient.Controllers
{
    public class ResponseToUser
    {
        public string Text { get; init; } = "";
        public Guid ConversationId { get; init; }
    }
}