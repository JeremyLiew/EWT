using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.ObjectPool;

public enum Rank
{
    Two = 2, Three, Four, Five, Six, Seven, Eight, Nine, Ten, Jack, Queen, King, Ace
}

public class Card
{
    public Rank Rank { get; set; }

    public Card(Rank rank)
    {
        Rank = rank;
    }

    public override string ToString()
    {
        return Rank.ToString();
    }
}

// ============================================================================================
// Class: Player
// ============================================================================================
    
public class Player
{
    // private const int STEP = 1;
    // private const int FINISH = 100;
    public string Id { get; set; }
    public string Icon { get; set; }
    public string Name { get; set; }
    public int Chips { get; set; }
    public Card Hand { get; set; }

    // public int Count { get; set; } = 0;
    // public bool IsWin => Count >= FINISH;
    public Player(string id, string icon, string name, int chips) => (Id, Icon, Name, Chips) = (id, icon, name, chips);
    // public void Run() => Count += STEP;

    public override string ToString()
    {
        return $"{Name} ({Chips} chips)";
    }
}

// ============================================================================================
// Class: Game
// ============================================================================================

public class Game
{
    public string Id { get; } = Guid.NewGuid().ToString();
    private const int MaxPlayers = 2;
    // public Player? PlayerA { get; set; } = null;
    // public Player? PlayerB { get; set; } = null;
    public List<Player> Players { get; set; }
    public bool IsWaiting => Players.Count < MaxPlayers;
    public bool IsEmpty => Players.Count == 0;
    public bool IsFull => Players.Count == MaxPlayers;

    public Game(){
        Players = new List<Player>();
    }

    public void AddPlayer(Player player)
    {
        if (IsFull)
        {
            throw new InvalidOperationException("The game is already full.");
        }
        Players.Add(player);
    }

    public void DealCards()
    {
        Random random = new Random();
        foreach (var player in Players)
        {
            player.Hand = new Card((Rank)random.Next(2, (int)Rank.Ace + 1)); // Deal a random card to each player
        }
    }

    public List<Player> DetermineWinner()
    {
        List<Player> winners = new List<Player>();
        int maxRank = int.MinValue;

        foreach (var player in Players)
        {
            if ((int)player.Hand.Rank > maxRank)
            {
                maxRank = (int)player.Hand.Rank;
                winners.Clear();
                winners.Add(player);
            }
            else if ((int)player.Hand.Rank == maxRank)
            {
                winners.Add(player);
            }
        }

        return winners;
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

        var player = new Player(id, icon, name , 1000);
        game.AddPlayer(player);

        await Groups.AddToGroupAsync(id, gameId);
        await Clients.Group(gameId).SendAsync("PlayerJoined", game,game.Players[0].Id == id?'A':'B');
        await Clients.All.SendAsync("UpdateList", games.FindAll(g => g.IsWaiting));

        if (game.IsFull)
        {
            game.DealCards();
            await Clients.Group(gameId).SendAsync("Start",game);
        }
    }


    public async Task PlaceBet(int betAmount ,bool endRound)
    {
        Console.WriteLine(endRound);
        string id = Context.ConnectionId;
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();

        var game = games.Find(g => g.Id == gameId);
        if (game == null)
        {
            await Clients.Caller.SendAsync("Reject");
            return;
        }

        var player = game.Players.FirstOrDefault(p => p.Id == id);
        if (player == null)
        {
            return;
        }

        if (betAmount > player.Chips)
        {
            // Handle insufficient funds
            return;
        }

        player.Chips -= betAmount;

        await Clients.Group(gameId).SendAsync("BetPlaced", game , player, player.Chips , betAmount);


        // var nextPlayer = game.Players.FirstOrDefault(p => p.Id != id) != null?game.Players.FirstOrDefault(p => p.Id != id):game.Players[0];
        if(!endRound){
            await Clients.Group(gameId).SendAsync("UpdateTurn");
        }
    }

    public async Task Check()
    {
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();
        await Clients.Group(gameId).SendAsync("UpdateTurn");
    }

    public async Task Fold(int potAmount)
    {
        string id = Context.ConnectionId;
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();

        var game = games.FirstOrDefault(g => g.Id == gameId);
        if (game == null)
        {
            return;
        }

        var currentPlayer = game.Players.FirstOrDefault(p => p.Id == id);
        if (currentPlayer == null)
        {
            return;
        }

        var opponent = game.Players.FirstOrDefault(p => p.Id != id);
        if (currentPlayer.Chips == 0)
        {
            // Opponent wins
            if (opponent != null)
            {
                await Clients.Group(gameId).SendAsync("Win", game.Players[0].Id == opponent.Id?'A':'B');
            }
        }
        else
        {
            opponent.Chips += potAmount;
            // Deal hand and play another round
            await Clients.Group(gameId).SendAsync("SplitPot", game,opponent, currentPlayer);
        }

    }

    public async Task Showdown()
    {
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();
        var game = games.FirstOrDefault(g => g.Id == gameId);
        if (game == null)
        {
            return;
        }

        await Clients.Group(gameId).SendAsync("Showdown");

    }

    public async Task DetermineWinner(int potAmount){
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();
        var game = games.FirstOrDefault(g => g.Id == gameId);
        if (game == null)
        {
            return;
        }
        var winners = game.DetermineWinner();
        if (winners.Count == 1)
        {
            // Single winner
            var winner = winners[0];
            winner.Chips += potAmount;
            await Clients.Group(gameId).SendAsync("SplitPot", game ,winner);
        }
        else
        {
            // Split pot among tied players
            int chipsPerWinner = potAmount / winners.Count;
            foreach (var winner in winners)
            {
                winner.Chips += chipsPerWinner;
                 await Clients.Group(gameId).SendAsync("SplitPot", game ,winner);
            }
        }
    }

    public async Task StartNewRound()
{
        string gameId = Context.GetHttpContext()!.Request.Query["gameId"].ToString();
        var game = games.FirstOrDefault(g => g.Id == gameId);
        if (game == null)
        {
            return;
        }

        // Check if any player has 0 chips
        var playersWithZeroChips = game.Players.Where(player => player.Chips <= 0).ToList();
        if (playersWithZeroChips.Any())
        {
            // End the game and declare the winner
            var remainingPlayers = game.Players.Except(playersWithZeroChips).ToList();
            var winner = remainingPlayers.Count == 1 ? remainingPlayers[0] : null;
            await Clients.Group(gameId).SendAsync("Win", game.Players[0].Id == winner.Id?'A':'B');
        }else{
             game.DealCards();
            await Clients.Group(gameId).SendAsync("Start", game);
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

        var disconnectedPlayer = game.Players.FirstOrDefault(p => p.Id == id);

        if (disconnectedPlayer != null)
        {
            game.Players.Remove(disconnectedPlayer);
            await Clients.Group(gameId).SendAsync("PlayerLeft", disconnectedPlayer);
        }

        if (game.IsEmpty)
        {
            games.Remove(game);
            await Clients.All.SendAsync("UpdateList", games.FindAll(g => g.IsWaiting));
        }
    }

    // End of GameHub -------------------------------------------------------------------------
}