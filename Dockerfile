# ---- Portal (React) ----
FROM node:22-alpine AS portal
WORKDIR /portal
COPY portal/package*.json ./
RUN npm ci
COPY portal/ .
RUN npm run build

# ---- API (.NET) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY PdfFillPoc.slnx .
COPY src/PdfFill.Api/PdfFill.Api.csproj src/PdfFill.Api/
COPY tests/PdfFill.Api.Tests/PdfFill.Api.Tests.csproj tests/PdfFill.Api.Tests/
RUN dotnet restore
COPY . .
RUN dotnet test -c Release --no-restore
RUN dotnet publish src/PdfFill.Api/PdfFill.Api.csproj -c Release -o /app --no-restore

# ---- Runtime: one container serves the API and the portal ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
COPY --from=portal /portal/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080 \
    Database__File=/data/poc.db \
    Database__RecreateOnStartup=false
VOLUME ["/data"]
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PdfFill.Api.dll"]
