FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY global.json ./
COPY CPCREDO.sln ./
COPY src/CPCREDO.Domain/CPCREDO.Domain.csproj src/CPCREDO.Domain/
COPY src/CPCREDO.Application/CPCREDO.Application.csproj src/CPCREDO.Application/
COPY src/CPCREDO.Infrastructure/CPCREDO.Infrastructure.csproj src/CPCREDO.Infrastructure/
COPY src/CPCREDO.WebApi/CPCREDO.WebApi.csproj src/CPCREDO.WebApi/
RUN dotnet restore src/CPCREDO.WebApi/CPCREDO.WebApi.csproj
COPY src/ src/
RUN dotnet publish src/CPCREDO.WebApi/CPCREDO.WebApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
ENV TZ=America/Port-au-Prince
EXPOSE 8080
ENTRYPOINT ["dotnet", "CPCREDO.WebApi.dll"]
