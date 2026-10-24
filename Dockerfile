FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

COPY backend/MedicalPortal.sln backend/
COPY backend/src/MedicalPortal.Core/MedicalPortal.Core.csproj backend/src/MedicalPortal.Core/
COPY backend/src/MedicalPortal.Infrastructure/MedicalPortal.Infrastructure.csproj backend/src/MedicalPortal.Infrastructure/
COPY backend/src/MedicalPortal.Web/MedicalPortal.Web.csproj backend/src/MedicalPortal.Web/
COPY backend/tests/MedicalPortal.Tests/MedicalPortal.Tests.csproj backend/tests/MedicalPortal.Tests/

WORKDIR /source/backend
RUN dotnet restore MedicalPortal.sln

WORKDIR /source
COPY backend/ ./backend/
WORKDIR /source/backend
RUN dotnet publish src/MedicalPortal.Web/MedicalPortal.Web.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV Kestrel__Endpoints__Http__Url=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

USER app

ENTRYPOINT ["dotnet", "MedicalPortal.Web.dll"]
