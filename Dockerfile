FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY HealthPilot.sln ./
COPY src/HealthPilot.Api/HealthPilot.Api.csproj src/HealthPilot.Api/
RUN dotnet restore src/HealthPilot.Api/HealthPilot.Api.csproj

COPY src/HealthPilot.Api/. src/HealthPilot.Api/
RUN dotnet publish src/HealthPilot.Api/HealthPilot.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "HealthPilot.Api.dll"]
