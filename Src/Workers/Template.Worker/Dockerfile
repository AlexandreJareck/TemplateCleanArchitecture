FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["global.json", "Directory.Packages.props", "./"]
COPY ["Src/Workers/Template.Worker/Template.Worker.csproj", "Src/Workers/Template.Worker/"]
COPY ["Src/Core/Template.Application/Template.Application.csproj", "Src/Core/Template.Application/"]
COPY ["Src/Core/Template.Domain/Template.Domain.csproj", "Src/Core/Template.Domain/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Files/Template.Infrastructure.Files.csproj", "Src/Infrastructure/Template.Infrastructure.Files/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Messaging/Template.Infrastructure.Messaging.csproj", "Src/Infrastructure/Template.Infrastructure.Messaging/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Persistence/Template.Infrastructure.Persistence.csproj", "Src/Infrastructure/Template.Infrastructure.Persistence/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Reporting/Template.Infrastructure.Reporting.csproj", "Src/Infrastructure/Template.Infrastructure.Reporting/"]
COPY ["Src/Infrastructure/Template.Infrastructure.Workflow/Template.Infrastructure.Workflow.csproj", "Src/Infrastructure/Template.Infrastructure.Workflow/"]
RUN dotnet restore "Src/Workers/Template.Worker/Template.Worker.csproj"
COPY . .
WORKDIR "/src/Src/Workers/Template.Worker"
# PlaywrightPlatform=linux copies only the Linux Node driver of Microsoft.Playwright into the output.
RUN dotnet publish "Template.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:PlaywrightPlatform=linux

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
# Phase 30 (D23-5): the headless Chromium shell pinned by Microsoft.Playwright 1.64.1 (browser revision 1248) and its OS
# dependencies, installed by the package's own driver at build time into a read-only shared path; the process runs non-root,
# so the driver (published with owner-only execute permission) and the browser are made readable and executable by everyone.
USER root
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN /app/.playwright/node/linux-x64/node /app/.playwright/package/cli.js install --with-deps --only-shell chromium \
    && rm -rf /var/lib/apt/lists/* \
    && chmod -R a+rX /ms-playwright /app/.playwright
USER $APP_UID
ENTRYPOINT ["dotnet", "Template.Worker.dll"]
