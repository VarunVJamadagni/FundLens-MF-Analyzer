# ================================
# Build stage
# ================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Copy Web project
COPY InvestmentPlanner.Web/InvestmentPlanner.Web.csproj InvestmentPlanner.Web/

# Restore dependencies
RUN dotnet restore InvestmentPlanner.Web/InvestmentPlanner.Web.csproj

# Copy Web source
COPY InvestmentPlanner.Web/ InvestmentPlanner.Web/

# Build and publish
RUN dotnet publish InvestmentPlanner.Web/InvestmentPlanner.Web.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false


# ================================
# Runtime stage
# ================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app

# Copy published application
COPY --from=build /app/publish .

# Render will provide the PORT environment variable
ENV ASPNETCORE_URLS=http://+:${PORT}

# Start Web application
ENTRYPOINT ["dotnet", "InvestmentPlanner.Web.dll"]