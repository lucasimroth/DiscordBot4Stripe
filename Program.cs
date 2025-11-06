// Program.cs (Corrigido)


using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Stripe;
using Microsoft.EntityFrameworkCore;
using WorkerService1.Discord.Modules;
using WorkerService1.Discord.Handlers;
using WorkerService1.Discord.Services;
using WorkerService1.Data;
using WorkerService1.Services;

var builder = WebApplication.CreateBuilder(args);

//Injeção de dependencias
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Lê a string de conexão do ambiente (que o Render vai fornecer)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (!string.IsNullOrEmpty(connectionString))
{
    // Usa PostgreSQL se a string de conexão for encontrada (Ambiente de Produção/Render)
    builder.Services.AddDbContext<SubscriptionDbContext>(options =>
        options.UseNpgsql(connectionString)
    );
}
else
{
    // Mantém o SQLite se nenhuma string for encontrada (Ambiente de Desenvolvimento Local)
    builder.Services.AddDbContext<SubscriptionDbContext>(options =>
        options.UseSqlite("Data Source=subscriptions.db")
    );
}


//servicos discord bot
//1.
var discordConfig = new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers
};
builder.Services.AddSingleton(new DiscordSocketClient(discordConfig));

//2.
builder.Services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));
builder.Services.AddSingleton<CommandHandler>();
builder.Services.AddSingleton<DiscordEventHandler>();
builder.Services.AddSingleton<WelcomeModule>();
builder.Services.AddScoped<WorkerService1.Discord.Modules.ComprarCommandModule>();
builder.Services.AddScoped<WorkerService1.Discord.Modules.PlanosCommandModule>();
builder.Services.AddScoped<WorkerService1.Discord.Modules.StripeAdminModule>();
builder.Services.AddScoped<StripeService>();
builder.Services.AddScoped<WorkerService1.Discord.Services.SubscriptionService>();
builder.Services.AddScoped<ProductNotificationService>();
builder.Services.AddScoped<StripeWebhookService>();
builder.Services.AddScoped<DiscordInfraService>();


//3.
builder.Services.AddSingleton<DiscordBotService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<DiscordBotService>());

//APP execution

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<SubscriptionDbContext>();
    context.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
