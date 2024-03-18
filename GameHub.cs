using Microsoft.AspNetCore.SignalR;

// ============================================================================================
// Class: Player
// ============================================================================================
    
public class Player
{
    private const int STEP = 1;
    private const int FINISH = 100;
    public string Id { get; set; }
    public string Icon { get; set; }
    public string Name { get; set; }
    public int Count { get; set; } = 0;
    public bool IsWin => Count >= FINISH;
    public Player(string id, string icon, string name) => (Id, Icon, Name) = (id, icon, name);
    public void Run() => Count += STEP;
}

// ============================================================================================
// Class: Game
// ============================================================================================

public class Game
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public Player? PlayerA { get; set; } = null;
    public Player? PlayerB { get; set; } = null;
    public bool IsWaiting { get; set; } = false;
    public bool IsEmpty => PlayerA == null && PlayerB == null;
    public bool IsFull => PlayerA != null && PlayerB != null;

    public string? AddPlayer(Player player)
    {
        if (PlayerA == null)
        {
            PlayerA = player;
            IsWaiting = true;
            return "A";
        }
        else if (PlayerB == null)
        {
            PlayerB = player;
            IsWaiting = false;
            return "B";
        }
        return null;
    }
}

// ============================================================================================
// Class: GameHub 🐱🐶
// ============================================================================================

public class GameHub : Hub
{
    // ----------------------------------------------------------------------------------------
    // General
    // ----------------------------------------------------------------------------------------

    private static List<Game> games = new()
    {
        // new() { PlayerA = new("1", "🐱", "Cat"), IsWaiting = true },
        // new() { PlayerA = new("2", "🐶", "Dog"), IsWaiting = true },
    };

    // ----------------------------------------------------------------------------------------
    // Functions
    // ----------------------------------------------------------------------------------------

    public string Create()
    {
        var game = new Game();
        games.Add(game);
        return game.Id;
    }

    public async Task Run(string letter)
    {
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();

        var game = games.Find(g => g.Id == gameId);
        if (game == null)
        {
            await Clients.Caller.SendAsync("Reject");
            return;
        }

        var player = letter == "A" ? game.PlayerA : game.PlayerB;
        if (player == null) return;

        player.Run();
        await Clients.Group(gameId).SendAsync("Move", letter, player.Count);

        if (player.IsWin)
        {
            await Clients.Group(gameId).SendAsync("Win", letter);
        }
    }

    // ----------------------------------------------------------------------------------------
    // Connected
    // ----------------------------------------------------------------------------------------

    public override async Task OnConnectedAsync()
    {
        string page = Context.GetHttpContext()!.Request.Query["page"].ToString();

        switch (page)
        {
            case "list": await ListConnected(); break;
            case "game": await GameConnected(); break;
        }
        
        await base.OnConnectedAsync();
    }

    private async Task ListConnected()
    {
        await Clients.Caller.SendAsync("UpdateList", games.FindAll(g => g.IsWaiting));
    }

    private async Task GameConnected()
    {
        string id = Context.ConnectionId;
        string icon = Context.GetHttpContext()!.Request.Query["icon"].ToString();
        string name = Context.GetHttpContext()!.Request.Query["name"].ToString();
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();

        var game = games.Find(g => g.Id == gameId);
        if (game == null || game.IsFull)
        {
            await Clients.Caller.SendAsync("Reject");
            return;
        }

        var player = new Player(id, icon, name);
        var letter = game.AddPlayer(player);

        await Groups.AddToGroupAsync(id, gameId);
        await Clients.Group(gameId).SendAsync("Ready", letter, game);
        await Clients.All.SendAsync("UpdateList", games.FindAll(g => g.IsWaiting));

        if (game.IsFull)
        {
            await Clients.Group(gameId).SendAsync("Start");
        }
    }

    // ----------------------------------------------------------------------------------------
    // Disconnected
    // ----------------------------------------------------------------------------------------

    public override async Task OnDisconnectedAsync(Exception? exception) 
    {
        string page = Context.GetHttpContext()!.Request.Query["page"].ToString();

        switch (page)
        {
            case "list": await ListDisconnected(); break;
            case "game": await GameDisconnected(); break;
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task ListDisconnected()
    {
        await Task.CompletedTask;
    }

    private async Task GameDisconnected()
    {
        string id = Context.ConnectionId;
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();
        
        var game = games.Find(g => g.Id == gameId);
        if (game == null)
        {
            return;
        }

        if (game.PlayerA?.Id == id)
        {
            game.PlayerA = null;
            await Clients.Group(gameId).SendAsync("Left", "A");
        }
        else if (game.PlayerB?.Id == id)
        {
            game.PlayerB = null;
            await Clients.Group(gameId).SendAsync("Left", "B");
        }

        if (game.IsEmpty)
        {
            games.Remove(game);
            await Clients.All.SendAsync("UpdateList", games.FindAll(g => g.IsWaiting));
        }
    }

    // End of GameHub -------------------------------------------------------------------------
}