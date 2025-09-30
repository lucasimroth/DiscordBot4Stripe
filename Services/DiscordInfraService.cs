using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;

namespace WorkerService1.Services
{
    public class DiscordInfraService
    {
        private readonly ILogger<DiscordInfraService> _logger;
        private readonly IConfiguration _config;
        private readonly DiscordSocketClient _client;

        public DiscordInfraService(ILogger<DiscordInfraService> logger, IConfiguration config, DiscordSocketClient client)
        {
            _logger = logger;
            _config = config;
            _client = client;
        }

        public async Task ProvisionProductInfrastructureAsync(Stripe.Product product)
        {
            // 1. Extrair os metadados
            product.Metadata.TryGetValue("mestre", out var mestreName);
            product.Metadata.TryGetValue("aventura", out var aventuraName);
            product.Metadata.TryGetValue("mesa", out var mesaChannelName);

            if (string.IsNullOrEmpty(mestreName) || string.IsNullOrEmpty(aventuraName) || string.IsNullOrEmpty(mesaChannelName))
            {
                _logger.LogWarning("Produto {ProductName} não tem os metadados necessários (mestre, aventura, mesa) para criar a infraestrutura.", product.Name);
                return;
            }

            var guild = _client.GetGuild(ulong.Parse(_config["Discord:GuildId"]));
            if (guild == null) return;
            
            // 2. Lógica do CARGO (Mestre)
            var mestreRole = guild.Roles.FirstOrDefault(r => r.Name.Equals(mestreName, System.StringComparison.OrdinalIgnoreCase));
            if (mestreRole == null)
            {
                _logger.LogInformation("Cargo '{MestreName}' não encontrado. Criando...", mestreName);
    
                // 1. Criamos o cargo e guardamos a resposta da API (um RestRole) em uma variável temporária.
                var newRestRole = await guild.CreateRoleAsync(mestreName, isMentionable: true);
    
                // 2. Agora, usamos o ID do novo cargo para pegar o objeto SocketRole completo do cache do servidor.
                mestreRole = guild.GetRole(newRestRole.Id);

                _logger.LogInformation("Cargo '{MestreName}' criado com sucesso.", mestreName);
            }

            // 3. Lógica da CATEGORIA (Aventura)
            var aventuraCategory = guild.CategoryChannels.FirstOrDefault(c => c.Name.Equals(aventuraName, System.StringComparison.OrdinalIgnoreCase));
            if (aventuraCategory == null)
            {
                _logger.LogInformation("Categoria '{AventuraName}' não encontrada. Criando...", aventuraName);
    
                // 1. Cria a categoria e guarda a resposta da API (um RestCategoryChannel)
                var newRestCategory = await guild.CreateCategoryChannelAsync(aventuraName);

                // 2. Usa o ID da nova categoria para pegar o objeto 'Socket' completo do cache
                aventuraCategory = guild.GetCategoryChannel(newRestCategory.Id);

                // 3. AGORA, com o objeto do tipo correto, modificamos as permissões
                _logger.LogInformation("Configurando permissões da categoria '{AventuraName}'...", aventuraName);

                await aventuraCategory.AddPermissionOverwriteAsync(guild.EveryoneRole, new OverwritePermissions(viewChannel: PermValue.Deny));
                await aventuraCategory.AddPermissionOverwriteAsync(mestreRole, new OverwritePermissions(viewChannel: PermValue.Allow));
                await aventuraCategory.AddPermissionOverwriteAsync(_client.CurrentUser, new OverwritePermissions(viewChannel: PermValue.Allow));
    
                _logger.LogInformation("Permissões da categoria '{AventuraName}' configuradas para privada.", aventuraName);
            }

            // 4. Lógica do CANAL (Mesa)
            var mesaChannel = guild.TextChannels.FirstOrDefault(c => c.Name.Equals(mesaChannelName, System.StringComparison.OrdinalIgnoreCase));
            if (mesaChannel == null)
            {
                _logger.LogInformation("Canal '{MesaChannelName}' não encontrado. Criando dentro da categoria '{AventuraName}'...", mesaChannelName, aventuraCategory.Name);
                await guild.CreateTextChannelAsync(mesaChannelName, props => props.CategoryId = aventuraCategory.Id);
                await guild.CreateVoiceChannelAsync(mesaChannelName, props => props.CategoryId = aventuraCategory.Id);
            }
        }
        
        public async Task DeprovisionProductInfrastructureAsync(Stripe.Product product)
        {
            // AVISO: Esta é uma ação destrutiva! Use com cuidado.
            // ... (Lógica para buscar e deletar o canal, a categoria e talvez o cargo)
             _logger.LogWarning("A lógica de deprovisionamento (deleção de canais/cargos) não foi implementada.");
        }
    }
}