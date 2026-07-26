# Contexto de build é a raiz do repositório: o Directory.Build.props fica lá e
# vale para todos os projetos.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY src/lancamentos-service/ src/lancamentos-service/
RUN dotnet publish src/lancamentos-service/Lancamentos.Api/Lancamentos.Api.csproj -c Release -o /app

# Imagem Debian, não Alpine: InvariantGlobalization está desligado e o domínio
# depende dos dados de ICU e de tzdata para resolver America/Sao_Paulo.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lancamentos.Api.dll"]
