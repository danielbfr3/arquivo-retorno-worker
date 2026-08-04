FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/CnabRetorno.PagamentoRetorno.Worker/CnabRetorno.PagamentoRetorno.Worker.csproj \
    -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "CnabRetorno.PagamentoRetorno.Worker.dll"]
