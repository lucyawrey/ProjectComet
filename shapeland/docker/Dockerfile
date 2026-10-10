# ShapeLand under a simulated network: runs programs built on the host behind netem, with the programs, the
# content, the web build and tools/docker/netem.sh all mounted (compose.yaml), so a run uses the same build as
# tools/shapeland-web.sh without one, and the image is only the runtime and tc.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends iproute2 \
 && rm -rf /var/lib/apt/lists/*
