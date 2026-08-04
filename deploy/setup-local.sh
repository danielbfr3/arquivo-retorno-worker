#!/usr/bin/env bash
# Sobe as dependências locais dos dois robôs e deixa um arquivo de remessa
# de exemplo pronto pro Robô 1 pegar.
#
# As duas bases são de outros times — este script sobe containers vazios;
# criar o schema real é passo manual (ver docs/cash-cobranca-referencia.md
# e docs/pagamento-referencia.md).
set -euo pipefail

cd "$(dirname "$0")/.."

echo "==> Subindo SQL Server (CASH_COBRANCA e ASA_CASH_PAGAMENTO)..."
docker compose -f docker-compose.local.yml up -d

echo "==> Gerando remessa de exemplo na pasta de entrada das VANs..."
# O nome segue a máscara "CB{cnpj}DDMMYY.*" do appsettings, com CNPJ
# fictício. Sem casar com máscara nenhuma, o arquivo iria pra Quarentena.
mkdir -p .dados-teste/vans/entrada
printf '%-240s\n' "0" "1" "3" "5" "9" > .dados-teste/vans/entrada/CB12345678000199030826.C01.rem

cat <<'TXT'

==> Falta, pra rodar de ponta a ponta:

    1. Criar o schema das duas bases nos containers locais:
       - Cobranca.Arquivo e Cobranca.Parametro   (Robô 1)
       - Pagamento.* (5 duplas + Arquivo + Parametro)  (Robô 2)
       Ver docs/cash-cobranca-referencia.md e docs/pagamento-referencia.md.

    2. Rodar deploy/pagamento-controle-janela.sql no pagamento-sqlserver
       (tabela de marca d'água + coluna SequencialAtual).

    3. Apontar as APIs externas em appsettings.json — hoje estão como
       TODO(a-confirmar):
       - GestorArquivosApi:BaseUrl   (os dois robôs)
       - LayoutConversaoApi:BaseUrl  (Robô 2)
       - Conversao:Pipeline          (Robô 2 — nome do pipeline)

==> Rode os workers com:

    Origem__Pasta=$(pwd)/.dados-teste/vans/entrada \
      dotnet run --project src/CnabRetorno.RemessaVan.Worker

    dotnet run --project src/CnabRetorno.PagamentoRetorno.Worker

    Dica pro Robô 2: pra não esperar até as 7h, reduza a janela com
    Janela__HoraInicio=00:00:00 Janela__IntervaloParcial=00:05:00

TXT
