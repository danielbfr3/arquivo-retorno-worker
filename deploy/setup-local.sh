#!/usr/bin/env bash
# Sobe as dependências locais e deixa uma planilha de exemplo pronta na
# pasta de entrada.
#
# A base é de outro time — este script sobe um container vazio; criar o
# schema é passo manual (ver docs/cash-cobranca-referencia.md).
set -euo pipefail

cd "$(dirname "$0")/.."

echo "==> Subindo SQL Server (CASH_COBRANCA)..."
docker compose -f docker-compose.local.yml up -d

echo "==> Copiando planilha de exemplo pra pasta de entrada..."
# O nome segue a máscara "Simplificado_{cnpj}" do appsettings, com CNPJ
# fictício. Fora do padrão, o arquivo iria pra Quarentena.
#
# Ao contrário do fluxo antigo, o worker agora ABRE a planilha (pra
# preenchê-la) — por isso o exemplo é um .xlsx de verdade, versionado em
# deploy/exemplos/, com os cabeçalhos "Documento", "Nome Cliente" e "Valor"
# na linha 1 e duas linhas de dados.
mkdir -p .dados-teste/planilhas/entrada
cp deploy/exemplos/Simplificado_12345678000199.xlsx \
  .dados-teste/planilhas/entrada/Simplificado_12345678000199.xlsx

cat <<'TXT'

==> Falta, pra rodar de ponta a ponta:

    1. Criar o schema no container local:
       - Cobranca.Arquivo         (ver docs/cash-cobranca-referencia.md §1.1)
       - Cobranca.DocumentoDados  (ver deploy/criar-tabela-documento-dados.sql)

    2. Inserir uma linha em Cobranca.DocumentoDados pro CNPJ
       12345678000199, com um Dados cujas chaves batem com os cabeçalhos
       da planilha de exemplo ("Nome Cliente", "Valor" e "Razão Social" —
       esta última é a chave reservada de
       DocumentoDados.ChaveRazaoSocial):

           INSERT INTO Cobranca.DocumentoDados (NumeroDocumento, Dados)
           VALUES (
             '12345678000199',
             N'{"Nome Cliente": "ACME DISTRIBUIDORA LTDA", "Valor": "1500.00", "Razão Social": "ACME DISTRIBUIDORA LTDA"}'
           );

       Sem essa linha, o arquivo vai pra quarentena por "documento sem
       dados"; sem a chave "Razão Social" (ou com ela vazia), vai pra
       quarentena por "cliente não encontrado".

    3. Apontar o conversor em appsettings.json — hoje está como
       TODO(a-confirmar):
       - LayoutConversaoApi:BaseUrl
       - Conversao:CampoMetadados  (nome do campo do JSON no multipart)

==> Rode o worker com:

    Origem__Pasta=$(pwd)/.dados-teste/planilhas/entrada \
      dotnet run --project src/CnabRetorno.ExcelCnab.Worker

    Dica: pra não esperar o próximo tique do cron, rode uma varredura só
    com Worker__Modo=CronJob

==> Depois de rodar, confira:

    - o arquivo sumiu de .dados-teste/planilhas/entrada;
    - apareceu em .dados-teste/planilhas/entrada/Backup/, e as colunas
      "Nome Cliente" e "Valor" já vêm preenchidas nas duas linhas de dados;
    - SELECT * FROM Cobranca.Arquivo mostra uma linha nova.

TXT
