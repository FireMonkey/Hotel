# Single image: ASP.NET Core API serving the built React app from wwwroot.
FROM node:22-alpine AS web
WORKDIR /web
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY backend/ ./
RUN dotnet publish src/Hotel.Api/Hotel.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080 \
    DataProtection__KeysPath=/keys
VOLUME ["/keys"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hotel.Api.dll"]
