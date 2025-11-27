# Étape 1 : Build de l'application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["PrintClient.csproj", "./"]
RUN dotnet restore "PrintClient.csproj"
COPY . .
RUN dotnet publish "PrintClient.csproj" -c Release -o /app/publish

# Étape 2 : Création de l'image finale (Runtime)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app

# --- PARTIE CRITIQUE ---
# On installe le client CUPS pour avoir la commande 'lp'
# On installe aussi 'libgdiplus' si jamais vous manipulez des images avant impression
RUN apt-get update && \
    apt-get install -y cups-client libgdiplus && \
    rm -rf /var/lib/apt/lists/*
# -----------------------

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PrintClient.dll"]