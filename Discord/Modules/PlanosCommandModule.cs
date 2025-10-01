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
            // Buscar todos os produtos ativos com preços expandidos
            var productService = new Stripe.ProductService();
            var products = await productService.ListAsync(new Stripe.ProductListOptions
            {
                Active = true,
                Limit = 100,
                Expand = new List<string> { "data.default_price" }
            });

            if (!products.Data.Any())
            {
                await FollowupAsync("❌ Nenhum plano disponível no momento.", ephemeral: true);
                return;
            }

            // Enviar mensagem inicial
            var embedInicial = new EmbedBuilder()
                .WithTitle("📋 Planos Disponíveis")
                .WithDescription($"**{products.Data.Count}** planos ativos encontrados. Enviando lista completa...")
                .WithColor(Color.Blue)
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await FollowupAsync(embed: embedInicial, ephemeral: true);

            // Processar todos os produtos em lotes de 5
            var todosProdutos = products.Data.ToList();
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
                    
                    // Usar preço já expandido (otimização)
                    string priceText = "Preço não definido";
                    
                    if (product.DefaultPrice != null)
                    {
                        var defaultPrice = product.DefaultPrice;
                        var unitAmount = defaultPrice.UnitAmount ?? 0;
                        var currency = defaultPrice.Currency?.ToUpper() ?? "USD";
                        var amountFormatted = unitAmount > 0 ? $"R$ {unitAmount / 100.0:F2}" : "Gratuito";
                        
                        var interval = defaultPrice.Recurring?.Interval ?? "único";
                        var intervalCount = defaultPrice.Recurring?.IntervalCount ?? 1;
                        var intervalText = intervalCount > 1 ? $"a cada {intervalCount} {interval}s" : $"por {interval}";
                        
                        priceText = $"{amountFormatted} {currency} / {intervalText}";
                    }

                    // Verificar se existe no PlanMapping
                    var planMappingKey = await GetPlanMappingKeyAsync(product.DefaultPriceId);
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
                embed.WithFooter($"Total: {products.Data.Count} planos ativos");

                // Enviar como follow-up
                await FollowupAsync(embed: embed.Build(), ephemeral: true);

                // Pequena pausa entre mensagens para evitar rate limit
                if (i < lotes.Count - 1)
                {
                    await Task.Delay(500);
                }
            }

            _logger.LogInformation($"Comando /planos executado por {Context.User.Username} (ID: {Context.User.Id}) - {products.Data.Count} planos listados");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar planos");
            await FollowupAsync("❌ Erro interno ao buscar planos. Tente novamente mais tarde.", ephemeral: true);
        }
    }

    private async Task<string> GetPlanMappingKeyAsync(string priceId)
    {
        if (string.IsNullOrEmpty(priceId))
            return string.Empty;

        // Busca na tabela PlanMappings do banco de dados
        var mapping = await _dbContext.PlanMappings
            .FirstOrDefaultAsync(m => m.StripePriceId == priceId);
    
        // Retorna o 'Slug' (o nome amigável para o comando /comprar) ou uma string vazia
        return mapping?.Slug ?? string.Empty;
    }
}
}
