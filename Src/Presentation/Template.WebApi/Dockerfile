# Pinned in Phase 14 and patched in Phase 21 (.NET 10.0.13 security release): ASP.NET Core runtime 10.0.13 and SDK 10.0.402 (global.json); both tags pointed to these digests on 2026-10-14.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Central package versions and the SDK pin must be present for the restore layer (NU1015 otherwise).
COPY ["global.json", "Directory.Packages.props", "./"]
COPY ["Src/Presentation/Template.WebApi/Template.WebApi.csproj", "Src/Presentation/Template.WebApi/"]
COPY ["Src/Core/Template.Application/Template.Application.csproj", "Src/Core/Template.Application/"]
COPY ["Src/Core/Template.Domain/Template.Domain.csproj", "Src/Core/Template.Domain/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Files/Template.Infrastructure.Files.csproj", "Src/Infrastructure/Template.Infrastructure.Files/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Identity/Template.Infrastructure.Identity.csproj", "Src/Infrastructure/Template.Infrastructure.Identity/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Persistence/Template.Infrastructure.Persistence.csproj", "Src/Infrastructure/Template.Infrastructure.Persistence/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Resources/Template.Infrastructure.Resources.csproj", "Src/Infrastructure/Template.Infrastructure.Resources/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Caching/Template.Infrastructure.Caching.csproj", "Src/Infrastructure/Template.Infrastructure.Caching/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Messaging/Template.Infrastructure.Messaging.csproj", "Src/Infrastructure/Template.Infrastructure.Messaging/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Search/Template.Infrastructure.Search.csproj", "Src/Infrastructure/Template.Infrastructure.Search/"]
COPY ["Tests/Template.Functional.Tests/Template.Functional.Tests.csproj", "Tests/Template.Functional.Tests/"]
COPY ["Tests/Template.Integration.Tests/Template.Integration.Tests.csproj", "Tests/Template.Integration.Tests/"]
COPY ["Tests/Template.Unit.Tests/Template.Unit.Tests.csproj", "Tests/Template.Unit.Tests/"]

RUN dotnet restore "./Src/Presentation/Template.WebApi/Template.WebApi.csproj"
COPY . .
WORKDIR "/src/Src/Presentation/Template.WebApi"
RUN dotnet build "./Template.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Template.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Template.WebApi.dll"]