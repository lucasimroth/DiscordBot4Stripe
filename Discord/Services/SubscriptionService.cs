using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using WorkerService1.Data;
using Stripe;

namespace WorkerService1.Discord.Services
{
    public class SubscriptionService
    {
        private readonly ILogger<SubscriptionService> _logger;
        private readonly IConfiguration _configuration;
        private readonly SubscriptionDbContext _dbContext;
        private readonly DiscordSocketClient _client;

        public SubscriptionService(ILogger<SubscriptionService> logger, IConfiguration configuration, DiscordSocketClient client,  SubscriptionDbContext dbContext)
        {
            _logger = logger;
            _configuration = configuration;
            _client = client;
            _dbContext = dbContext;
        }

        public async Task HandleCheckoutSessionCompleted(Stripe.Checkout.Session session)
        {
            // Toda a sua lógica original de HandleCheckoutSessionCompleted está aqui
            _logger.LogInformation($"Processando checkout session: {session.Id}");
            
            session.Metadata.TryGetValue("discord_user_id", out var discordUserIdStr);
            if (!ulong.TryParse(discordUserIdStr, out var discordUserId))
            {
                _logger.LogWarning($"Não foi possível converter Discord User ID: {discordUserIdStr}");
                return;
            }

            var lineItemService = new Stripe.Checkout.SessionLineItemService();
            var lineItems = await lineItemService.ListAsync(session.Id);
            var priceId = lineItems.Data[0].Price.Id;
            var mapping = await _dbContext.PlanRoleMappings.FindAsync(priceId);
            if (mapping == null)
            {
                _logger.LogWarning($"Mapeamento de cargo não encontrado no banco de dados para o Price ID: {priceId}");
                return;
            }
            var roleId = mapping.DiscordRoleId;
            
            var guildIdStr = _configuration["Discord:GuildId"];
            if (!ulong.TryParse(guildIdStr, out var guildId))
            {
                _logger.LogError("Discord:GuildId não configurado ou inválido");
                return;
            }
            
            var guild = _client.GetGuild(guildId);
            if (guild == null)
            {
                _logger.LogError($"Servidor Discord não encontrado com ID: {guildId}.");
                return;
            }

            await guild.DownloadUsersAsync();
            var user = guild.GetUser(discordUserId);
            if (user == null)
            {
                _logger.LogWarning($"⚠️ Usuário ainda encontrado. ");
                return;
            }

            var role = guild.GetRole(roleId);
            if (role == null)
            {
                _logger.LogError($"Cargo não encontrado no servidor com ID: {roleId}");
                return;
            }
            _logger.LogInformation($"Tentando adicionar cargo '{role.Name}' ao usuário '{user.Username}'");


            await user.AddRoleAsync(role);
            _logger.LogInformation($"Cargo '{role.Name}' adicionado com sucesso ao usuário '{user.Username}'");
            try
            {
                var newSubscription = new UserSubscription
                {
                    StripeSubscriptionId = session.SubscriptionId,
                    DiscordUserId = discordUserId,
                    StripeCustomerId = session.CustomerId
                };
                _dbContext.UserSubscriptions.Add(newSubscription);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation($"Assinatura {session.SubscriptionId} salva no banco de dados.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao salvar assinatura {session.SubscriptionId} no banco de dados.");
            }
        }

        public async Task HandleSubscriptionDeleted(Subscription subscription)
        {
            // Toda a sua lógica original de HandleSubscriptionDeleted está aqui
            var userSubscription = await _dbContext.UserSubscriptions
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == subscription.Id);

            if (userSubscription == null)
            {
                _logger.LogWarning($"Assinatura {subscription.Id} não encontrada no banco de dados.");
                return;
            }
            
            // ... (resto da sua lógica para remover o cargo e a entrada no DB) ...
            var priceId = subscription.Items.Data[0].Price.Id;
            var mapping = await _dbContext.PlanRoleMappings.FindAsync(priceId);
            if (mapping == null)
            {
                _logger.LogWarning($"Mapeamento de cargo não encontrado no DB para o Price ID: {priceId} da assinatura cancelada.");
                // Mesmo sem o mapeamento, ainda removemos a entrada do userSubscription
            }
            var roleId = mapping?.DiscordRoleId ?? 0; // Pega o ID ou 0 se o mapping for nulo

            var guildIdStr = _configuration["Discord:GuildId"];
            if (!ulong.TryParse(guildIdStr, out var guildId)) return;

            var guild = _client.GetGuild(guildId);
            var user = guild?.GetUser(userSubscription.DiscordUserId);
            var role = guild?.GetRole(roleId);

            if (user != null && role != null)
            {
                await user.RemoveRoleAsync(role);
                _logger.LogInformation($"Cargo '{role.Name}' removido do usuário '{user.Username}' devido ao fim da assinatura.");
            }
            else
            {
                _logger.LogWarning($"Não foi possível remover o cargo do usuário {userSubscription.DiscordUserId} (cargo ou usuário não encontrado).");
            }
        
            try
            {
                _dbContext.UserSubscriptions.Remove(userSubscription);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation($"Assinatura {subscription.Id} removida do banco de dados.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao remover assinatura {subscription.Id} do banco de dados.");
            }
        }
    }
}