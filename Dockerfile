# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

COPY . .

RUN dotnet restore revix.sln
RUN dotnet publish src/revix.API/revix.API.csproj -c Release -o /app/out /p:ErrorOnDuplicatePublishOutputFiles=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .

EXPOSE 5001
ENTRYPOINT ["dotnet", "revix.API.dll"]