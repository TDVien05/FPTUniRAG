FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["FPTUniRAG/FPTUniRAG.csproj", "FPTUniRAG/"]
COPY ["FPTUniRAG.BusinessLayer/FPTUniRAG.BusinessLayer.csproj", "FPTUniRAG.BusinessLayer/"]
COPY ["FPTUniRAG.DataAccessLayer/FPTUniRAG.DataAccessLayer.csproj", "FPTUniRAG.DataAccessLayer/"]
RUN dotnet restore "FPTUniRAG/FPTUniRAG.csproj"

COPY . .
RUN dotnet publish "FPTUniRAG/FPTUniRAG.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-eng \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
RUN mkdir -p /app/.keys /app/App_Data/teacher-uploads \
    && chown -R app:app /app/.keys /app/App_Data

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

USER app
ENTRYPOINT ["dotnet", "FPTUniRAG.dll"]
