using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using System.IO;
using System.Threading.Tasks;
using WorkerService1.Discord.Services;

namespace Workerservice1.Controller
{
    [Route("api/stripe-webhook")]
    [ApiController]
    public class StripeWebhookController : ControllerBase
    {
        private readonly ILogger<StripeWebhookController> _logger;
        private readonly StripeWebhookService _webhookService;
        private readonly string? _webhookSecret;
        // Injete os outros serviços se precisar manter os endpoints de teste
        private readonly ProductNotificationService _productNotificationService; 
    
        public StripeWebhookController(ILogger<StripeWebhookController> logger, IConfiguration config, StripeWebhookService webhookService, ProductNotificationService productNotificationService)
        {
            _logger = logger;
            _webhookService = webhookService;
            _webhookSecret = config["Stripe:WebhookSecret"];
            _productNotificationService = productNotificationService; // Para os testes
        }
    
        [HttpPost]
        public async Task<IActionResult> Post()
        {
            if (string.IsNullOrEmpty(_webhookSecret))
            {
                _logger.LogError("Webhook secret não configurado");
                return BadRequest();
            }
            
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            try
            {
                var stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"], _webhookSecret);
                _logger.LogInformation($"Evento do Stripe recebido: {stripeEvent.Type}");
                await _webhookService.ProcessWebhookEventAsync(stripeEvent);
                return Ok();
            }
            catch (StripeException e)
            {
                _logger.LogError(e, "Erro de assinatura do Webhook.");
                return BadRequest();
            }
        }
        
        // --- SEUS ENDPOINTS DE TESTE CONTINUAM AQUI ---
        // Eles apenas foram adaptados para chamar os serviços
        [HttpGet("test-product-created")]
        public async Task<IActionResult> TestProductCreated()
        {
            var testProduct = new Stripe.Product { Id = "test_prod_1", Name = "Produto Teste Criado" };
            await _productNotificationService.HandleProductCreated(testProduct);
            return Ok("Teste de criação de produto executado.");
        }
    
        // ... (adapte os outros endpoints de teste da mesma forma) ...
    }
}

