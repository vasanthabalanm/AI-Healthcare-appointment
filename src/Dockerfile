# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy all project files
COPY ClinicalHealthcare.Api/ClinicalHealthcare.Api.csproj ClinicalHealthcare.Api/
COPY ClinicalHealthcare.Infrastructure/ClinicalHealthcare.Infrastructure.csproj ClinicalHealthcare.Infrastructure/
COPY ClinicalHealthcare.Infrastructure.PgMigrations/ClinicalHealthcare.Infrastructure.PgMigrations.csproj ClinicalHealthcare.Infrastructure.PgMigrations/

# Restore dependencies
RUN dotnet restore ClinicalHealthcare.Api/ClinicalHealthcare.Api.csproj

# Copy everything and build
COPY . .
RUN dotnet publish ClinicalHealthcare.Api/ClinicalHealthcare.Api.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Render uses PORT environment variable
ENV ASPNETCORE_URLS=http://+:${PORT:-10000}
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 10000
ENTRYPOINT ["dotnet", "ClinicalHealthcare.Api.dll"]
