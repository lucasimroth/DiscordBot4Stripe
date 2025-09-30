using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorkerService1.Discord.Services
{
    public class StripeService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<StripeService> _logger;

        public StripeService(IConfiguration config, ILogger<StripeService> logger)
        {
            _config = config;
            _logger = logger;
            // A chave da API pode ser configurada aqui ou no Program.cs
            StripeConfiguration.ApiKey = _config["Stripe:SecretKey"];
        }

        public async Task<string> CreateCheckoutSessionUrlAsync(string planName, ulong discordUserId)
        {
            var priceId = _config.GetValue<string>($"PlanMapping:{planName.ToLower()}");
            if (string.IsNullOrEmpty(priceId))
            {
                // Lança uma exceção que o módulo de comando pode capturar
                throw new ArgumentException($"Plano '{planName}' não encontrado.");
            }

            try
            {
                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = new List<SessionLineItemOptions>
                    {
                        new() { Price = priceId, Quantity = 1 }
                    },
                    Mode = "subscription",
                    SuccessUrl = "https://discord.com/channels/@me", // Idealmente uma página de sucesso
                    CancelUrl = "https://discord.com/channels/@me",   // Idealmente uma página de cancelamento
                    Metadata = new Dictionary<string, string>
                    {
                        { "discord_user_id", discordUserId.ToString() }
                    }
                };

                var service = new SessionService();
                var session = await service.CreateAsync(options);
                return session.Url;
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe API error ao criar sessão de checkout para o usuário {UserId}", discordUserId);
                // Lança a exceção para que a camada de cima saiba que algo deu errado
                throw; 
            }
        }
        // ... dentro da classe StripeService

        public async Task<List<Stripe.Price>> GetActivePlansAsync()
        {
            _logger.LogInformation("Buscando planos ativos do Stripe...");
            var options = new Stripe.PriceListOptions
            {
                Active = true,
                Limit = 100,
                // --- ADICIONE ESTA LINHA ---
                // Pede ao Stripe para incluir o objeto completo do Produto em cada Preço
                Expand = new List<string> { "data.product" }
            };
            var prices = await new Stripe.PriceService().ListAsync(options);
            _logger.LogInformation("{Count} planos ativos encontrados.", prices.Data.Count);
            return prices.Data.ToList();
        }
    }
}