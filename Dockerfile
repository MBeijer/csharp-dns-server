FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM node:22-alpine AS spa-build
WORKDIR /src/Dns.Spa
COPY ["Dns.Spa/package.json", "Dns.Spa/package-lock.json", "./"]
RUN npm ci
COPY ["Dns.Spa/", "./"]
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS setup
WORKDIR /src
COPY ["Dns/Dns.csproj", "Dns/"]
COPY ["Dns.Cli/Dns.Cli.csproj", "Dns.Cli/"]
COPY ["Dns.Db/Dns.Db.csproj", "Dns.Db/"]
RUN dotnet restore "Dns.Cli/Dns.Cli.csproj"
COPY . .
COPY --from=spa-build /src/Dns.Spa/dist /src/Dns.Spa/dist

FROM setup AS build
WORKDIR "/src/Dns.Cli"
RUN dotnet build "Dns.Cli.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Dns.Cli.csproj" -c Release -o /app/publish

FROM base AS final
RUN useradd -ms /bin/bash dns

USER dns
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS="http://*:80"
ENTRYPOINT ["dotnet", "Dns.Cli.dll"]
EXPOSE 5335/udp
EXPOSE 5335/tcp
EXPOSE 80/tcp