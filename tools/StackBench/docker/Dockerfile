# Stack benchmark images. Build context: the repository root (see compose.yaml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ProjectComet.slnx ./
COPY comet/src comet/src
COPY tools/StackBench tools/StackBench
RUN dotnet publish tools/StackBench/StackBench.Server -c Release -o /out/server \
 && dotnet publish tools/StackBench/StackBench.Bots -c Release -o /out/bots

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS server
WORKDIR /app
COPY --from=build /out/server .
ENV ASPNETCORE_URLS=http://+:5080
EXPOSE 5080
ENTRYPOINT ["dotnet", "StackBench.Server.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS bots
RUN apt-get update \
 && apt-get install -y --no-install-recommends iproute2 \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/bots .
COPY tools/docker/netem.sh /usr/local/bin/netem.sh
ENTRYPOINT ["/usr/local/bin/netem.sh", "dotnet", "StackBench.Bots.dll"]
