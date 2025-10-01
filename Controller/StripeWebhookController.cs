using Microsoft.AspNetCore.Mvc;
using Stripe;
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
        
    
        public StripeWebhookController(ILogger<StripeWebhookController> logger, IConfiguration config, StripeWebhookService webhookService)
        {
            _logger = logger;
            _webhookService = webhookService;
            _webhookSecret = config["Stripe:WebhookSecret"];
            
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
                var stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"], _webhookSecret, throwOnApiVersionMismatch: false);
                _logger.LogWarning(">>> Evento Stripe Recebido: {EventType} às {Timestamp:O}", stripeEvent.Type, DateTime.UtcNow);
                await _webhookService.ProcessWebhookEventAsync(stripeEvent);
                return Ok();
            }
            catch (StripeException e)
            {
                _logger.LogError(e, "Erro de assinatura do Webhook.");
                return BadRequest();
            }
        }
    }
}

