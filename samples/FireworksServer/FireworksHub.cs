using Microsoft.AspNetCore.SignalR;

namespace FireworksServer;

public sealed class FireworksHub(ShowState showState) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (showState.Latest is { } latest)
        {
            await Clients.Caller.SendAsync("ShowLaunched", latest, Context.ConnectionAborted);
        }

        await base.OnConnectedAsync();
    }
}
