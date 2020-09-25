FROM mcr.microsoft.com/dotnet/runtime:5.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY CorpinatorBot/CorpinatorBot.csproj CorpinatorBot/
COPY .nuget/nuget.config .nuget/nuget.config
RUN dotnet restore CorpinatorBot/CorpinatorBot.csproj --configfile .nuget/nuget.config

COPY . .
WORKDIR src/CorpinatorBot
RUN dotnet build CorpinatorBot.csproj -c Release -o /app

FROM build AS publish
RUN dotnet publish CorpinatorBot.csproj -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "CorpinatorBot.dll"]
