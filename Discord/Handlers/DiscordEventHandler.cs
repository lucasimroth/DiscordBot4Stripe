using Discord;
using Discord.WebSocket;
using WorkerService1.Discord.Modules;

namespace WorkerService1.Discord.Handlers;

public class DiscordEventHandler
{
    private readonly DiscordSocketClient _client;
    private readonly ILogger<DiscordEventHandler> _logger;
    private readonly WelcomeModule _welcomeModule;

    public DiscordEventHandler(DiscordSocketClient client, ILogger<DiscordEventHandler> logger, WelcomeModule welcomeModule)
    {
        _client = client;
        _logger = logger;
        _welcomeModule = welcomeModule;
    }

    public Task InitializeAsync()
    {
        _client.Ready += OnReadyAsync;
        _client.UserJoined += OnUserJoinedAsync; // Evento principal
        _client.Log += OnLog;
        return Task.CompletedTask;
    }
    
    // ... (métodos OnLog e OnReadyAsync continuam os mesmos) ...
    private Task OnLog(LogMessage msg)
    {
        _logger.LogInformation(msg.ToString());
        return Task.CompletedTask;
    }

    private Task OnReadyAsync()
    {
        _logger.LogInformation("BOT IS ONLINE and ready!");
        return Task.CompletedTask;
    }
    
    private async Task OnUserJoinedAsync(SocketGuildUser member)
    {
        // NOVO LOG: Informa que o evento foi recebido
        _logger.LogInformation($"[EVENTO RECEBIDO] Usuário '{member.Username}' (ID: {member.Id}) entrou no servidor '{member.Guild.Name}'.");
        
        // Chama o módulo para executar a ação
        await _welcomeModule.SendWelcomeMessageAsync(member);
    }
}