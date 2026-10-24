FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

COPY MedicalPortal.sln .
COPY src/MedicalPortal.Core/MedicalPortal.Core.csproj src/MedicalPortal.Core/
COPY src/MedicalPortal.Infrastructure/MedicalPortal.Infrastructure.csproj src/MedicalPortal.Infrastructure/
COPY src/MedicalPortal.Web/MedicalPortal.Web.csproj src/MedicalPortal.Web/
COPY tests/MedicalPortal.Tests/MedicalPortal.Tests.csproj tests/MedicalPortal.Tests/

RUN dotnet restore

COPY . .
RUN dotnet test tests/MedicalPortal.Tests/MedicalPortal.Tests.csproj -c Release
RUN dotnet publish src/MedicalPortal.Web/MedicalPortal.Web.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "MedicalPortal.Web.dll"]
