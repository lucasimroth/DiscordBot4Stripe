using Discord;
using Discord.Interactions;
using WorkerService1.Data;
using Stripe;
using Stripe.Checkout;
using Microsoft.EntityFrameworkCore;

namespace WorkerService1.Discord.Modules
{
    public class ComprarCommandModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ComprarCommandModule> _logger;
        private readonly SubscriptionDbContext _dbContext;

        public ComprarCommandModule(IConfiguration configuration, ILogger<ComprarCommandModule> logger,
            SubscriptionDbContext dbContext)
        {
            _configuration = configuration;
            _logger = logger;
            _dbContext = dbContext;

        }

        [SlashCommand("comprar", "Comprar um plano específico")]
        public async Task ComprarPlanoEspecifico(string produto_id)
        {
            await DeferAsync(ephemeral: true);

            // Validação de entrada
            if (string.IsNullOrWhiteSpace(produto_id))
            {
                await FollowupAsync("❌ Por favor, forneça um ID de produto válido.", ephemeral: true);
                return;
            }

            try
            {
                // Verificar se é uma chave mapeada no PlanMapping
                var mapping = await _dbContext.PlanMappings
                    .FirstOrDefaultAsync(m => m.Slug == produto_id.ToLower());

                if (mapping == null)
                {
                    await FollowupAsync(
                        "❌ Plano não encontrado! Use o comando `/planos listar` para ver os planos disponíveis.",
                        ephemeral: true);
                    return;
                }

                var priceId = mapping.StripePriceId;
                _logger.LogInformation(
                    $"Usando mapeamento do banco: {produto_id} -> {priceId} (Produto: {mapping.ProductName})");

                // Buscar o preço e produto usando o mapeamento do banco
                var priceService = new Stripe.PriceService();
                var price = await priceService.GetAsync(priceId);

                if (price == null || !price.Active)
                {
                    await FollowupAsync("❌ Preço não encontrado ou inativo no Stripe.", ephemeral: true);
                    return;
                }

                // Buscar o produto a partir do preço
                var productService = new Stripe.ProductService();
                var product = await productService.GetAsync(price.ProductId);

                if (product == null || !product.Active)
                {
                    await FollowupAsync("❌ Produto não encontrado ou inativo no Stripe.", ephemeral: true);
                    return;
                }

                // Criar sessão de checkout
                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = new List<SessionLineItemOptions>
                    {
                        new SessionLineItemOptions
                        {
                            Price = price.Id,
                            Quantity = 1,
                        },
                    },
                    Mode = "subscription",
                    SuccessUrl = "https://discord.com/channels/@me",
                    CancelUrl = "https://discord.com/channels/@me",
                    Metadata = new Dictionary<string, string>
                    {
                        { "discord_user_id", Context.User.Id.ToString() }
                    }
                };

                var sessionService = new SessionService();
                var session = await sessionService.CreateAsync(options);

                // Criar embed simples e direto
                var unitAmount = price.UnitAmount ?? 0;
                var currency = price.Currency?.ToUpper() ?? "USD";
                var amountFormatted = unitAmount > 0 ? $"R$ {unitAmount / 100.0:F2}" : "Gratuito";

                var interval = price.Recurring?.Interval ?? "único";
                var intervalCount = price.Recurring?.IntervalCount ?? 1;
                var intervalText = intervalCount > 1 ? $"a cada {intervalCount} {interval}s" : $"por {interval}";

                // Determinar emoji baseado no preço
                var priceValue = unitAmount;
                var planEmoji = priceValue == 0 ? "🆓" :
                    priceValue < 2000 ? "⭐" :
                    priceValue < 5000 ? "💎" :
                    "👑";

                var embed = new EmbedBuilder()
                    .WithTitle($"{planEmoji} {product.Name}")
                    .WithDescription($"**Preço:** {amountFormatted} {currency} / {intervalText}\n\n" +
                                     $"**Descrição:** {(!string.IsNullOrEmpty(product.Description) ? product.Description : "Plano premium com recursos exclusivos!")}\n\n" +
                                     $"**Link de pagamento:**\n{session.Url}")
                    .WithColor(Color.Green)
                    .WithFooter("Pagamento processado pelo Stripe - 100% seguro!")
                    .WithTimestamp(DateTimeOffset.Now);

                // Adicionar imagem se existir
                if (product.Images != null && product.Images.Any())
                {
                    embed.WithThumbnailUrl(product.Images.First());
                }

                // Enviar no privado do usuário
                try
                {
                    var dmChannel = await Context.User.CreateDMChannelAsync();
                    await dmChannel.SendMessageAsync(embed: embed.Build());

                    // Confirmar que enviou no privado
                    await FollowupAsync("✅ Link de pagamento enviado no seu privado! Verifique suas mensagens diretas.",
                        ephemeral: true);

                    _logger.LogInformation(
                        $"Link de compra enviado no DM para {Context.User.Username} (ID: {Context.User.Id}) - Produto: {product.Name}");
                }
                catch (Exception dmEx)
                {
                    _logger.LogWarning(dmEx,
                        $"Não foi possível enviar DM para {Context.User.Username}. Enviando resposta ephemeral.");

                    // Se não conseguir enviar DM, enviar resposta ephemeral
                    await FollowupAsync(embed: embed.Build(), ephemeral: true);
                    await FollowupAsync(
                        "⚠️ Não consegui enviar mensagem no privado. Certifique-se de que suas DMs estão abertas.",
                        ephemeral: true);
                }
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Erro ao criar sessão de checkout do Stripe");
                await FollowupAsync("❌ Erro ao processar pagamento. Tente novamente mais tarde.", ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar compra do plano");
                await FollowupAsync("❌ Erro interno. Tente novamente mais tarde.", ephemeral: true);
            }
        }
        // ... (dentro da classe ComprarCommandModule)

        [SlashCommand("comprar-mesa", "Gera um link de pagamento para a mesa deste canal.")]
        public async Task ComprarMesaAtual()
        {
            await DeferAsync(ephemeral: true);

            var channelId = Context.Channel.Id;

            try
            {
                // 1. Encontrar o mapeamento do canal no banco de dados
                var mapping = await _dbContext.ChannelProductMappings.FindAsync(channelId);

                if (mapping == null)
                {
                    await FollowupAsync("❌ Este comando só pode ser usado em um canal de assinatura de mesa.",
                        ephemeral: true);
                    return;
                }

                var priceId = mapping.StripePriceId;
                _logger.LogInformation(
                    $"[Compra por Canal] Usuário {Context.User.Username} iniciando compra no canal {Context.Channel.Name} (Price ID: {priceId})");

                // 2. A partir daqui, a lógica é a MESMA do seu outro comando de compra!
                // (Buscamos preço, produto, criamos a sessão, o embed e enviamos por DM)
                var priceService = new Stripe.PriceService();
                var price = await priceService.GetAsync(priceId);

                if (price == null || !price.Active)
                {
                    await FollowupAsync("❌ O produto associado a este canal não está mais ativo.", ephemeral: true);
                    return;
                }

                var productService = new Stripe.ProductService();
                var product = await productService.GetAsync(price.ProductId);

                if (product == null || !product.Active)
                {
                    await FollowupAsync("❌ O produto associado a este canal não está mais ativo.", ephemeral: true);
                    return;
                }

                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = new List<SessionLineItemOptions> { new() { Price = price.Id, Quantity = 1 } },
                    Mode = "subscription",
                    SuccessUrl = "https://discord.com/channels/@me",
                    CancelUrl = "https://discord.com/channels/@me",
                    Metadata = new Dictionary<string, string> { { "discord_user_id", Context.User.Id.ToString() } }
                };

                var sessionService = new SessionService();
                var session = await sessionService.CreateAsync(options);

                // Construir e enviar o Embed (pode-se refatorar para um método privado para não repetir código)
                var embed = new EmbedBuilder()
                    .WithTitle($"⭐ {product.Name}")
                    .WithDescription(
                        $"Clique no botão abaixo para ir para a página de pagamento segura e garantir sua vaga!\n\n**Link direto:**\n{session.Url}")
                    .WithColor(Color.Green)
                    .WithFooter("Pagamento processado pelo Stripe - 100% seguro!")
                    .WithTimestamp(DateTimeOffset.Now);

                if (product.Images != null && product.Images.Any())
                {
                    embed.WithThumbnailUrl(product.Images.First());
                }

                var dmChannel = await Context.User.CreateDMChannelAsync();
                await dmChannel.SendMessageAsync(embed: embed.Build());

                await FollowupAsync("✅ Link de pagamento enviado no seu privado! Verifique suas mensagens diretas.",
                    ephemeral: true);
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Erro do Stripe ao criar sessão de checkout via canal.");
                await FollowupAsync("❌ Erro ao processar o pagamento. Tente novamente mais tarde.", ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar compra via canal.");
                await FollowupAsync("❌ Erro interno. Tente novamente mais tarde.", ephemeral: true);
            }
        }
    }
}


