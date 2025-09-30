using Discord.Interactions;
using Discord.WebSocket;
using System.Reflection;


namespace WorkerService1.Discord.Handlers
{
    public class CommandHandler
    {
        private readonly ILogger<CommandHandler> _logger;
        private readonly InteractionService _commands;
        private readonly DiscordSocketClient _client;
        private readonly IServiceProvider _services;
        private readonly IConfiguration _config;

        public CommandHandler(ILogger<CommandHandler> logger, InteractionService commands, DiscordSocketClient client, IServiceProvider services, IConfiguration config)
        {
            _logger = logger;
            _commands = commands;
            _client = client;
            _services = services;
            _config = config;
        }

        public async Task InitializeAsync()
        {
            // Adiciona todos os módulos de comando do nosso projeto
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

            // Escuta por interações (comandos de barra)
            _client.InteractionCreated += HandleInteractionAsync;

            // Escuta quando o bot estiver pronto para registrar os comandos
            _client.Ready += OnClientReady;
        }

        private async Task OnClientReady()
        {
            // Registra os comandos para um servidor específico (guild).
            // É muito mais rápido para testar do que registrar globalmente.
            var guildIdStr = _config["Discord:GuildId"];
            if (ulong.TryParse(guildIdStr, out ulong guildId))
            {
                await _commands.RegisterCommandsToGuildAsync(guildId, true);
                _logger.LogInformation($"Comandos registrados para o servidor ID: {guildId}");
            }
            else
            {
                _logger.LogWarning("Discord:GuildId não encontrado ou inválido no appsettings.json. Comandos não serão registrados.");
            }
        }

        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            try
            {
                var context = new SocketInteractionContext(_client, interaction);
                await _commands.ExecuteCommandAsync(context, _services);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar uma interação.");
                // Se a interação ainda não foi respondida, informa ao usuário que algo deu errado.
                if (interaction.HasResponded is false)
                {
                    await interaction.RespondAsync("Ocorreu um erro ao executar este comando.", ephemeral: true);
                }
            }
        }
        
    }
}