FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ProductServiceApi/ProductServiceApi/*.csproj ProductServiceApi/
COPY ProductServiceApi/ProductApi.Service/*.csproj ProductApi.Service/
COPY ProductServiceApi/ProductServiceApi.Data/*.csproj ProductServiceApi.Data/
COPY ProductServiceApi/ProductServiceApi.Model/*.csproj ProductServiceApi.Model/
RUN dotnet restore ProductServiceApi/ProductServiceApi.csproj
COPY ProductServiceApi/ .
RUN dotnet publish ProductServiceApi/ProductServiceApi.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ProductServiceApi.dll"]
