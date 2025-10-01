using Discord;
using Discord.WebSocket;
using WorkerService1.Data;
using System.Text.RegularExpressions;
using Stripe;

namespace WorkerService1.Services
{
    public class DiscordInfraService
    {
        private readonly ILogger<DiscordInfraService> _logger;
        private readonly IConfiguration _config;
        private readonly DiscordSocketClient _client;
        private readonly SubscriptionDbContext _dbContext; 


        public DiscordInfraService(ILogger<DiscordInfraService> logger,
            IConfiguration config,
            DiscordSocketClient client,
            SubscriptionDbContext dbContext)
        {
            _logger = logger;
            _config = config;
            _client = client;
            _dbContext = dbContext;
        }

        public async Task ProvisionProductInfrastructureAsync(Stripe.Product product)
        {
            // 1. Extrair os metadados
            product.Metadata.TryGetValue("mestre", out var mestreName);
            product.Metadata.TryGetValue("aventura", out var aventuraName);
            product.Metadata.TryGetValue("mesa", out var mesaChannelName);
            product.Metadata.TryGetValue("cargo", out var playerRoleName);


            if (string.IsNullOrEmpty(mestreName) || string.IsNullOrEmpty(aventuraName) || string.IsNullOrEmpty(mesaChannelName))
            {
                _logger.LogWarning("Produto {ProductName} não tem os metadados necessários (mestre, aventura, mesa) para criar a infraestrutura.", product.Name);
                return;
            }

            var guild = _client.GetGuild(ulong.Parse(_config["Discord:GuildId"]));
            if (guild == null) return;
            
            // 2. Lógica do CARGO (Mestre)
            
            var restGuild = await _client.Rest.GetGuildAsync(guild.Id);
            var allRoles = restGuild.Roles;
            var mestreRole = allRoles.FirstOrDefault(r => r.Name.Equals(mestreName, System.StringComparison.OrdinalIgnoreCase));
            if (mestreRole == null)
            {
                _logger.LogInformation("Cargo '{MestreName}' não encontrado. Criando...", mestreName);
    
                // 1. Criamos o cargo e guardamos a resposta da API (um RestRole) em uma variável temporária.
    
                mestreRole = await guild.CreateRoleAsync(mestreName, isMentionable: true);

                _logger.LogInformation("Cargo '{MestreName}' criado com sucesso.", mestreName);
            }
            
            //2.1 Logica do CARGO(player)
            var playerRole = allRoles.FirstOrDefault(r => r.Name.Equals(playerRoleName, System.StringComparison.OrdinalIgnoreCase));
            if (playerRole == null)
            {
                playerRole = await guild.CreateRoleAsync(playerRoleName, isMentionable: true);
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
            var mesaChannel = guild.TextChannels.FirstOrDefault(c => 
                c.Name.Equals(mesaChannelName, System.StringComparison.OrdinalIgnoreCase) &&
                c.CategoryId == aventuraCategory.Id);
            if (mesaChannel == null)
            {
                _logger.LogInformation("Canal '{MesaChannelName}' não encontrado. Criando dentro da categoria '{AventuraName}'...", mesaChannelName, aventuraCategory.Name);
                var canalTexto = await guild.CreateTextChannelAsync(mesaChannelName, props => props.CategoryId = aventuraCategory.Id);
                var canalVoz = await guild.CreateVoiceChannelAsync(mesaChannelName, props => props.CategoryId = aventuraCategory.Id);
                
                await canalTexto.AddPermissionOverwriteAsync(playerRole, new OverwritePermissions(viewChannel: PermValue.Allow));
                await canalVoz.AddPermissionOverwriteAsync(playerRole, new OverwritePermissions(viewChannel: PermValue.Allow));
            }else
            {
                _logger.LogInformation("Canal '{MesaChannelName}' já existe na categoria correta.", mesaChannelName);
            }
            // 5. Mapeamento
            // Buscar preços do produto em vez de usar DefaultPriceId
            var prices = await GetProductPricesAsync(product.Id);
            if (!prices.Any())
            {
                _logger.LogError("Produto {ProductName} não tem preços ativos definidos no Stripe. Mapeamento não será criado.", product.Name);
                return;
            }

            // Usar o primeiro preço ativo encontrado (ou você pode implementar lógica para escolher o preço padrão)
            var price = prices.First();
            var priceId = price.Id;

            _logger.LogInformation("Usando preço {PriceId} (R$ {Amount:F2}) para o produto {ProductName}", 
                priceId, (price.UnitAmount ?? 0) / 100.0, product.Name);

            // Mapeamento price <-> cargo
            var existingMapping = await _dbContext.PlanRoleMappings.FindAsync(priceId);
            if (existingMapping == null)
            {
                var newMapping = new PlanRoleMapping
                {
                    StripePriceId = priceId,
                    DiscordRoleId = playerRole.Id // Usamos o ID do cargo dos jogadores
                };
                _dbContext.PlanRoleMappings.Add(newMapping);
                _logger.LogInformation("Mapeamento automático adicionado: Price ID {PriceId} -> Role ID {RoleId}", priceId, playerRole.Id);
            }
            else
            {
                _logger.LogInformation("Mapeamento para o Price ID {PriceId} já existe no DB.", priceId);
            }
            
            // Mapeamento do nome <-> price
            var slug = CreateSlug(product.Name);
            var existingPlanMapping = await _dbContext.PlanMappings.FindAsync(slug);
            if (existingPlanMapping == null)
            {
                _dbContext.PlanMappings.Add(new PlanMapping
                {
                    Slug = slug,
                    StripePriceId = priceId,
                    ProductName = product.Name
                });
                _logger.LogInformation("Mapeamento de Plano adicionado: Slug '{Slug}' -> Price ID {PriceId}", slug, priceId);
            }
            else
            {
                _logger.LogInformation("Mapeamento de Plano para o Slug '{Slug}' já existe no DB.", slug);
            }

            // Salvar todas as mudanças de uma vez só
            try
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Todos os mapeamentos salvos com sucesso no banco de dados.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao salvar mapeamentos no banco de dados. Verificando se já existem...");
                
                // Verificar novamente se os mapeamentos já existem (pode ter sido criado por outro processo)
                var finalMappingCheck = await _dbContext.PlanRoleMappings.FindAsync(priceId);
                var finalPlanCheck = await _dbContext.PlanMappings.FindAsync(slug);
                
                if (finalMappingCheck != null && finalPlanCheck != null)
                {
                    _logger.LogInformation("Mapeamentos já existem no banco de dados. Continuando...");
                }
                else
                {
                    _logger.LogError("Falha ao salvar mapeamentos. Erro persistente.");
                    throw; // Re-lança a exceção se não conseguir resolver
                }
            }
        }
        
        public async Task DeprovisionProductInfrastructureAsync(Stripe.Product product)
        {
            // AVISO: Esta é uma ação destrutiva! Use com cuidado.
            // ... (Lógica para buscar e deletar o canal, a categoria e talvez o cargo)
             _logger.LogWarning("A lógica de deprovisionamento (deleção de canais/cargos) não foi implementada.");
        }
        
        
        // Método auxiliar para criar um nome amigável (slug)
        private string CreateSlug(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var nowhitespace = Regex.Replace(input.ToLower(), @"\s+", "-");
            var slug = Regex.Replace(nowhitespace, @"[^a-z0-9_-]", "");
            return slug;
        }

        // Método para buscar preços de um produto específico
        private async Task<List<Price>> GetProductPricesAsync(string productId)
        {
            try
            {
                var options = new PriceListOptions
                {
                    Product = productId,
                    Active = true,
                    Limit = 100
                };
                
                var priceService = new PriceService();
                var prices = await priceService.ListAsync(options);
                
                _logger.LogInformation("Encontrados {Count} preços ativos para o produto {ProductId}", 
                    prices.Data.Count, productId);
                
                return prices.Data.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao buscar preços para o produto {ProductId}", productId);
                return new List<Price>();
            }
        }
    }
}