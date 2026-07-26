FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY src/consolidado-service/ src/consolidado-service/
RUN dotnet publish src/consolidado-service/Consolidado.Api/Consolidado.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Consolidado.Api.dll"]
