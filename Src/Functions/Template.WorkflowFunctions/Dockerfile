# Phase 31 (ADR-0003): the WorkflowFunctions host on the official Azure Functions 4.x .NET isolated image for .NET 10
# (host 4.1054.250.10 at pinning time), pinned by digest. It listens on port 80 inside the private Compose network only;
# no port is published and the internal HTTP boundary requires the shared key.
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0@sha256:2fc73bed0c1377deb3fbb24be75b210b54b0931996312ce68b56adb23ab8d279 AS base

FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["global.json", "Directory.Packages.props", "./"]
COPY ["Src/Functions/Template.WorkflowFunctions/Template.WorkflowFunctions.csproj", "Src/Functions/Template.WorkflowFunctions/"]
COPY ["Src/Core/Template.Application/Template.Application.csproj", "Src/Core/Template.Application/"]
COPY ["Src/Core/Template.Domain/Template.Domain.csproj", "Src/Core/Template.Domain/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Identity/Template.Infrastructure.Identity.csproj", "Src/Infrastructure/Template.Infrastructure.Identity/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Persistence/Template.Infrastructure.Persistence.csproj", "Src/Infrastructure/Template.Infrastructure.Persistence/"]
RUN dotnet restore "Src/Functions/Template.WorkflowFunctions/Template.WorkflowFunctions.csproj"
COPY . .
WORKDIR "/src/Src/Functions/Template.WorkflowFunctions"
RUN dotnet publish "Template.WorkflowFunctions.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated \
    # The Durable client binding needs a host name outside Azure; it is the container's own address, never published.
    WEBSITE_HOSTNAME=localhost:80
COPY --from=build /app/publish /home/site/wwwroot
