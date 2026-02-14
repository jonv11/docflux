FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore src/DocFlux.Cli/DocFlux.Cli.csproj
RUN dotnet publish src/DocFlux.Cli/DocFlux.Cli.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

COPY --from=build /app/publish/ ./
ENTRYPOINT ["dotnet", "docflux.dll"]
