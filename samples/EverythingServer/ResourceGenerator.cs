using ModelContextProtocol.Protocol;

namespace EverythingServer;

static class ResourceGenerator
{
    private static readonly List<Resource> _resources = Enumerable.Range(1, 100).Select(i =>
        {
            var uri = $"test://template/resource/{i}";
            if (i % 2 != 0)
            {
                return new Resource
                {
                    Uri = uri,
                    Name = $"Resource {i}",
                    MimeType = "text/plain",
                    Description = $"Resource {i}: This is a plaintext resource"
                };
            }
            else
            {
                var buffer = System.Text.Encoding.UTF8.GetBytes($"Resource {i}: This is a base64 blob");
                return new Resource
                {
                    Uri = uri,
                    Name = $"Resource {i}",
                    MimeType = "application/octet-stream",
                    Description = Convert.ToBase64String(buffer)
                };
            }
        }).ToList();

    public static IReadOnlyList<Resource> Resources => _resources;
}