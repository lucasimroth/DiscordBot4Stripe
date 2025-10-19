using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace WorkerService1.Data
{
    public class UserSubscription
    {
        [Key]
        public string StripeSubscriptionId { get; set; } = string.Empty;
    
        public ulong DiscordUserId { get; set; }
        
        public string StripeCustomerId { get; set; } = string.Empty;
    }
    
    public class SubscriptionDbContext : DbContext
    {
        public DbSet<UserSubscription> UserSubscriptions { get; set; }
        public DbSet<PlanRoleMapping> PlanRoleMappings { get; set; }
        public DbSet<PlanMapping> PlanMappings { get; set; }
        
        public DbSet<ChannelProductMapping> ChannelProductMappings { get; set; }

    
        public SubscriptionDbContext(DbContextOptions<SubscriptionDbContext> options)
            : base(options)
        {
        }
    }
    
    public class ChannelProductMapping
    {
        [Key]
        public ulong DiscordChannelId { get; set; }

        public string StripePriceId { get; set; } = string.Empty;
    }
    
    public class PlanRoleMapping
    {
        [Key]
        public string StripePriceId { get; set; } = string.Empty;

        public ulong DiscordRoleId { get; set; }
    }
    
    public class PlanMapping
    {
        [Key]
        public string Slug { get; set; } // O nome amigável, ex: "a-maldicao-de-strahd"

        public string StripePriceId { get; set; }
        
        public string ProductName { get; set; } // Guardamos o nome original para exibição
    }
}
