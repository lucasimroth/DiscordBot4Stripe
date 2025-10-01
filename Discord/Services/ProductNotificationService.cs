using Discord;
using Discord.WebSocket;
using System.Text;
using System.Collections.Concurrent;

namespace WorkerService1.Discord.Services
{
    public class ProductNotificationService
    {
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingCreations = new();
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingUpdates = new();
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingDeletions = new();

        private readonly ILogger<ProductNotificationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly DiscordSocketClient _client;

        public ProductNotificationService(ILogger<ProductNotificationService> logger, IConfiguration configuration,
            DiscordSocketClient client)
        {
            _logger = logger;
            _configuration = configuration;
            _client = client;
        }

        public Task HandleProductCreated(Stripe.Product product)
        {
            _logger.LogInformation($"Evento CREATED recebido para {product.Id}. Tem prioridade sobre 'updated'.");

            // Cancela qualquer notificação de 'updated' que possa ter chegado antes por engano.
            if (_pendingUpdates.TryRemove(product.Id, out var cts))
            {
                _logger.LogWarning($"Cancelando notificação de 'updated' pendente para dar lugar à de 'created'.");
                cts.Cancel();
            }

            // Inicia a notificação de 'created' (que agora não será cancelada por um 'updated').
            return StartDelayedNotification(product, "created", TimeSpan.FromSeconds(5), _pendingCreations);
        }

        public Task HandleProductDeleted(Stripe.Product product)
        {
            _logger.LogInformation($"Evento DELETED recebido para {product.Id}. Tem prioridade máxima.");

            // Cancela QUALQUER notificação pendente (created ou updated).
            if (_pendingUpdates.TryRemove(product.Id, out var cts2)) cts2.Cancel();

            // AGORA, em vez de agir imediatamente, ele também inicia a espera de 5 segundos.
            return StartDelayedNotification(product, "deleted", TimeSpan.FromSeconds(5), _pendingDeletions);
        }

        public Task HandleProductUpdated(Stripe.Product product)
        {
            // --- LÓGICA DE PRIORIDADE ---
            // Se uma notificação de 'created' já está na fila, este 'updated' é provavelmente
            // uma edição rápida e deve ser IGNORADO para não cancelar a mensagem de criação.
            if (_pendingCreations.ContainsKey(product.Id))
            {
                _logger.LogInformation(
                    $"Ignorando evento 'updated' para {product.Id} porque uma notificação de 'created' já está pendente.");
                return Task.CompletedTask;
            }

            var eventType = !product.Active ? "archived" : "updated";
            return StartDelayedNotification(product, eventType, TimeSpan.FromSeconds(5), _pendingUpdates);
        }

        private Task StartDelayedNotification(Stripe.Product product, string eventType, TimeSpan delay,
            ConcurrentDictionary<string, CancellationTokenSource> pendingDictionary)
        {
            _logger.LogInformation(
                $"Iniciando espera de {delay.TotalSeconds}s para o evento '{eventType}' do produto {product.Id}.");

            var cts = new CancellationTokenSource();

            // Cancela qualquer tarefa antiga DO MESMO TIPO e adiciona a nova
            if (pendingDictionary.TryGetValue(product.Id, out var oldCts))
            {
                oldCts.Cancel();
            }

            pendingDictionary[product.Id] = cts;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, cts.Token);
                    _logger.LogInformation(
                        $"Tempo de espera para '{eventType}' ({product.Id}) expirou. Processando...");
                    await ProcessProductEvent(product, eventType);
                }
                catch (TaskCanceledException)
                {
                    _logger.LogInformation($"A notificação de '{eventType}' para {product.Id} foi cancelada.");
                }
                finally
                {
                    pendingDictionary.TryRemove(new KeyValuePair<string, CancellationTokenSource>(product.Id, cts));
                }
            });

            return Task.CompletedTask;
        }

        private async Task ProcessProductEvent(Stripe.Product product, string eventType)
        {
            _logger.LogInformation($"Processando evento de produto '{eventType}' para: {product.Name}");
            try
            {
                var activePlans = await GetActivePlans();
                await SendActivePlansToDiscord(product, eventType);
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
        private async Task SendActivePlansToDiscord(Stripe.Product product, string eventType)
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
                _logger.LogError("Discord:GuildId não configurado ou é inválido.");
                return;
            }

            _logger.LogInformation("Configurações de Guild ID ({guildId}) e Channel ID ({channelId}) lidas.", guildId,
                channelId);

            var guild = _client?.GetGuild(guildId);
            if (guild == null)
            {
                _logger.LogError(
                    "Não foi possível encontrar o Servidor (Guild) com o ID: {GuildId}. Verifique se o bot está no servidor.",
                    guildId);
                return;
            }

            _logger.LogInformation("Servidor (Guild) '{GuildName}' encontrado.", guild.Name);

            var channel = guild.GetTextChannel(channelId);
            if (channel == null)
            {
                /* ... */
                return;
            }

            // --- LÓGICA DO EMBED ---
            var (title, color) = eventType switch
            {
                "created" => ("🆕 Novo Produto Criado no Stripe!", Color.Green),
                "deleted" => ("🗑️ Produto Deletado no Stripe!", Color.Red),
                "archived" => ("📦 Produto Arquivado no Stripe!", Color.Orange),
                "updated" => ("📝 Produto Atualizado no Stripe!", Color.Blue),
                _ => ("📝 Produto Atualizado no Stripe!", Color.Gold)
            };

            var embedBuilder = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription($"**Produto:** {product.Name}\n")
                .WithColor(color)
                .WithCurrentTimestamp()
                .WithFooter($"ID do Produto: {product.Id}");

            // --- LÓGICA DE DETALHES CORRIGIDA ---
            var details = new StringBuilder();

            // Primeiro, busca todos os metadados
            if (product.Metadata.TryGetValue("aventura", out var aventura))
                details.AppendLine($"> **Aventura:** {aventura}");
            if (product.Metadata.TryGetValue("mestre", out var mestre)) details.AppendLine($"> **Mestre:** {mestre}");
            if (product.Metadata.TryGetValue("vagas", out var vagas))
                details.AppendLine($"> **Vagas Disponíveis:** {vagas}");
            if (product.Metadata.TryGetValue("horas", out var horas)) details.AppendLine($"> **Duração:** {horas}");
            if (product.Metadata.TryGetValue("horario", out var horario))
                details.AppendLine($"> **Horário:** {horario}");
            if (!string.IsNullOrEmpty(product.Description))
                details.AppendLine($"\n> **Descrição:**\n> {product.Description.Replace("\n", "\n> ")}");

            // SÓ ADICIONA O CAMPO SE A STRING 'details' TIVER ALGUM CONTEÚDO REAL
            if (details.Length > 0)
            {
                embedBuilder.AddField("📋 Detalhes", details.ToString());
            }
            // --- FIM DA CORREÇÃO ---

            if (product.Images.Any())
            {
                embedBuilder.WithImageUrl(product.Images.First());
            }

            // --- ENVIO DA MENSAGEM ---
            try
            {
                await channel.SendMessageAsync(embed: embedBuilder.Build());
                _logger.LogInformation("SUCESSO! Mensagem de notificação enviada...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "!!! FALHA AO ENVIAR MENSAGEM PARA O DISCORD !!!");
            }
        }
    }
}