FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Src/Presentation/Template.WebApi/Template.WebApi.csproj", "Src/Presentation/Template.WebApi/"]
COPY ["Src/Core/Template.Application/Template.Application.csproj", "Src/Core/Template.Application/"]
COPY ["Src/Core/Template.Domain/Template.Domain.csproj", "Src/Core/Template.Domain/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Identity/Template.Infrastructure.Identity.csproj", "Src/Infrastructure/Template.Infrastructure.Identity/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Persistence/Template.Infrastructure.Persistence.csproj", "Src/Infrastructure/Template.Infrastructure.Persistence/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Resources/Template.Infrastructure.Resources.csproj", "Src/Infrastructure/Template.Infrastructure.Resources/"]
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