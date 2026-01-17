# Use the official .NET runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["Bingo_Back/Bingo_Back.csproj", "Bingo_Back/"]
RUN dotnet restore "Bingo_Back/Bingo_Back.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/Bingo_Back"
RUN dotnet build "Bingo_Back.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Bingo_Back.csproj" -c Release -o /app/publish

# Final runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Bingo_Back.dll"]

