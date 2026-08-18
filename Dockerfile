FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY KartAiAssistantService.sln Directory.Build.props nuget.config ./
COPY packages/ packages/
COPY src/Api/Kart.AiAssistant.Api.csproj src/Api/
COPY src/Application/Kart.AiAssistant.Application.csproj src/Application/
COPY src/Domain/Kart.AiAssistant.Domain.csproj src/Domain/
COPY src/Infrastructure/Kart.AiAssistant.Infrastructure.csproj src/Infrastructure/
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget-packages \
    dotnet restore src/Api/Kart.AiAssistant.Api.csproj
COPY src/ src/
COPY contracts/ contracts/
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget-packages \
    dotnet publish src/Api/Kart.AiAssistant.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Kart.AiAssistant.Api.dll"]
