FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["Dns/Dns.csproj", "Dns/"]
COPY ["Dns.Cli/Dns.Cli.csproj", "Dns.Cli/"]
RUN dotnet restore "Dns.Cli/Dns.Cli.csproj"
COPY . .
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
EXPOSE 80/tcp
