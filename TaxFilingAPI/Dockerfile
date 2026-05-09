# ─────────────────────────────────────────
# STAGE 1: BUILD
# Use the full SDK image to compile the app
# ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies first
# This layer is cached — only re-runs if .csproj changes
COPY ["TaxFilingAPI.csproj", "."]
RUN dotnet restore "./TaxFilingAPI.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/."
RUN dotnet build "TaxFilingAPI.csproj" -c Release -o /app/build

# ─────────────────────────────────────────
# STAGE 2: PUBLISH
# Create optimized production output
# ─────────────────────────────────────────
FROM build AS publish
RUN dotnet publish "TaxFilingAPI.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ─────────────────────────────────────────
# STAGE 3: RUNTIME
# Use lightweight runtime image — no SDK bloat
# ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Set working directory
WORKDIR /app

# Create non-root user for security
# Running as root in containers is a security risk
RUN useradd --no-create-home --shell /bin/false appuser && \
    chown -R appuser /app
USER appuser

# Copy published output from publish stage
COPY --from=publish /app/publish .

# Environment configuration
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

# Expose port
EXPOSE 8080

# Health check — Kubernetes uses this to know if pod is ready
HEALTHCHECK --interval=30s \
            --timeout=10s \
            --start-period=40s \
            --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1

# Start the application
ENTRYPOINT ["dotnet", "TaxFilingAPI.dll"]