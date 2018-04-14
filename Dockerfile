FROM microsoft/dotnet:2.0-sdk AS build
WORKDIR /app
COPY backend /app
RUN dotnet publish -f netcoreapp2.0 -c Release

FROM microsoft/dotnet:2.0-runtime AS runtime
WORKDIR /app
COPY --from=build /app/bin/Release/netcoreapp2.0/publish ./
ENTRYPOINT ["dotnet", "SteamDatabaseBackend.dll"]
