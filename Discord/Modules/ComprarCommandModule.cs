using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using WorkerService1.Discord.Services; // Importa o nosso novo serviço

namespace WorkerService1.Discord.Modules
{
    [Group("comprar", "Comandos para comprar planos")]
    public class ComprarCommandModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly ILogger<ComprarCommandModule> _logger;
        private readonly StripeService _stripeService; // Injeta o StripeService

        public ComprarCommandModule(ILogger<ComprarCommandModule> logger, StripeService stripeService)
        {
            _logger = logger;
            _stripeService = stripeService;
        }

        [SlashCommand("plano", "Comprar um plano de assinatura")]
        public async Task ComprarPlano(
            [Summary("plano", "Nome do plano (premium, vip, pro)")] string nomePlano)
        {
            await DeferAsync(ephemeral: true);

            try
            {
                // 1. Chama o serviço para fazer a lógica de negócios
                var checkoutUrl = await _stripeService.CreateCheckoutSessionUrlAsync(nomePlano, Context.User.Id);

                // 2. Formata a resposta
                var embed = new EmbedBuilder()
                    .WithTitle($"🛒 Compra do Plano {nomePlano.ToUpper()}")
                    .WithDescription($"Clique no botão abaixo para finalizar sua compra:")
                    .AddField("Link de Pagamento", $"[Pagar Agora]({checkoutUrl})")
                    .WithColor(Color.Green)
                    .Build();
                
                // Cria um botão para o link, fica mais bonito!
                var component = new ComponentBuilder()
                    .WithButton("Finalizar Pagamento", style: ButtonStyle.Link, url: checkoutUrl)
                    .Build();

                await Context.User.SendMessageAsync(embed: embed, components: component);
                await FollowupAsync("✅ Link de pagamento enviado no seu privado!", ephemeral: true);
            }
            catch (ArgumentException ex) // Captura o erro de plano não encontrado
            {
                await FollowupAsync($"❌ {ex.Message} Planos disponíveis: premium, vip, pro", ephemeral: true);
            }
            catch (Exception ex) // Captura qualquer outro erro (incluindo do Stripe)
            {
                _logger.LogError(ex, "Erro ao processar o comando /comprar plano para {User}", Context.User.Username);
                await FollowupAsync("❌ Ocorreu um erro ao gerar seu link de pagamento. Por favor, tente novamente mais tarde.", ephemeral: true);
            }
        }
    }
}