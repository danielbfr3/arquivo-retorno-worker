FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/CnabRetorno.RetornoSubscriber.Worker/CnabRetorno.RetornoSubscriber.Worker.csproj \
    -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "CnabRetorno.RetornoSubscriber.Worker.dll"]
