using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Threading.Tasks;

namespace WorkerService1.Discord.Modules
{
    // Este módulo contém comandos de interação (comandos de barra)
    public class RoleModule : InteractionModuleBase<SocketInteractionContext>
    {
        // Comando /cargo <usuario> <cargo>
        [SlashCommand("cargo", "Atribui um cargo a um usuário.")]
        // Apenas usuários com a permissão de "Gerenciar Cargos" podem usar este comando
        [RequireUserPermission(GuildPermission.ManageRoles)]
        public async Task AssignRoleCommand(
            [Summary("usuario", "O usuário que receberá o cargo.")] SocketGuildUser usuario,
            [Summary("cargo", "O cargo a ser atribuído.")] SocketRole cargo)
        {
            // --- Verificação de Segurança 1: Hierarquia de Cargos ---
            // O bot não pode atribuir um cargo que seja mais alto que o seu próprio.
            // Pegamos o usuário do bot no servidor atual.
            var botUser = Context.Guild.CurrentUser;
            if (botUser.Hierarchy <= cargo.Position)
            {
                await RespondAsync($"Não foi possível atribuir o cargo {cargo.Mention}. Meu cargo é mais baixo que este na hierarquia do servidor.", ephemeral: true);
                return;
            }

            //Verifica se o usuario em questão ja esta ou não com o cargo atribuido
            var cargos = usuario.Roles;
            foreach (var role in cargos)
            {
                if (role.Id == cargo.Id)
                {
                    await RespondAsync($"O usuario {usuario.Mention} ja possui o cargo  {cargo.Mention}.",
                        ephemeral: true);
                    return;
                }
            }
            
            // --- Ação ---
            // Adiciona o cargo ao usuário especificado
            await usuario.AddRoleAsync(cargo);

            // --- Feedback ---
            // Envia uma mensagem de confirmação visível apenas para quem usou o comando
            await RespondAsync($"O cargo {cargo.Mention} foi atribuído a {usuario.Mention} com sucesso!", ephemeral: true);
        }

        [SlashCommand(name: "remover-cargo", "Remover um cargo.")]
        [RequireUserPermission(GuildPermission.ManageRoles)]
        public async Task RemoverCargoCommand(
            [Summary("Usuario", "o usuário que receberá um cargo.")] SocketGuildUser usuario,
            [Summary("Cargo", "Cargo a ser atribuido")] SocketRole cargo)
        {
            var botUser = Context.Guild.CurrentUser;
            if (botUser.Hierarchy <= cargo.Position)
            {
                await RespondAsync($"Não foi possivel remover o cargo {cargo.Mention} .", ephemeral: true);
                return;
            }
            
            if (usuario.Roles.Any(r => r.Id == cargo.Id) == false)
            {
                await RespondAsync($"O usuário {usuario.Mention} não possui o cargo {cargo.Mention}.", ephemeral: true);
                return;
            }

            await usuario.RemoveRoleAsync(cargo);
            
            await RespondAsync($"cargo {cargo.Mention} removido do usuario {usuario.Mention} com sucesso!", ephemeral: true);
        } 
        
        
    }
}