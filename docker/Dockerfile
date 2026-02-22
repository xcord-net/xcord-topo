# Runtime-only image — host builds backend + frontend, this just copies artifacts in
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine
WORKDIR /app

# Install Terraform and SSH client
RUN apk add --no-cache wget unzip openssh-client && \
    wget -q https://releases.hashicorp.com/terraform/1.9.8/terraform_1.9.8_linux_amd64.zip -O /tmp/terraform.zip && \
    unzip /tmp/terraform.zip -d /usr/local/bin/ && \
    rm /tmp/terraform.zip && \
    chmod +x /usr/local/bin/terraform && \
    apk del wget unzip

# Create non-root user
RUN addgroup -g 1001 xcord-topo && \
    adduser -u 1001 -G xcord-topo -s /bin/sh -D xcord-topo

# Create data directories
RUN mkdir -p /data/topologies /data/terraform /data/credentials

# Copy pre-built backend publish output
COPY .build/publish/ .

# Copy pre-built SPA
COPY .build/wwwroot/ ./wwwroot/

# Seed example topologies
COPY docker/data/topologies/ /data/topologies/

# Set ownership
RUN chown -R xcord-topo:xcord-topo /data /app

USER xcord-topo

EXPOSE 80

ENV ASPNETCORE_URLS=http://+:80
ENV Data__BasePath=/data

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:80/health || exit 1

ENTRYPOINT ["dotnet", "XcordTopo.Api.dll"]
