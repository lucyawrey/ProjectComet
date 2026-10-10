# ShapeLand under a simulated network: runs programs built on the host (mounted from artifacts/, see
# compose.yaml) behind netem, so a run uses the same build as tools/shapeland-web.sh without one.
# Build context: the repository root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends iproute2 \
 && rm -rf /var/lib/apt/lists/*
COPY tools/docker/netem.sh /usr/local/bin/netem.sh
ENTRYPOINT ["/usr/local/bin/netem.sh"]
