#!/usr/bin/env bash
# Sobe as dependências locais dos dois robôs (SQL Server da base de
# cobrança) e deixa um arquivo V de exemplo pronto pro Robô 1 pegar.
# Nenhum dos dois robôs usa banco próprio (Postgres) — ver
# docker-compose.local.yml.
#
# Uso:
#   ./deploy/setup-local.sh
#   dotnet run --project src/CnabRetorno.RetornoCron.Worker --environment Local
#   dotnet run --project src/CnabRetorno.RetornoSubscriber.Worker --environment Local
set -euo pipefail

cd "$(dirname "$0")/.."

echo "==> Subindo SQL Server (base de cobrança)..."
docker compose -f docker-compose.local.yml up -d cobranca-sqlserver

echo "==> Gerando arquivo V de exemplo (padrão V<ClientId><sufixo>.txt)..."
mkdir -p .dados-teste/retorno-cron
cat > .dados-teste/retorno-cron/V1234567890001.txt << 'EOF'
0RETORNO EXEMPLO CNAB240
1REGISTRO DETALHE
9TRAILER
EOF

echo "==> Pronto. Falta criar o schema real da base CASH_COBRANCA"
echo "    (Cobranca.*, Titulo.*, Instrucao.* — ver docs/cash-cobranca-referencia.md)"
echo "    no cobranca-sqlserver local, e confirmar/emular a fila SQS e os"
echo "    endpoints reais das APIs de conversão e Gestor Arquivo pro Robô 2"
echo "    usar em desenvolvimento (sem emulação local ainda — ver Sqs/"
echo "    GestorArquivosApi em"
echo "    src/CnabRetorno.RetornoSubscriber.Worker/appsettings.json)."
echo
echo "==> Rode os workers com:"
echo "    DOTNET_ENVIRONMENT=Local dotnet run --project src/CnabRetorno.RetornoCron.Worker"
echo "    DOTNET_ENVIRONMENT=Local dotnet run --project src/CnabRetorno.RetornoSubscriber.Worker"
