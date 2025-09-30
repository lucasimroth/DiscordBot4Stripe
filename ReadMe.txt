Organização de arquivos no modelo Service oriented design pattern

- Services: lidam com a logica de negocio (o que fazer), como as comunicações com stripe
- modules: gerenciam a interação com o usuario, basicamente comandos que o usuario pode usar nomeando tambem de "modulos de comando"
- handlers: conecta os eventos discord com a logica de programação (ele ouve os comando e executa se for um comando valido