using Discord.WebSocket;

namespace WorkerService1.Discord.Modules;

public class WelcomeModule
{
    private readonly ILogger<WelcomeModule> _logger;
    private readonly IConfiguration _config;

    public WelcomeModule(ILogger<WelcomeModule> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task SendWelcomeMessageAsync(SocketGuildUser member)
    {
        // NOVO LOG: Informa que o módulo foi acionado
        _logger.LogInformation($"[MÓDULO WELCOME] Acionado para o usuário '{member.Username}'.");
        
        var channelIdStr = _config["Discord:BemVindoID"];
        // NOVO LOG: Mostra o ID que está sendo lido do arquivo de configuração
        _logger.LogInformation($"[MÓDULO WELCOME] Tentando usar o ID de canal: '{channelIdStr}'.");

        if (!ulong.TryParse(channelIdStr, out ulong channelId))
        {
            _logger.LogError($"[ERRO] O ID do canal de boas-vindas ('{channelIdStr}') no appsettings.json é inválido.");
            return;
        }

        var channel = member.Guild.GetTextChannel(channelId);
        
        if (channel != null)
        {
            // NOVO LOG: Confirma que o canal foi encontrado
            _logger.LogInformation($"[MÓDULO WELCOME] Canal '{channel.Name}' encontrado com sucesso. Enviando mensagem...");
            try
            {
                await channel.SendMessageAsync($"👋 Seja bem-vindo(a), {member.Mention}! 🎉");
                // NOVO LOG: Confirma o envio da mensagem
                _logger.LogInformation($"[SUCESSO] Mensagem de boas-vindas enviada para '{member.Username}'.");
            }
            catch (Exception ex)
            {
                // NOVO LOG: Captura erros específicos de envio (ex: falta de permissão)
                _logger.LogError(ex, $"[ERRO] Falha ao enviar a mensagem de boas-vindas. Verifique as permissões do bot no canal '{channel.Name}'.");
            }
        }
        else
        {
            _logger.LogWarning($"[ERRO] Não foi possível encontrar o canal de texto com o ID: {channelId}");
        }
    }
}