# Pinned in Phase 14: ASP.NET Core runtime 10.0.12 and SDK 10.0.401 (global.json); both tags pointed to these digests.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Central package versions and the SDK pin must be present for the restore layer (NU1015 otherwise).
COPY ["global.json", "Directory.Packages.props", "./"]
COPY ["Src/Presentation/Template.WebApi/Template.WebApi.csproj", "Src/Presentation/Template.WebApi/"]
COPY ["Src/Core/Template.Application/Template.Application.csproj", "Src/Core/Template.Application/"]
COPY ["Src/Core/Template.Domain/Template.Domain.csproj", "Src/Core/Template.Domain/"]
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