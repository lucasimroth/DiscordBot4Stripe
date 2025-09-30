Organização de arquivos no modelo Service oriented design pattern

- Services: lidam com a logica de negocio (o que fazer), como as comunicações com stripe
- modules: gerenciam a interação com o usuario, basicamente comandos que o usuario pode usar nomeando tambem de "modulos de comando"
- handlers: conecta os eventos discord com a logica de programação (ele ouve os comando e executa se for um comando valido

------------------------------------------------- Controller --------------------------------------------
API/Controller/StripeWebhookController.cs
Classe(s): StripeWebhookController

Responsabilidade Principal: Servir como a "porta de entrada" da aplicação para a internet. Sua única função é 
receber e validar as notificações automáticas (webhooks) enviadas pelo Stripe. Este é um "Controller Magro" 
(Thin Controller), ou seja, ele não contém lógica de negócios, apenas direciona as requisições.

Principais Funções (Métodos Públicos):

Post(): Ativado por uma requisição POST na rota /api/stripe-webhook.
Ele recebe o evento do Stripe, valida a assinatura secreta (Stripe-Signature)
para garantir que a requisição é autêntica e segura. Se a validação for bem-sucedida,
ele repassa o evento para o StripeWebhookService processar e retorna 200 OK para o Stripe.

TestProductCreated(): Um endpoint de GET na rota /api/stripe-webhook/test-product-created.
Serve apenas para desenvolvimento, permitindo simular um evento de criação de produto para
testar a lógica de notificação no Discord sem precisar de um webhook real.

Dependências:

StripeWebhookService: É o serviço principal para o qual este controller delega todo o trabalho de processamento dos eventos.

ProductNotificationService: Usado especificamente pelos endpoints de teste para acionar lógicas de notificação de forma isolada.

IConfiguration: Para ler a chave secreta do webhook (Stripe:WebhookSecret) do arquivo appsettings.json.

Observações:

Este controller é a única parte do sistema que fica exposta publicamente na internet.

A segurança é garantida pela validação da assinatura. Se o WebhookSecret no appsettings.json estiver desalinhado
 com o do painel do Stripe, este controller retornará um erro 400 Bad Request e a execução será interrompida.
 
 -------------------------------------------------- Data ---------------------------------------------------------
 
 Data/SubscriptionDbContext.cs
 Classe(s): UserSubscription e SubscriptionDbContext
 
 Responsabilidade Principal: Definir a estrutura do banco de dados da aplicação e gerenciar a conexão com ele.
 Este arquivo é a "camada de persistência", responsável por guardar e recuperar informações sobre as assinaturas
 dos usuários.
 
 Principais Funções (Classes):
 
 UserSubscription: É a classe modelo (ou "entidade") que representa a tabela UserSubscriptions no banco de dados.
 Sua função é criar a ligação entre um usuário do Discord (DiscordUserId), sua assinatura no Stripe
 (StripeSubscriptionId) e seu cadastro de cliente no Stripe (StripeCustomerId).
 
 SubscriptionDbContext: É a classe de contexto do Entity Framework Core, a "ponte" principal entre o seu código C#
 e o banco de dados (o arquivo subscriptions.db). A propriedade UserSubscriptions é a porta de entrada para realizar
 operações na tabela (buscar, adicionar, remover registros de assinaturas).
 
 Dependências:
 
 Microsoft.EntityFrameworkCore: Esta é a biblioteca do .NET para interação com bancos de dados.
 
 É configurado no Program.cs para usar o provedor do SQLite.
 
 Observações:
 
 Qualquer serviço que precise ler ou escrever dados sobre as assinaturas (como o SubscriptionService) deve receber uma
 injeção de SubscriptionDbContext para poder acessar o banco de dados.
 
 O atributo [Key] na propriedade StripeSubscriptionId o define como a chave primária da tabela, garantindo que cada
 registro de assinatura seja único.[
 
 -------------------------------------------------Discord ------------------------------------------------------
 
+++++++++++++++++++++++ Handlers ++++++++++++++

Discord/Handlers/CommandHandler.cs
Classe(s): CommandHandler

Responsabilidade Principal: É o "cérebro" que gerencia todos os comandos de barra (/) do bot. Suas funções são:
encontrar todos os comandos definidos nos Módulos, registrá-los no Discord quando o bot fica online,
 ouvir quando um usuário os executa e direcionar a execução para o código correto.

Principais Funções (Métodos Públicos):

InitializeAsync(): É o método de inicialização. Ele "liga" o handler, descobrindo todos os módulos de comando no
projeto e se inscrevendo nos eventos do Discord (Ready e InteractionCreated) necessários para que os comandos funcionem.

Dependências:

DiscordSocketClient: Para se inscrever nos eventos do Discord.

InteractionService: A biblioteca do Discord.Net que efetivamente gerencia e executa os comandos.

IConfiguration: Para ler o ID do servidor (Discord:GuildId) e saber onde registrar os comandos para teste.

IServiceProvider: Usado pelo InteractionService para criar as instâncias dos Módulos de Comando, resolvendo suas
dependências (como injetar um StripeService em um comando, por exemplo).

Observações:

Este handler está configurado para registrar os comandos apenas em um servidor de teste (GuildId), o que é ótimo para
desenvolvimento rápido. Para que os comandos fiquem disponíveis em todos os servidores onde o bot entrar, a lógica
no método OnClientReady precisaria ser alterada para um registro global.

------

Discord/Handlers/DiscordEventHandler.cs
Classe(s): DiscordEventHandler

Responsabilidade Principal: É o "ouvinte" para eventos gerais do Discord que não são comandos. Sua função é se inscrever
nos eventos do gateway do Discord (como a entrada de um novo usuário, mensagens de log, etc.) e acionar a lógica de
negócios correspondente, que está em outras classes (como o WelcomeModule).

Funções (Todos os Métodos):

InitializeAsync(): Método público que "liga" o handler. Ele se inscreve nos eventos Ready, UserJoined e Log do cliente
 do Discord, conectando-os aos métodos privados correspondentes.

OnLog(LogMessage msg): Método privado acionado sempre que a biblioteca do Discord gera uma mensagem de log. Ele
simplesmente repassa essa mensagem para o sistema de logs principal da aplicação, centralizando toda a informação
no console.

OnReadyAsync(): Método privado acionado uma vez, quando o bot se conecta com sucesso e está pronto para operar. Ele
 registra a mensagem "BOT IS ONLINE and ready!" no console para confirmar o status.

OnUserJoinedAsync(SocketGuildUser member): Método privado acionado sempre que um novo usuário entra no servidor. Ele
registra no console quem entrou e, em seguida, delega a tarefa de enviar a mensagem de boas-vindas para o WelcomeModule.

Observações:

Esta classe é um excelente exemplo da separação de responsabilidades. Ela sabe quando algo acontece (um usuário entrou),
 mas não o que fazer a respeito (ela manda o WelcomeModule agir).

É o local ideal para adicionar novos "ouvintes" de eventos que não sejam comandos, como reações a emojis (ReactionAdded)
ou leitura de mensagens (MessageReceived).

++++++++++++++++++++++ Modules +++++++

Discord/Modules/ComprarCommandModule.cs
Classe(s): ComprarCommandModule

Responsabilidade Principal: Define a interface de usuário no Discord para o processo de compra. Este módulo contém os
 comandos que um usuário executa para iniciar a aquisição de um plano de assinatura. Ele atua como um "controlador", 
 recebendo a intenção do usuário e delegando a lógica de negócios de pagamento para o StripeService.

Funções (Todos os Métodos):

ComprarCommandModule(ILogger, StripeService): O construtor da classe, responsável por receber os serviços necessários
 (o Logger para registrar informações e o StripeService para a lógica de pagamento) através de injeção de dependência.

ComprarPlano(string nomePlano): Expõe o comando /comprar plano para os usuários. Este método é acionado quando o comando
 é executado no Discord. Ele recebe o nome do plano desejado, chama o StripeService para gerar um link de pagamento
  único do Stripe e, em seguida, envia este link para o usuário em uma mensagem privada (DM). A mensagem é formatada
   como um Embed com um botão "Finalizar Pagamento" para uma melhor experiência. Ele também envia uma confirmação no
    canal onde o comando foi usado e possui tratamento de erros para casos de plano inválido ou falhas na comunicação
     com o Stripe.

Observações:

O atributo [Group("comprar", ...)] no topo da classe faz com que todos os comandos definidos aqui sejam subcomandos.
 Neste caso, o comando final para o usuário é /comprar plano.

Este módulo demonstra perfeitamente a separação de responsabilidades: ele cuida da apresentação para o usuário (Embeds,
 botões, mensagens no Discord), enquanto o StripeService (que ele chama) cuida da lógica de negócios (criar a sessão de
  pagamento).

A resposta principal é enviada na conversa privada do usuário para manter a privacidade do link de pagamento.

-----------

Discord/Modules/InfoModule.cs
Classe(s): InfoModule

Responsabilidade Principal: Centralizar os comandos informativos do servidor. Este módulo contém tanto comandos para
 usuários gerais consultarem informações (como regras), quanto ferramentas para administradores postarem anúncios e
  mensagens formatadas.

Funções (Todos os Métodos):

InfoModule(ILogger): O construtor da classe, responsável por receber o serviço de Logging para registrar a atividade
 dos comandos.

ShowOrientations(): Expõe o comando /orientacoes, que pode ser usado por qualquer membro do servidor. Ele busca o embed
 de orientações pré-definido e o envia como uma mensagem privada (efêmera) para o usuário, permitindo uma consulta
  rápida das regras sem poluir os canais.

PostCustomEmbed(canal, titulo, mensagem, corHex): Expõe o comando /postar-embed, uma ferramenta restrita a
 administradores (com o cargo GrandeMestre). Permite criar e postar uma mensagem customizada (Embed) em qualquer
  canal. O administrador pode definir dinamicamente o canal de destino, o título, o conteúdo da mensagem (com suporte
   a quebras de linha \n) e até a cor da barra lateral.

BuildOrientationEmbed(): É um método privado auxiliar que constrói e retorna o embed padrão de "Orientações e Regras
 Principais". Centralizar a criação do embed aqui garante que, se as regras mudarem, só é preciso editar o código em
  um único lugar, e o comando /orientacoes será atualizado automaticamente.

Observações:

Este módulo é um bom exemplo de como ter comandos com diferentes níveis de permissão (um público, outro restrito)
 dentro da mesma classe.

O comando /postar-embed é uma poderosa ferramenta de administração, permitindo a criação de qualquer tipo de anúncio
 ou mensagem informativa sem a necessidade de criar novos comandos no código.

O uso do método privado BuildOrientationEmbed() é uma boa prática para não repetir código (princípio DRY - 
Don't Repeat Yourself).

-------------

Discord/Modules/RoleModule.cs
Classe(s): RoleModule

Responsabilidade Principal: Centralizar os comandos administrativos para gerenciamento de cargos no Discord.
 Este módulo contém as ferramentas que permitem à equipe de moderação adicionar e remover cargos de membros do
  servidor de forma controlada e segura.

Funções (Todos os Métodos):

AssignRoleCommand(SocketGuildUser usuario, SocketRole cargo): Expõe o comando /cargo. É restrito a usuários com a
 permissão de "Gerenciar Cargos". A função primeiro realiza duas verificações de segurança importantes: 1) Garante
  que o cargo do bot é hierarquicamente superior ao cargo que ele está tentando atribuir. 2) Verifica se o usuário
   alvo já não possui o cargo. Se ambas as condições passarem, ele adiciona o cargo ao usuário e envia uma mensagem
    de confirmação privada para o administrador que executou o comando.

RemoverCargoCommand(SocketGuildUser usuario, SocketRole cargo): Expõe o comando /remover-cargo, também restrito a
 usuários com a permissão de "Gerenciar Cargos". A função executa verificações de segurança similares: 1) Valida
  a hierarquia de cargos para garantir que o bot tem poder sobre o cargo em questão. 2) Confirma se o usuário
   realmente possui o cargo antes de tentar removê-lo. Se tudo estiver correto, remove o cargo e envia uma
    confirmação privada.

Observações:

A segurança é um ponto central neste módulo. Ambos os comandos são protegidos pelo atributo
 [RequireUserPermission(GuildPermission.ManageRoles)], garantindo que apenas membros autorizados possam executá-los.

A verificação de hierarquia (botUser.Hierarchy <= cargo.Position) é uma salvaguarda crucial. Ela impede que o bot
 tente executar uma ação que não tem permissão para fazer (gerenciar um cargo mais alto que o seu), o que resultaria
  em um erro da API do Discord. O código lida com isso proativamente.

O módulo também inclui lógicas para uma melhor experiência do usuário, como verificar se um cargo já foi atribuído
 ou se realmente pertence ao usuário antes de uma remoção, evitando ações desnecessárias e fornecendo feedback claro.
 
 --------
 
 Discord/Modules/StripeAdminModule.cs
 Classe(s): StripeAdminModule
 
 Responsabilidade Principal: Fornecer comandos administrativos para interagir e visualizar dados da conta do Stripe
  diretamente pelo Discord. Este módulo serve como uma interface para que a equipe de moderação possa consultar
   informações do Stripe sem precisar acessar o painel de controle da plataforma.
 
 Funções (Todos os Métodos):
 
 StripeAdminModule(ILogger, StripeService, IConfiguration): O construtor da classe, responsável por injetar os serviços
  necessários para logging (ILogger), comunicação com a API do Stripe (StripeService) e leitura de configurações do 
  appsettings.json (IConfiguration).
 
 ListActivePlans(ITextChannel canal): Expõe o comando /listar-planos, restrito a administradores (com o cargo
  GrandeMestre). Esta função busca todos os planos de assinatura ativos diretamente da API do Stripe, formata-os em uma
   lista legível e os posta em um Embed no canal do Discord especificado pelo administrador. A função é robusta e divide
    a lista em múltiplos campos ("Parte 1", "Parte 2", etc.) automaticamente, caso ela exceda o limite de 1024
     caracteres de um campo do Discord.
 
 GetPlanMappingName(string priceId): É um método privado auxiliar usado para encontrar um "nome amigável" para um plano.
  Ele consulta o arquivo appsettings.json (na seção PlanMapping) para traduzir um ID de preço técnico do Stripe (ex:
   price_123...) em um nome mais legível que possa ter sido definido na configuração.
 
 Observações:
 
 Este módulo é estritamente para uso administrativo, protegido pelo cargo GrandeMestre, garantindo que usuários
  comuns não tenham acesso a essas funções.
 
 A lógica de paginação de campos no comando ListActivePlans é uma salvaguarda importante para evitar que o comando
  falhe caso a lista de planos se torne muito extensa.
 
 O método GetPlanMappingName está duplicado em outros arquivos. Em uma futura refatoração, ele poderia ser movido para
  um serviço compartilhado para centralizar a lógica e evitar repetição de código.
  
--------------

Discord/Modules/WelcomeModule.cs
Classe(s): WelcomeModule

Responsabilidade Principal: Conter a lógica de negócios para dar as boas-vindas a novos membros. Esta classe é
 responsável por formatar e enviar a mensagem de boas-vindas em um canal pré-configurado sempre que um novo usuário
  entra no servidor.

Funções (Todos os Métodos):

WelcomeModule(ILogger, IConfiguration): O construtor da classe, responsável por injetar os serviços de Logging
 (para registrar a atividade) e de Configuração (para ler o ID do canal de boas-vindas do arquivo appsettings.json).

SendWelcomeMessageAsync(SocketGuildUser member): A função principal do módulo. Ela não é um comando, mas sim um método
 que é chamado pelo DiscordEventHandler quando o evento UserJoined é disparado. A função lê o ID do canal de boas-vindas
  das configurações, encontra o canal correspondente no servidor e envia uma mensagem pública mencionando o novo membro
   que acabou de entrar. O método inclui logs detalhados para cada etapa, facilitando a depuração de problemas como IDs
    de canal inválidos ou falta de permissão do bot.

Observações:

Diferente dos outros módulos que analisamos, esta classe não define comandos de barra (/). Ela é puramente um serviço
 de lógica de negócios que é acionado por um evento do sistema, e não diretamente por um usuário.

O canal para onde a mensagem é enviada é totalmente configurável através da chave Discord:BemVindoID no arquivo
 appsettings.json.

A lógica atual envia uma mensagem de texto simples. Este módulo poderia ser facilmente expandido para enviar uma
 mensagem mais elaborada (um "Embed"), como fizemos nos outros módulos.
 
 ++++++++++++ Services +++++
 
 Services/DiscordActionService.cs
 Classe(s): DiscordActionService
 
 Responsabilidade Principal: É um serviço de lógica de negócios central que executa ações concretas no Discord em
  resposta a eventos externos (principalmente do Stripe). Ele age como a ponte entre o "mundo dos pagamentos" e o
   "mundo da comunidade", sendo responsável por traduzir um evento de negócio (como um pagamento) em uma ação no
    servidor (como dar um cargo).
 
 Funções (Todos os Métodos):
 
 DiscordActionService(ILogger, IConfiguration, ...): O construtor, que injeta todas as ferramentas que a classe precisa
  para trabalhar: o logger, as configurações do appsettings.json, o DiscordBotService para interagir com a API do
   Discord, e o SubscriptionDbContext para acessar o banco de dados.
 
 GrantRoleAfterPaymentAsync(Stripe.Checkout.Session session): Acionado após um pagamento ser concluído com sucesso.
  Sua responsabilidade é extrair o ID do usuário do Discord e o plano comprado a partir dos dados do Stripe, encontrar
   o cargo correspondente no servidor, adicionar o cargo ao membro e salvar os detalhes da assinatura no banco de dados.
 
 RevokeRoleAfterSubscriptionEndAsync(Stripe.Subscription subscription): Acionado quando uma assinatura do Stripe é
  cancelada ou expira. Ele busca os detalhes da assinatura no banco de dados para encontrar o usuário, e então remove
   o cargo correspondente do membro no Discord.
 
 NotifyProductUpdate(Stripe.Product product, string eventType): Responsável por enviar notificações para um canal
  específico do Discord sobre mudanças nos produtos do Stripe (criação, atualização, deleção).
 
 Observações:
 
 O código fornecido para este arquivo é um template/esqueleto. As funções (GrantRoleAfterPaymentAsync, etc.) contêm
  comentários descrevendo a lógica que deve ser implementada, que foi extraída do StripeWebhookController original.
 
 Esta classe é um pilar da arquitetura de "Separação de Responsabilidades". Ela garante que a lógica de como interagir
  com o Discord (guild.GetUser, user.AddRoleAsync, etc.) esteja em um único lugar, em vez de espalhada por outros
   serviços ou controllers.
 
 A injeção do DiscordBotService para acessar o .Client do Discord é uma abordagem. Uma alternativa (que usamos no
  SubscriptionService) seria injetar o DiscordSocketClient diretamente, para diminuir o acoplamento entre os serviços.
  
 -----------------
 
 Discord/Services/DiscordBotService.cs
 Classe(s): DiscordBotService
 
 Responsabilidade Principal: É o ponto de partida e o "coração" operacional do bot do Discord. Sua única função é
  iniciar, conectar e manter o bot online. Ele herda de BackgroundService, o que o torna um serviço que roda
   continuamente em segundo plano dentro da aplicação principal. Ele não contém nenhuma lógica de comandos ou
    eventos; ele apenas orquestra a inicialização dos Handlers especializados que fazem o trabalho pesado.
 
 Funções (Todos os Métodos):
 
 DiscordBotService(ILogger, DiscordSocketClient, ...): O construtor da classe, que recebe todos os serviços essenciais
  para o funcionamento do bot via injeção de dependência: o cliente de conexão com o Discord (DiscordSocketClient),
   os handlers de eventos e comandos, o logger e o serviço de configuração.
 
 ExecuteAsync(CancellationToken stoppingToken): Este é o método principal, executado automaticamente quando a
  aplicação inicia. Ele realiza a sequência de inicialização do bot:
 
 Chama o método InitializeAsync() do DiscordEventHandler para que ele comece a ouvir eventos gerais.
 
 Chama o método InitializeAsync() do CommandHandler para que ele descubra e registre os comandos de barra.
 
 Lê o token secreto do bot a partir das configurações.
 
 Usa o token para conectar e autenticar o bot no Discord.
 
 Entra em um estado de espera infinita (Task.Delay(Timeout.Infinite)) para manter o processo do bot ativo.
 
 Observações:
 
 Esta classe é o exemplo perfeito de um "orquestrador". Ela é "agnóstica" à lógica de negócios: não sabe como os
  comandos ou eventos funcionam, apenas sabe quem chamar (_commandHandler, _eventHandler) para iniciar o processo.
 
 O uso de BackgroundService é a prática recomendada pela Microsoft para criar serviços de longa duração, como um bot,
  que rodam em segundo plano em uma aplicação .NET.
 
 Se um novo Handler for criado no futuro (ex: para gerenciar reações a mensagens), sua chamada de inicialização
  (.InitializeAsync()) deverá ser adicionada neste arquivo.
 
 -----------
 
 Discord/Services/ProductNotificationService.cs
 Classe(s): ProductNotificationService
 
 Responsabilidade Principal: Gerenciar e enviar as notificações para um canal do Discord sobre mudanças nos produtos
  do Stripe (criação, atualização, deleção). Sua função mais crítica é implementar uma lógica de "debounce"
   (anti-duplicidade), usando um sistema de espera e cancelamento para garantir que ações rápidas e sequenciais no
  painel do Stripe (como criar e logo em seguida editar um produto) resultem em uma única e coesa notificação no Discord.
 
 Funções (Todos os Métodos):
 
 ProductNotificationService(...): O construtor, que recebe as dependências necessárias para funcionar: o logger, as
  configurações do appsettings.json e o cliente de conexão do Discord.
 
 HandleProductCreated(product), HandleProductDeleted(product), HandleProductUpdated(product): Métodos públicos que
  servem como pontos de entrada para os eventos de webhook que vêm do StripeWebhookService. Eles contêm a lógica de
   prioridade para decidir se uma notificação deve ser iniciada, ignorada, ou se uma notificação pendente de outro
    evento deve ser cancelada para evitar duplicidade.
 
 StartDelayedNotification(...): Método privado central que implementa a lógica de espera de 5 segundos. Ele recebe um
  evento, o adiciona a uma fila de espera (um ConcurrentDictionary) e agenda o envio da notificação. Ele também é
   responsável por cancelar tarefas antigas do mesmo tipo para "agrupar" edições múltiplas.
 
 ProcessProductEvent(...): Método privado que é chamado após o tempo de espera terminar. Ele orquestra a busca de
  dados atualizados do Stripe (GetActivePlans) e o envio da mensagem final (SendActivePlansToDiscord).
 
 GetActivePlans(): Método privado que se comunica com a API do Stripe para obter uma lista de todos os preços de
  produtos que estão atualmente ativos.
 
 SendActivePlansToDiscord(...): Método privado responsável por construir e enviar a mensagem final (Embed) no
  Discord. Ele monta o Embed, adicionando dinamicamente o título, cor, os metadados customizados (aventura, mestre,
   vagas, etc.), a imagem do produto e a lista de preços ativos, e o envia para o canal de notificações configurado.
 
 Observações:
 
 Esta classe é o coração da automação de notificações e contém a lógica mais sofisticada do sistema para lidar com a
  natureza assíncrona e, por vezes, desordenada dos webhooks.
 
 A lógica de cancelamento e espera (debounce) é a chave para uma boa experiência do usuário, evitando o spam de
  notificações no canal do Discord.
 
 O conteúdo do Embed é montado de forma totalmente dinâmica a partir dos dados recebidos do Stripe.Product,
  incluindo seus Metadata e Images.
  
  --------------
  
  Discord/Services/StripeService.cs
  Classe(s): StripeService
  
  Responsabilidade Principal: É o principal ponto de comunicação ativa com a API do Stripe. Esta classe encapsula a
   lógica de negócios para criar sessões de pagamento para os usuários e buscar informações sobre os planos/produtos
    disponíveis na sua conta do Stripe.
  
  Funções (Todos os Métodos):
  
  StripeService(IConfiguration, ILogger): O construtor da classe. Além de injetar as dependências de logging e
   configuração, ele tem a importante função de configurar a chave secreta da API do Stripe (StripeConfiguration.ApiKey)
    para toda a aplicação, lendo-a a partir do appsettings.json.
  
  CreateCheckoutSessionUrlAsync(string planName, ulong discordUserId): Função chamada pelo comando /comprar plano.
   Ela recebe o nome de um plano e o ID de um usuário do Discord, traduz o nome do plano para um ID de Preço do Stripe
    (usando o PlanMapping na configuração) e, em seguida, cria uma sessão de checkout de assinatura. A parte mais
     importante é que ela "carimba" o discordUserId nos metadados da transação, criando a ponte essencial entre o
      pagamento no Stripe e o usuário no Discord. No final, retorna a URL de pagamento para ser enviada ao usuário.
  
  GetActivePlansAsync(): Função chamada pelo comando /listar-planos. Ela busca na API do Stripe e retorna uma lista de
   todos os "Preços" (Stripe.Price) que estão ativos. Ela usa o recurso de "Expansão" (Expand) da API do Stripe para que,
    junto com cada preço, venham também os dados completos do "Produto" associado (nome, imagem, etc.), otimizando a
     busca de dados.
  
  Observações:
  
  O namespace WorkerService1.Discord.Services indica que este serviço pertence ao bot. No entanto, como ele contém
   lógica de negócios do Stripe que poderia ser usada por outras partes do sistema (como a API), ele se encaixaria
    melhor na pasta raiz Services/, com o namespace WorkerService1.Services, conforme nossa arquitetura final.
  
  Esta classe não lida com o recebimento de webhooks; essa é a responsabilidade do StripeWebhookService. 
  Ela apenas inicia ações e busca informações.
  
  O mapeamento de nomes de planos para IDs de preço (PlanMapping no appsettings.json) é crucial para o 
  funcionamento do CreateCheckoutSessionUrlAsync.
  
  -----------------
  
  Discord/Services/StripeWebhookService.cs
  Classe(s): StripeWebhookService
  
  Responsabilidade Principal: Atuar como o "roteador" ou "orquestrador" principal para os eventos de webhook recebidos
   do Stripe. Sua única função é receber um evento já validado do StripeWebhookController, identificar o tipo de evento
    (created, deleted, etc.) e delegar o processamento para o serviço especializado correto.
  
  Funções (Todos os Métodos):
  
  StripeWebhookService(ILogger, ...): O construtor, que injeta os outros serviços de lógica de negócios
   (SubscriptionService e ProductNotificationService). Ele não interage diretamente com o Discord ou com o banco de dados;
    ele apenas "conhece" quais serviços chamar para cada tipo de tarefa.
  
  ProcessWebhookEventAsync(Event stripeEvent): O único método público da classe. Ele contém uma instrução switch que
   examina o tipo do evento do Stripe. Para eventos relacionados a pagamentos e assinaturas
    (como checkout.session.completed), ele chama o SubscriptionService. Para eventos relacionados a produtos
     (como product.created), ele chama o ProductNotificationService.
  
  Observações:
  
  Esta classe é o coração da camada de serviço da API. Ela desacopla o Controller (que só lida com a requisição web) da
   lógica de negócios específica (que está nos outros serviços). Isso torna o sistema muito mais organizado.
  
  Se novos tipos de eventos do Stripe precisarem ser tratados no futuro (ex: invoice.payment_failed), a principal
   alteração será adicionar um novo case a este switch e criar o método correspondente no serviço apropriado.
  
  Assim como o StripeService, este é um serviço de lógica de negócios geral. Na sua estrutura de pastas final, ele se
   encaixa melhor na pasta raiz Services/, com o namespace WorkerService1.Services
   
  ----------------
  
  Discord/Services/SubscriptionService.cs
  Classe(s): SubscriptionService
  
  Responsabilidade Principal: Gerenciar o ciclo de vida das assinaturas, sincronizando o status de pagamento do Stripe
   com os cargos (roles) no Discord e com o banco de dados local. Esta é uma das classes de lógica de negócios mais
    importantes, conectando todas as partes do sistema: Stripe (eventos de pagamento), Discord (ações de cargos) e a
     Base de Dados (persistência do estado).
  
  Funções (Todos os Métodos):
  
  SubscriptionService(...): O construtor, que recebe todas as dependências que a classe precisa para operar: logger,
   as configurações do appsettings.json, o cliente do Discord para executar ações no servidor e o DbContext para
    interagir com o banco de dados.
  
  HandleCheckoutSessionCompleted(session): Acionado pelo StripeWebhookService quando um pagamento de assinatura é
   concluído com sucesso. Ele executa a sequência completa de "provisionamento": lê os dados da sessão do Stripe
    (incluindo o ID do usuário do Discord nos metadados), mapeia o plano comprado para um cargo específico do Discord
     (usando o RoleMapping da configuração), encontra o usuário no servidor, adiciona o cargo a ele e, finalmente,
      salva um registro dessa nova assinatura no banco de dados.
  
  HandleSubscriptionDeleted(subscription): Acionado pelo StripeWebhookService quando uma assinatura é cancelada. Ele
   executa a sequência de "desprovisionamento": usa o ID da assinatura do Stripe para encontrar o registro correspondente
    no banco de dados, descobre qual usuário do Discord e qual cargo estão associados, remove o cargo do membro no
     Discord e, por fim, apaga o registro da assinatura do banco de dados para manter tudo limpo.
  
  Observações:
  
  A chamada await guild.DownloadUsersAsync() é uma etapa crucial no método HandleCheckoutSessionCompleted. Ela força
   o bot a baixar uma lista atualizada de todos os membros do servidor, garantindo que ele consiga encontrar o usuário
    que fez a compra, mesmo que ele tenha acabado de entrar no servidor.
  
  A interação com o banco de dados (_dbContext) é fundamental. É ela que permite que o bot "lembre" qual assinatura
   do Stripe pertence a qual usuário do Discord, o que é essencial para o processo de remoção do cargo quando a
    assinatura é cancelada.
  
  O mapeamento de Price ID (Stripe) para Role ID (Discord) é feito através da seção RoleMapping no arquivo appsettings.json.
  
  Na sua estrutura de pastas final, por ser um serviço de lógica de negócios, ele se encaixaria melhor na
   pasta raiz Services/, com o namespace WorkerService1.Services.
   
--------------------------------------------------- PROGRAM -----------------------------------------------

Program.cs
Classe(s): Não aplicável (usa o modelo de "top-level statements" do .NET moderno).

Responsabilidade Principal: É o ponto de entrada e o arquivo de configuração central de toda a aplicação.
 Sua função é "montar" o aplicativo, registrando todos os serviços necessários (API, Banco de Dados, Discord Bot,
  Stripe), configurando como as requisições da web são tratadas e, finalmente, iniciando a execução de tudo.

Funções (Principais Blocos de Configuração):

Configuração da API e Dados: Configura os serviços essenciais para a API web funcionar. Isso inclui registrar os
 Controllers (endpoints da API), o DbContext (a ponte com o banco de dados SQLite) e o Swagger (a ferramenta de
  documentação da API). Também define a chave global da API do Stripe.

Configuração do Bot do Discord: Registra todos os componentes do bot no sistema de injeção de dependência. Isso inclui:

O cliente de conexão (DiscordSocketClient) com as permissões corretas (Intents).

O serviço de interação para comandos (InteractionService).

Todos os seus Handlers (CommandHandler, DiscordEventHandler).

Todos os seus Módulos de comando e lógica (WelcomeModule, InfoModule, etc.).

Todos os seus serviços de lógica de negócios (StripeService, SubscriptionService, etc.).

Serviço em Background: Registra o DiscordBotService como um IHostedService, uma instrução que diz ao .NET: "inicie
 este serviço junto com a aplicação e o mantenha rodando em segundo plano".

Criação do Banco de Dados: Contém uma lógica que, ao iniciar a aplicação, verifica se o arquivo de banco de dados
 (subscriptions.db) e suas tabelas existem, criando-os caso seja a primeira execução.

Pipeline HTTP: Define a "esteira" de como o servidor web deve tratar as requisições da internet, habilitando o Swagger
 em ambiente de desenvolvimento e direcionando o tráfego para os Controllers corretos.

Execução (app.Run()): A linha final que efetivamente "liga" o servidor web e todos os serviços em background,
 iniciando a aplicação.

Observações:

Este arquivo é o "maestro" da orquestra. Se um novo serviço for criado no futuro, ele precisa ser registrado
 aqui para que o resto da aplicação possa recebê-lo via injeção de dependência.

Ele unifica as duas "personalidades" da aplicação: a de uma API Web (que responde a requisições da internet) e a de
 um Serviço de Background (o bot do Discord que fica sempre online).
