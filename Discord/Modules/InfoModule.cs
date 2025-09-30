using Discord;
using Discord.Interactions;
using System.Globalization; // Adicionado para a conversão de cores


namespace WorkerService1.Discord.Modules
{
    public class InfoModule : InteractionModuleBase<SocketInteractionContext>
    {
        // 1. Adiciona um campo privado para o logger
        private readonly ILogger<InfoModule> _logger;

        // 2. O construtor agora recebe o logger via injeção de dependência
        public InfoModule(ILogger<InfoModule> logger)
        {
            _logger = logger;
        }

        // --- COMANDO 1: PARA QUALQUER USUÁRIO (COM LOGS) ---
        [SlashCommand("orientacoes", "Envia a mensagem de orientações do servidor.")]
        public async Task ShowOrientations()
        {
            _logger.LogInformation("--- Comando /orientacoes INICIADO pelo usuário {User} ---", Context.User.Username);
            try
            {
                var embed = BuildOrientationEmbed();
                await RespondAsync(embed: embed, ephemeral: true);
                _logger.LogInformation("SUCESSO: Embed de orientações enviado de forma privada para {User}.", Context.User.Username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "!!! EXCEÇÃO no comando /orientacoes !!!");
            }
            _logger.LogInformation("--- Comando /orientacoes FINALIZADO ---");
        }

        // --- COMANDO 2: PARA O ADMINISTRADOR (COM LOGS) ---
        // --- COMANDO 2: FERRAMENTA UNIVERSAL PARA ADMINS ---
        [SlashCommand("postar-embed", "Posta uma mensagem formatada e customizada em um canal.")]
        [RequireRole("GrandeMestre")]
        
        public async Task PostCustomEmbed(
            [Summary("canal", "O canal onde a mensagem será postada.")] ITextChannel canal,
            [Summary("titulo", "O título da sua mensagem.")] string titulo,
            [Summary("mensagem", "O conteúdo. Use '\\n' para pular linha. Admite Markdown do Discord.")] string mensagem,
            [Summary("cor", "O código HEX da cor da barra lateral. Ex: #70FF00")] string corHex = null)
        {
            await DeferAsync(ephemeral: true);

            var embedBuilder = new EmbedBuilder()
                .WithTitle(titulo)
                .WithDescription(mensagem.Replace("\\n", "\n")) // Permite usar "\n" para quebras de linha
                .WithCurrentTimestamp()
                .WithFooter(text: $"Postado por: {Context.User.Username}", iconUrl: Context.User.GetAvatarUrl());
            
            // Tenta definir a cor, se for um código HEX válido
            if (corHex != null && uint.TryParse(corHex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.CurrentCulture, out uint hexColor))
            {
                embedBuilder.WithColor(new Color(hexColor));
            }

            await canal.SendMessageAsync(embed: embedBuilder.Build());
            await FollowupAsync("Sua mensagem customizada foi postada com sucesso!");
        }

        // --- MÉTODO PRIVADO QUE CONSTRÓI A SUA MENSAGEM (sem alterações) ---
        private Embed BuildOrientationEmbed()
        {
            var embedBuilder = new EmbedBuilder()
                .WithTitle("📜 | Orientações e Regras Principais")
                .WithDescription("Bem-vindo(a) ao Discord do RPG-Next! Para uma melhor convivência, por favor, leia as informações abaixo.")
                .WithColor(new Color(0x70FF00))
                .WithCurrentTimestamp()
                .WithFooter(text: "Qualquer dúvida, procure a moderação.", iconUrl: Context.Guild.IconUrl);

            embedBuilder.AddField("1. O que é este servidor?",
                "> Este é um servidor para alugar um mestre de RPG e jogar suas aventuras favoritas!");

            embedBuilder.AddField("2. Como participar de uma mesa?",
                "> é só dar um /Mesas ou /compra, que as instruções para escolher vão aparecer!!");
            
            embedBuilder.AddField("3. Dúvidas ou Problemas?",
                "> Chame um moderador no canal <#1421887543804432515>.",
                inline: true);

            return embedBuilder.Build();
        }
    }
}