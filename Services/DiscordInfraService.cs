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


            if (string.IsNullOrEmpty(mestreName) || string.IsNullOrEmpty(aventuraName) ||
                string.IsNullOrEmpty(mesaChannelName))
            {
                _logger.LogWarning(
                    "Produto {ProductName} não tem os metadados necessários (mestre, aventura, mesa) para criar a infraestrutura.",
                    product.Name);
                return;
            }

            var guild = _client.GetGuild(ulong.Parse(_config["Discord:GuildId"]));
            if (guild == null) return;

            // 2. Lógica do CARGO (Mestre)

            var restGuild = await _client.Rest.GetGuildAsync(guild.Id);
            var allRoles = restGuild.Roles;
            var mestreRole =
                allRoles.FirstOrDefault(r => r.Name.Equals(mestreName, System.StringComparison.OrdinalIgnoreCase));
            if (mestreRole == null)
            {
                _logger.LogInformation("Cargo '{MestreName}' não encontrado. Criando...", mestreName);

                // 1. Criamos o cargo e guardamos a resposta da API (um RestRole) em uma variável temporária.

                mestreRole = await guild.CreateRoleAsync(mestreName, isMentionable: true);

                _logger.LogInformation("Cargo '{MestreName}' criado com sucesso.", mestreName);
            }

            //2.1 Logica do CARGO(player)
            var playerRole = allRoles.FirstOrDefault(r =>
                r.Name.Equals(playerRoleName, System.StringComparison.OrdinalIgnoreCase));
            if (playerRole == null)
            {
                playerRole = await guild.CreateRoleAsync(playerRoleName, isMentionable: true);
            }

            // 3. Lógica da CATEGORIA (Aventura)
            var allChannels = await restGuild.GetChannelsAsync();
            
            var aventuraCategory = allChannels.OfType<ICategoryChannel>().FirstOrDefault(c =>
                c.Name.Equals(aventuraName, StringComparison.OrdinalIgnoreCase));

            if (aventuraCategory == null)
            {
                _logger.LogInformation("Categoria '{AventuraName}' não encontrada. Criando...", aventuraName);

                // 1. Cria a categoria e guarda a resposta da API (um RestCategoryChannel)
                var newRestCategory = await guild.CreateCategoryChannelAsync(aventuraName);

                // 2. Usa o ID da nova categoria para pegar o objeto 'Socket' completo do cache
                aventuraCategory = guild.GetCategoryChannel(newRestCategory.Id);

                // 3. AGORA, com o objeto do tipo correto, modificamos as permissões
                _logger.LogInformation("Configurando permissões da categoria '{AventuraName}'...", aventuraName);

                await aventuraCategory.AddPermissionOverwriteAsync(guild.EveryoneRole,
                    new OverwritePermissions(viewChannel: PermValue.Deny));
                await aventuraCategory.AddPermissionOverwriteAsync(mestreRole,
                    new OverwritePermissions(viewChannel: PermValue.Allow));
                await aventuraCategory.AddPermissionOverwriteAsync(_client.CurrentUser,
                    new OverwritePermissions(viewChannel: PermValue.Allow));

                _logger.LogInformation("Permissões da categoria '{AventuraName}' configuradas para privada.",
                    aventuraName);
            }

            // 4. Lógica do CANAL (Mesa)
            var allChannels2 = await restGuild.GetChannelsAsync();
            var mesaChannel = allChannels2.OfType<ITextChannel>().FirstOrDefault(c =>
                c.Name.Equals(mesaChannelName, StringComparison.OrdinalIgnoreCase) && 
                c.CategoryId == aventuraCategory.Id);

            if (mesaChannel == null)
            {
                _logger.LogInformation(
                    "Canal '{MesaChannelName}' não encontrado. Criando dentro da categoria '{AventuraName}'...",
                    mesaChannelName, aventuraCategory.Name);
                var canalTexto = await guild.CreateTextChannelAsync(mesaChannelName,
                    props => props.CategoryId = aventuraCategory.Id);
                var canalVoz = await guild.CreateVoiceChannelAsync(mesaChannelName,
                    props => props.CategoryId = aventuraCategory.Id);

                await canalTexto.AddPermissionOverwriteAsync(playerRole,
                    new OverwritePermissions(viewChannel: PermValue.Allow));
                await canalVoz.AddPermissionOverwriteAsync(playerRole,
                    new OverwritePermissions(viewChannel: PermValue.Allow));
                await canalTexto.AddPermissionOverwriteAsync(mestreRole,
                    new OverwritePermissions(viewChannel: PermValue.Allow));
                await canalVoz.AddPermissionOverwriteAsync(mestreRole,
                    new OverwritePermissions(viewChannel: PermValue.Allow));
            }
            else
            {
                _logger.LogInformation("Canal '{MesaChannelName}' já existe na categoria correta.", mesaChannelName);
            }

            // 5. Mapeamento
            // Buscar preços do produto em vez de usar DefaultPriceId
            var prices = await GetProductPricesAsync(product.Id);
            if (!prices.Any())
            {
                _logger.LogError(
                    "Produto {ProductName} não tem preços ativos definidos no Stripe. Mapeamento não será criado.",
                    product.Name);
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
                _logger.LogInformation("Mapeamento automático adicionado: Price ID {PriceId} -> Role ID {RoleId}",
                    priceId, playerRole.Id);
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
                _logger.LogInformation("Mapeamento de Plano adicionado: Slug '{Slug}' -> Price ID {PriceId}", slug,
                    priceId);
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
            _logger.LogWarning("INICIANDO PROCESSO DE DEPROVISIONAMENTO para o produto: {ProductName}", product.Name);
            _logger.LogWarning("AVISO: Esta é uma ação destrutiva e irreversível.");

            try
            {
                // 1. Extrair os metadados do produto deletado para saber o que apagar
                product.Metadata.TryGetValue("aventura", out var aventuraName);
                product.Metadata.TryGetValue("mesa", out var mesaChannelName);
                product.Metadata.TryGetValue("cargo",
                    out var playerRoleName); // Usamos o mesmo nome do cargo que foi criado

                if (string.IsNullOrEmpty(aventuraName) || string.IsNullOrEmpty(mesaChannelName) ||
                    string.IsNullOrEmpty(playerRoleName))
                {
                    _logger.LogError(
                        "Não foi possível executar o deprovisionamento. Metadados essenciais (aventura, mesa, player_role) não encontrados no produto deletado {ProductId}.",
                        product.Id);
                    return;
                }

                var guild = _client.GetGuild(ulong.Parse(_config["Discord:GuildId"]));
                if (guild == null)
                {
                    _logger.LogError("Servidor não encontrado, deprovisionamento cancelado.");
                    return;
                }

                // 2. Encontrar os itens do Discord que serão deletados
                var aventuraCategory = guild.CategoryChannels.FirstOrDefault(c =>
                    c.Name.Equals(aventuraName, StringComparison.OrdinalIgnoreCase));
                var playerRole = guild.Roles.FirstOrDefault(r =>
                    r.Name.Equals(playerRoleName, StringComparison.OrdinalIgnoreCase));

                // Encontra o canal de texto E o de voz (se existirem)
                var mesaChannelTexto = guild.TextChannels.FirstOrDefault(c =>
                    c.Name.Equals(mesaChannelName, StringComparison.OrdinalIgnoreCase) &&
                    c.CategoryId == aventuraCategory?.Id);
                var mesaChannelVoz = guild.VoiceChannels.FirstOrDefault(c =>
                    c.Name.Equals(mesaChannelName, StringComparison.OrdinalIgnoreCase) &&
                    c.CategoryId == aventuraCategory?.Id);

                // 3. Deletar os canais da mesa (se existirem)
                if (mesaChannelTexto != null)
                {
                    _logger.LogInformation("Deletando canal de texto: #{MesaChannelName}", mesaChannelTexto.Name);
                    await mesaChannelTexto.DeleteAsync();
                }

                if (mesaChannelVoz != null)
                {
                    _logger.LogInformation("Deletando canal de voz: {MesaChannelName}", mesaChannelVoz.Name);
                    await mesaChannelVoz.DeleteAsync();
                }

                // 4. Deletar o cargo do assinante (se existir)
                if (playerRole != null)
                {
                    _logger.LogInformation("Deletando cargo de jogador: {PlayerRoleName}", playerRole.Name);
                    await playerRole.DeleteAsync();
                }

                // 5. Verificar se a categoria ficou vazia e, se sim, deletá-la
                if (aventuraCategory != null)
                {
                    _logger.LogInformation("Verificando se a categoria '{AventuraName}' ficou vazia...", aventuraCategory.Name);

                    // Adicionamos uma pequena pausa para dar tempo ao cache do Discord de ser atualizado
                    // após a deleção do canal no passo anterior.
                    await Task.Delay(1000); // 1 segundo de espera

                    // Buscamos a versão mais atualizada da categoria a partir do cache do servidor.
                    var updatedCategory = guild.GetCategoryChannel(aventuraCategory.Id);

                    // Verificamos a propriedade 'Channels' da própria categoria.
                    // O '.Any()' checa se a lista de canais dentro dela tem algum item.
                    if (updatedCategory != null && !updatedCategory.Channels.Any())
                    {
                        _logger.LogInformation("A categoria '{AventuraName}' está vazia. Deletando...", updatedCategory.Name);
                        await updatedCategory.DeleteAsync();
                    }
                    else
                    {
                        _logger.LogInformation("A categoria '{AventuraName}' ainda contém outros canais e não será deletada.", aventuraCategory.Name);
                    }
                }

                // 6. Remover os mapeamentos do banco de dados
                var priceId = product.DefaultPriceId; // Ou buscar todos os preços associados, se houver mais de um
                if (!string.IsNullOrEmpty(priceId))
                {
                    var roleMapping = await _dbContext.PlanRoleMappings.FindAsync(priceId);
                    if (roleMapping != null)
                    {
                        _dbContext.PlanRoleMappings.Remove(roleMapping);
                        _logger.LogInformation("Mapeamento de Cargo (Price ID {PriceId}) removido do DB.", priceId);
                    }

                    var slug = CreateSlug(product.Name);
                    var planMapping = await _dbContext.PlanMappings.FindAsync(slug);
                    if (planMapping != null)
                    {
                        _dbContext.PlanMappings.Remove(planMapping);
                        _logger.LogInformation("Mapeamento de Plano (Slug '{Slug}') removido do DB.", slug);
                    }

                    await _dbContext.SaveChangesAsync();
                }

                _logger.LogWarning("PROCESSO DE DEPROVISIONAMENTO CONCLUÍDO para o produto: {ProductName}",
                    product.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Ocorreu um erro durante o deprovisionamento da infraestrutura do produto {ProductId}", product.Id);
            }
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