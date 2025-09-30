using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkerService1.Discord.Services; // Importa o StripeService

namespace WorkerService1.Discord.Modules
{
    public class StripeAdminModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly ILogger<StripeAdminModule> _logger;
        private readonly StripeService _stripeService;
        // Você também precisará do IConfiguration se o GetPlanMappingName estiver no StripeService
        private readonly IConfiguration _configuration;


        public StripeAdminModule(ILogger<StripeAdminModule> logger, StripeService stripeService, IConfiguration configuration)
        {
            _logger = logger;
            _stripeService = stripeService;
            _configuration = configuration;
        }

        [SlashCommand("listar-planos", "Lista todos os planos ativos do Stripe em um canal.")]
        [RequireRole("GrandeMestre")] // Comando apenas para administradores
        public async Task ListActivePlans(
            [Summary("canal", "O canal onde a lista de planos será postada.")] ITextChannel canal)
        {
            await DeferAsync(ephemeral: true);

            try
            {
                // 1. Chama o serviço para buscar os planos
                var activePlans = await _stripeService.GetActivePlansAsync();

                // 2. Constrói o Embed usando a lógica que você enviou
                var embedBuilder = new EmbedBuilder()
                    .WithTitle("📊 Planos Ativos no Stripe")
                    .WithDescription("Esta é a lista de todos os planos de assinatura atualmente ativos.")
                    .WithColor(Color.Blue)
                    .WithCurrentTimestamp();

                if (activePlans.Any())
                {
                    var plansDescription = new StringBuilder();
                    plansDescription.AppendLine("📋 **Planos Ativos Atualmente:**\n");
                    foreach (var plan in activePlans.Take(20)) // Limite para não exceder o tamanho do embed
                    {
                        var unitAmount = plan.UnitAmount ?? 0;
                        var amountFormatted = unitAmount > 0 ? $"R$ {unitAmount / 100.0:F2}" : "Gratuito";
                        var displayName = !string.IsNullOrEmpty(GetPlanMappingName(plan.Id)) ? GetPlanMappingName(plan.Id) : (plan.Nickname ?? "Sem nome");
                        
                        plansDescription.AppendLine($"• **{displayName}** (`{plan.Id}`) - {amountFormatted}");
                    }
                    embedBuilder.AddField("Detalhes dos Planos", plansDescription.ToString());
                }
                else
                {
                    embedBuilder.AddField("Detalhes dos Planos", "Nenhum plano ativo encontrado no Stripe.");
                }

                // 3. Envia a mensagem pública no canal escolhido
                await canal.SendMessageAsync(embed: embedBuilder.Build());

                // 4. Envia a confirmação privada
                await FollowupAsync("Lista de planos ativos postada com sucesso!");
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Erro ao executar o comando /listar-planos.");
                await FollowupAsync("Ocorreu um erro ao buscar os planos do Stripe. Verifique os logs.");
            }
        }
        
        // Copiado do ProductNotificationService para ser usado aqui
        private string GetPlanMappingName(string priceId)
        {
            var planMappingSection = _configuration.GetSection("PlanMapping");
            return planMappingSection.GetChildren().FirstOrDefault(x => x.Value == priceId)?.Key ?? string.Empty;
        }
    }
}