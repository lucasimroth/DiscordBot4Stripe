using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkerService1.Discord.Services;

namespace WorkerService1.Discord.Services
{
    public class ProductNotificationService
    {
        private readonly ILogger<ProductNotificationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly DiscordSocketClient _client;


        public ProductNotificationService(ILogger<ProductNotificationService> logger, IConfiguration configuration, DiscordSocketClient client)
        {
            _logger = logger;
            _configuration = configuration;
            _client = client;
        }
        
        // Métodos de atalho para clareza no StripeWebhookService
        public Task HandleProductCreated(Stripe.Product product) => ProcessProductEvent(product, "created");
        public Task HandleProductDeleted(Stripe.Product product) => ProcessProductEvent(product, "deleted");
        public Task HandleProductUpdated(Stripe.Product product) => ProcessProductEvent(product, !product.Active ? "archived" : "reactivated");

        private async Task ProcessProductEvent(Stripe.Product product, string eventType)
        {
            _logger.LogInformation($"Processando evento de produto '{eventType}' para: {product.Name}");
            try
            {
                var activePlans = await GetActivePlans();
                await SendActivePlansToDiscord(activePlans, product, eventType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ocorreu uma exceção inesperada ao processar o evento do produto.");
            }
        }

        private async Task<List<Stripe.Price>> GetActivePlans()
        {
            _logger.LogInformation("Buscando planos ativos do Stripe...");
            var options = new Stripe.PriceListOptions { Active = true, Limit = 100 };
            var prices = await new Stripe.PriceService().ListAsync(options);
            _logger.LogInformation("{Count} planos ativos encontrados.", prices.Data.Count);
            return prices.Data.ToList();
        }

        // --- MÉTODO PREENCHIDO COM A LÓGICA E OS LOGS ---
        private async Task SendActivePlansToDiscord(List<Stripe.Price> activePlans, Stripe.Product product, string eventType)
        {
            _logger.LogInformation("Preparando para enviar mensagem de notificação para o Discord...");
            
            // --- VERIFICAÇÕES ---
            var channelIdStr = "1420206249508601857"; // Canal de Notificações, verifique se este ID está correto.
            if (!ulong.TryParse(channelIdStr, out var channelId))
            {
                _logger.LogError("ID do canal de notificação é inválido: {ChannelId}", channelIdStr);
                return;
            }

            var guildIdStr = _configuration["Discord:GuildId"];
            if (!ulong.TryParse(guildIdStr, out var guildId))
            {
                _logger.LogError("DiscordGuildId não configurado ou é inválido.");
                return;
            }
            _logger.LogInformation("Configurações de Guild ID ({guildId}) e Channel ID ({channelId}) lidas.", guildId, channelId);

            var guild = _client?.GetGuild(guildId);
            if (guild == null)
            {
                _logger.LogError("Não foi possível encontrar o Servidor (Guild) com o ID: {GuildId}. Verifique se o bot está no servidor.", guildId);
                return;
            }
            _logger.LogInformation("Servidor (Guild) '{GuildName}' encontrado.", guild.Name);

            var channel = guild.GetTextChannel(channelId);
            if (channel == null)
            {
                _logger.LogError("Não foi possível encontrar o Canal de Texto com o ID: {ChannelId} no servidor {GuildName}", channelId, guild.Name);
                return;
            }
            _logger.LogInformation("Canal de destino '#{ChannelName}' encontrado.", channel.Name);

            // --- LÓGICA DO EMBED ---
            var (title, color) = eventType switch
            {
                "created" => ("🆕 Novo Produto Criado no Stripe!", Color.Green),
                "deleted" => ("🗑️ Produto Deletado no Stripe!", Color.Red),
                "archived" => ("📦 Produto Arquivado no Stripe!", Color.Orange),
                "reactivated" => ("♻️ Produto Reativado no Stripe!", Color.Blue),
                _ => ("📝 Produto Atualizado no Stripe!", Color.Gold)
            };

            var embedBuilder = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription($"**Produto:** {product.Name}\n")
                .WithColor(color)
                .WithCurrentTimestamp()
                .WithFooter($"ID do Produto: {product.Id}");
            
            // 1. Crie um StringBuilder para montar a seção de detalhes.
            var details = new StringBuilder();
            details.AppendLine("\n");
            // 2. Verifique e adicione cada metadado, um por um.
            if (product.Metadata.TryGetValue("> aventura", out var aventura))
            {
                details.AppendLine($"> **Aventura:** {aventura}");
            }
            if (product.Metadata.TryGetValue("> mestre", out var mestre))
            {
                details.AppendLine($"> **Mestre:** {mestre}");
            }
            if (product.Metadata.TryGetValue("> vagas", out var vagas))
            {
                details.AppendLine($"> **Vagas Disponíveis:** {vagas}");
            }
            if (product.Metadata.TryGetValue("> horas", out var horas))
            {
                details.AppendLine($"> **Duração:** {horas}");
            }
            if (product.Metadata.TryGetValue("> horario", out var horario))
            {
                details.AppendLine($"> **Horário:** {horario}");
            }

            // 3. Adicione a descrição do produto (campo padrão do Stripe)
            if (!string.IsNullOrEmpty(product.Description))
            {
                details.AppendLine($"\n> **Descrição:**\n{product.Description}");
            }

            // 4. Se encontramos algum detalhe, adicionamos o campo ao embed.
            if (details.Length > 0)
            {
                embedBuilder.AddField("📋 Detalhes", details.ToString());
            }
            
            // 1. Verificamos se a lista de imagens não está vazia.
            if (product.Images.Any())
            {
                // 2. Pegamos a primeira imagem da lista e a usamos como a imagem principal do Embed.
                embedBuilder.WithImageUrl(product.Images.First());
            }

            // --- ENVIO DA MENSAGEM ---
            try
            {
                _logger.LogInformation("Enviando a mensagem para o Discord...");
                await channel.SendMessageAsync(embed: embedBuilder.Build());
                _logger.LogInformation("SUCESSO! Mensagem de notificação enviada para o canal #{ChannelName}.", channel.Name);
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "!!! FALHA AO ENVIAR MENSAGEM PARA O DISCORD !!! Causa provável: O bot não tem permissão para 'Enviar Mensagens' ou 'Anexar Links' neste canal.");
            }
        }
        
        private string GetPlanMappingName(string priceId)
        {
            var planMappingSection = _configuration.GetSection("PlanMapping");
            return planMappingSection.GetChildren().FirstOrDefault(x => x.Value == priceId)?.Key ?? string.Empty;
        }
    }
}