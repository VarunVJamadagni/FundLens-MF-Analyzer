# ================================
# Build stage
# ================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Copy API project
COPY InvestmentPlanner.Api/InvestmentPlanner.Api.csproj InvestmentPlanner.Api/

# Restore dependencies
RUN dotnet restore InvestmentPlanner.Api/InvestmentPlanner.Api.csproj

# Copy the API source
COPY InvestmentPlanner.Api/ InvestmentPlanner.Api/

# Copy the shared Data folder
COPY Data/ Data/

# Build and publish
RUN dotnet publish InvestmentPlanner.Api/InvestmentPlanner.Api.csproj \
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

# Copy Data folder into the container
COPY --from=build /src/Data ./Data

# Tell the application where the data lives
ENV DataDirectory=/app/Data

# Render will provide the PORT environment variable
ENV ASPNETCORE_URLS=http://+:10000

# Start API
ENTRYPOINT ["dotnet", "InvestmentPlanner.Api.dll"]