// Services/DiscordBotService.cs

using Discord;
using Discord.WebSocket;
using WorkerService1.Discord.Handlers; // Importa a pasta Handlers

namespace WorkerService1.Discord.Services;

public class DiscordBotService : BackgroundService
{
    private readonly ILogger<DiscordBotService> _logger;
    private readonly DiscordSocketClient _client;
    private readonly DiscordEventHandler _eventHandler; // Injeta o nosso handler
    private readonly CommandHandler _commandHandler;
    private readonly IConfiguration _config;
    

    public DiscordBotService(
        ILogger<DiscordBotService> logger, 
        DiscordSocketClient client,
        DiscordEventHandler eventHandler,
        CommandHandler commandHandler,
        IConfiguration config)
    {
        _logger = logger;
        _client = client;
        _eventHandler = eventHandler;
        _config  = config;
        _commandHandler = commandHandler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Inicializa o handler que vai "ouvir" os eventos
        await _eventHandler.InitializeAsync();
        await _commandHandler.InitializeAsync();
        
        var token = _config["Discord:DISCORD_TOKEN"];
        
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        _logger.LogInformation("Discord Bot Service is running.");

        // Mantém o serviço rodando em background
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}