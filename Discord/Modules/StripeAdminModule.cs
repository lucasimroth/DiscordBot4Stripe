using Discord;
using Discord.Interactions;
using System.Text;
using WorkerService1.Discord.Services; // Importa o StripeService
using WorkerService1.Data;
using Microsoft.EntityFrameworkCore;

namespace WorkerService1.Discord.Modules
{
    public class StripeAdminModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly ILogger<StripeAdminModule> _logger;

        private readonly StripeService _stripeService;

        // Você também precisará do IConfiguration se o GetPlanMappingName estiver no StripeService
        private readonly IConfiguration _configuration;
        private readonly SubscriptionDbContext _dbContext;


        public StripeAdminModule(ILogger<StripeAdminModule> logger, StripeService stripeService,
            IConfiguration configuration, SubscriptionDbContext dbContext)
        {
            _logger = logger;
            _stripeService = stripeService;
            _configuration = configuration;
            _dbContext = dbContext;
        }

        [SlashCommand("cancelar-assinatura", "Cancela a assinatura ativa do usuário.")]
        public async Task CancelarAssinatura()
        {
            await DeferAsync(ephemeral: true);

            try
            {
                var discordUserId = Context.User.Id;
                
                // Buscar assinatura ativa do usuário no banco de dados
                var userSubscription = await _dbContext.UserSubscriptions
                    .FirstOrDefaultAsync(s => s.DiscordUserId == discordUserId);

                if (userSubscription == null)
                {
                    await FollowupAsync("❌ Você não possui nenhuma assinatura ativa para cancelar.", ephemeral: true);
                    return;
                }

                // Cancelar assinatura no Stripe
                var cancelado = await _stripeService.CancelSubscriptionAsync(userSubscription.StripeSubscriptionId);
                
                if (cancelado)
                {
                    await FollowupAsync("✅ Sua assinatura foi cancelada com sucesso! O cargo será removido automaticamente.", ephemeral: true);
                }
                else
                {
                    await FollowupAsync("❌ Erro ao cancelar a assinatura. Tente novamente ou entre em contato com o suporte.", ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao executar o comando /cancelar-assinatura.");
                await FollowupAsync("❌ Ocorreu um erro ao cancelar a assinatura. Verifique os logs.", ephemeral: true);
            }
        }

        [SlashCommand("listar-planos", "Lista todos os planos ativos do Stripe em um canal.")]
        [RequireRole("GrandeMestre")]
        public async Task ListActivePlans(
            [Summary("canal", "O canal onde a lista de planos será postada.")]
            ITextChannel canal)
        {
            await DeferAsync(ephemeral: true);

            try
            {
                var activePlans = await _stripeService.GetActivePlansAsync();

                var embedBuilder = new EmbedBuilder()
                    .WithTitle("📊 Planos Ativos no Stripe\n")
                    .WithColor(Color.Blue)
                    .WithCurrentTimestamp();

                if (activePlans.Any())
                {
                    var plansDescription = new StringBuilder();
                    var fieldCount = 1;

                    foreach (var plan in activePlans)
                    {
                        var unitAmount = plan.UnitAmount ?? 0;
                        var amountFormatted = unitAmount > 0 ? $"R$ {unitAmount / 100.0:F2}" : "Gratuito";
                        var displayName = plan.Product?.Name ?? plan.Nickname ?? "Sem nome";

                        // Monta a linha de texto para o plano atual
                        var planLine = $"• **{displayName}** - {amountFormatted}\n";
                        // --- LÓGICA DE DIVISÃO ---
                        // Se adicionar a próxima linha for estourar o limite...
                        if (plansDescription.Length + planLine.Length > 1024)
                        {
                            // ...primeiro, adiciona o que já temos como um campo...
                            embedBuilder.AddField($"📋 Planos Ativos (Parte {fieldCount})",
                                plansDescription.ToString());
                            // ...depois, limpa o construtor de texto...
                            plansDescription.Clear();
                            // ...e incrementa o contador.
                            fieldCount++;
                        }

                        // Adiciona a linha ao construtor de texto
                        plansDescription.Append(planLine);
                    }

                    // Adiciona o último bloco de texto que sobrou no StringBuilder
                    if (plansDescription.Length > 0)
                    {

                        embedBuilder.AddField("Planos: ", plansDescription.ToString());
                    }
                }
                else
                {
                    embedBuilder.AddField("Detalhes dos Planos", "Nenhum plano ativo encontrado no Stripe.");
                }

                await canal.SendMessageAsync(embed: embedBuilder.Build());
                await FollowupAsync("Lista de planos ativos postada com sucesso!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao executar o comando /listar-planos.");
                await FollowupAsync("Ocorreu um erro ao buscar os planos do Stripe. Verifique os logs.");
            }
        }
    }
}