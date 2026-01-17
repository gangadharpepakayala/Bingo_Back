FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj from current folder
COPY ["Bingo_Back.csproj", "./"]
RUN dotnet restore "Bingo_Back.csproj"

COPY . .
RUN dotnet build "Bingo_Back.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Bingo_Back.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Bingo_Back.dll"]
