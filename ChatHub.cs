using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    private static int count = 0;

    public async Task SendText(string name, string message)
    {
        await Clients.Caller.SendAsync("ReceiveText", name, message, "caller");
        await Clients.Others.SendAsync("ReceiveText", name, message, "others");
    }

    public async Task SendImage(string name, string url)
    {
        await Clients.Caller.SendAsync("ReceiveImage", name, url, "caller");
        await Clients.Others.SendAsync("ReceiveImage", name, url, "others");
    }

    public async Task SendYouTube(string name, string id)
    {
        await Clients.Caller.SendAsync("ReceiveYouTube", name, id, "caller");
        await Clients.Others.SendAsync("ReceiveYouTube", name, id, "others");
    }

    public override async Task OnConnectedAsync()
    {
        count++;
        string name = Context.GetHttpContext()!.Request.Query["name"].ToString();
        await Clients.All.SendAsync("UpdateStatus", count, $"<b>{name}</b> joined");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        count--;
        string name = Context.GetHttpContext()!.Request.Query["name"].ToString();
        await Clients.All.SendAsync("UpdateStatus", count, $"<b>{name}</b> left");
        await base.OnDisconnectedAsync(exception);
    }
}