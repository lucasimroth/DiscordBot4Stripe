using Stripe;
using Microsoft.Extensions.Logging;

namespace WorkerService1.Discord.Services
{
    public class StripeWebhookService
    {
        private readonly ILogger<StripeWebhookService> _logger;
        private readonly SubscriptionService _subscriptionService;
        private readonly ProductNotificationService _productNotificationService;

        public StripeWebhookService(ILogger<StripeWebhookService> logger, SubscriptionService subscriptionService, ProductNotificationService productNotificationService)
        {
            _logger = logger;
            _subscriptionService = subscriptionService;
            _productNotificationService = productNotificationService;
        }

        public async Task ProcessWebhookEventAsync(Event stripeEvent)
        {
            _logger.LogInformation($"Processando evento do Stripe: {stripeEvent.Type}");
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    if (stripeEvent.Data.Object is Stripe.Checkout.Session s1) await _subscriptionService.HandleCheckoutSessionCompleted(s1);
                    break;
                case "customer.subscription.deleted":
                    if (stripeEvent.Data.Object is Stripe.Subscription s2) await _subscriptionService.HandleSubscriptionDeleted(s2);
                    break;
                case "product.created":
                    if (stripeEvent.Data.Object is Stripe.Product p1) await _productNotificationService.HandleProductCreated(p1);
                    break;
                case "product.deleted":
                    if (stripeEvent.Data.Object is Stripe.Product p2) await _productNotificationService.HandleProductDeleted(p2);
                    break;
                case "product.updated":
                    if (stripeEvent.Data.Object is Stripe.Product p3) await _productNotificationService.HandleProductUpdated(p3);
                    break;
            }
        }
    }
}