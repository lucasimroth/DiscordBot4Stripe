using Discord;
using Discord.Interactions;
using WorkerService1.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace WorkerService1.Discord.Modules
{
    [Group("planos", "Comandos para visualizar planos disponíveis")]
    public class PlanosCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlanosCommandModule> _logger;
    private readonly SubscriptionDbContext _dbContext;

    public PlanosCommandModule(IConfiguration configuration, ILogger<PlanosCommandModule> logger, SubscriptionDbContext dbContext)
    {
        _configuration = configuration;
        _logger = logger;
        _dbContext = dbContext;
    }

    [SlashCommand("listar", "Lista todos os planos disponíveis com informações detalhadas")]
    public async Task ListarPlanos()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            // Buscar produtos que estão mapeados no banco de dados
            var mappedProducts = await _dbContext.PlanMappings
                .ToListAsync();

            if (!mappedProducts.Any())
            {
                await FollowupAsync("❌ Nenhum plano mapeado disponível no momento.", ephemeral: true);
                return;
            }

            // Buscar informações dos produtos no Stripe
            var productService = new Stripe.ProductService();
            var priceService = new Stripe.PriceService();
            var products = new List<Stripe.Product>();
            var prices = new List<Stripe.Price>();

            foreach (var mapping in mappedProducts)
            {
                try
                {
                    // Buscar o preço
                    var price = await priceService.GetAsync(mapping.StripePriceId);
                    if (price != null && price.Active)
                    {
                        prices.Add(price);
                        
                        // Buscar o produto
                        var product = await productService.GetAsync(price.ProductId);
                        if (product != null && product.Active)
                        {
                            products.Add(product);
                        }
                    }
                }
                catch (StripeException ex)
                {
                    _logger.LogWarning(ex, "Erro ao buscar produto/preço para mapping {Slug}", mapping.Slug);
                }
            }

            if (!products.Any())
            {
                await FollowupAsync("❌ Nenhum plano ativo encontrado no Stripe.", ephemeral: true);
                return;
            }

            // Enviar mensagem inicial
            var embedInicial = new EmbedBuilder()
                .WithTitle("📋 Planos Disponíveis")
                .WithDescription($"**{products.Count}** planos ativos encontrados. Enviando lista completa...")
                .WithColor(Color.Blue)
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await FollowupAsync(embed: embedInicial, ephemeral: true);

            // Processar todos os produtos em lotes de 5
            var todosProdutos = products;
            var lotes = todosProdutos.Select((produto, index) => new { produto, index })
                                   .GroupBy(x => x.index / 5)
                                   .Select(g => g.Select(x => x.produto).ToList())
                                   .ToList();

            for (int i = 0; i < lotes.Count; i++)
            {
                var lote = lotes[i];
                var embed = new EmbedBuilder()
                    .WithTitle($"📋 Planos Disponíveis (Parte {i + 1}/{lotes.Count})")
                    .WithColor(Color.Blue)
                    .WithTimestamp(DateTimeOffset.Now);

                var planosLista = new System.Text.StringBuilder();

                foreach (var product in lote)
                {
                    var productName = product.Name ?? "Sem nome";
                    
                    // Buscar o preço correspondente
                    var productPrice = prices.FirstOrDefault(p => p.ProductId == product.Id);
                    string priceText = "Preço não definido";
                    string planMappingKey = string.Empty;
                    
                    if (productPrice != null)
                    {
                        var unitAmount = productPrice.UnitAmount ?? 0;
                        var currency = productPrice.Currency?.ToUpper() ?? "USD";
                        var amountFormatted = unitAmount > 0 ? $"R$ {unitAmount / 100.0:F2}" : "Gratuito";
                        
                        var interval = productPrice.Recurring?.Interval ?? "único";
                        var intervalCount = productPrice.Recurring?.IntervalCount ?? 1;
                        var intervalText = intervalCount > 1 ? $"a cada {intervalCount} {interval}s" : $"por {interval}";
                        
                        priceText = $"{amountFormatted} {currency} / {intervalText}";
                        
                        // Buscar o mapping key
                        var mapping = mappedProducts.FirstOrDefault(m => m.StripePriceId == productPrice.Id);
                        planMappingKey = mapping?.Slug ?? string.Empty;
                    }

                    var comandoCompra = !string.IsNullOrEmpty(planMappingKey) 
                        ? $"`/comprar {planMappingKey}`" 
                        : "❌ Não disponível para compra";

                    planosLista.AppendLine($"**{productName}**");
                    planosLista.AppendLine($"💰 {priceText}");
                    
                    if (!string.IsNullOrEmpty(product.Description))
                    {
                        var description = product.Description.Length > 50 
                            ? product.Description.Substring(0, 50) + "..." 
                            : product.Description;
                        planosLista.AppendLine($"📝 {description}");
                    }

                    // Adicionar metadados se existirem (limitado)
                    if (product.Metadata != null && product.Metadata.Any())
                    {
                        foreach (var metadata in product.Metadata.Take(2))
                        {
                            planosLista.AppendLine($"• {metadata.Key}: {metadata.Value}");
                        }
                    }

                    planosLista.AppendLine($"🛒 {comandoCompra}");
                    planosLista.AppendLine("");
                }

                // Garantir que não exceda o limite de 1024 caracteres
                var conteudoFinal = planosLista.ToString();
                if (conteudoFinal.Length > 1000)
                {
                    conteudoFinal = conteudoFinal.Substring(0, 1000) + "...";
                }

                embed.AddField("🛍️ Planos Disponíveis", conteudoFinal, false);
                embed.WithFooter($"Total: {products.Count} planos ativos");

                // Enviar como follow-up
                await FollowupAsync(embed: embed.Build(), ephemeral: true);

                // Pequena pausa entre mensagens para evitar rate limit
                if (i < lotes.Count - 1)
                {
                    await Task.Delay(500);
                }
            }

            _logger.LogInformation($"Comando /planos executado por {Context.User.Username} (ID: {Context.User.Id}) - {products.Count} planos listados");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar planos");
            await FollowupAsync("❌ Erro interno ao buscar planos. Tente novamente mais tarde.", ephemeral: true);
        }
    }

}
}
