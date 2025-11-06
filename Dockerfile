# Estágio 1: Build (Construção)
# Usamos a imagem oficial do SDK do .NET 9 para construir o projeto
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copia os arquivos de projeto e restaura os pacotes (NuGet)
COPY *.sln .
COPY WorkerService1/*.csproj ./WorkerService1/
RUN dotnet restore

# Copia todo o resto do código-fonte e faz o publish (compila para produção)
COPY . .
WORKDIR /src/WorkerService1
RUN dotnet publish -c Release -o /app/publish

# Estágio 2: Final (Execução)
# Usamos a imagem leve do ASP.NET Runtime para rodar o app
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Expõe a porta 80 (padrão que o Render espera)
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

# O comando para iniciar o seu bot
ENTRYPOINT ["dotnet", "WorkerService1.dll"]