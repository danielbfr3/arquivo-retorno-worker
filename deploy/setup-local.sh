#!/usr/bin/env bash
# Sobe as dependências locais e deixa uma planilha de exemplo pronta na
# pasta de entrada.
#
# As duas bases são de outros times — este script sobe containers vazios;
# criar o schema é passo manual (ver docs/cash-cobranca-referencia.md pro
# CASH_COBRANCA; o schema da base de adesão ainda está em aberto).
set -euo pipefail

cd "$(dirname "$0")/.."

echo "==> Subindo SQL Server (CASH_COBRANCA e base de adesão)..."
docker compose -f docker-compose.local.yml up -d

echo "==> Gerando planilha de exemplo na pasta de entrada..."
# O nome segue a máscara "Simplificado_{cnpj}" do appsettings, com CNPJ
# fictício. Fora do padrão, o arquivo iria pra Quarentena.
#
# O conteúdo não importa pro worker (ele não abre a planilha) — mas
# tampouco é um .xlsx válido, então o pipeline do conversor rejeitaria.
# Pra testar o caminho completo, troque por uma planilha de verdade com
# este nome.
mkdir -p .dados-teste/planilhas/entrada
printf 'placeholder — troque por um .xlsx real pra testar o conversor\n' \
  > .dados-teste/planilhas/entrada/Simplificado_12345678000199.xlsx

cat <<'TXT'

==> Falta, pra rodar de ponta a ponta:

    1. Criar o schema nos containers locais:
       - Cobranca.Arquivo         (ver docs/cash-cobranca-referencia.md §1.1)
       - a tabela da base de adesão com Documento e RazaoSocial
         (schema real ainda em aberto — ver Persistencia/AdesaoDbContext.cs)

    2. Inserir a linha do cliente 12345678000199 na base de adesão, senão
       o arquivo de exemplo vai direto pra Quarentena por "cliente não
       encontrado".

    3. Apontar o conversor em appsettings.json — hoje está como
       TODO(a-confirmar):
       - LayoutConversaoApi:BaseUrl
       - Conversao:CampoMetadados  (nome do campo do JSON no multipart)

    4. Decidir o que fazer com as cópias (Gestor de Arquivos + bucket S3).
       Local, o mais simples é desligar — senão cada arquivo gera dois
       erros de cópia no log (que não impedem o envio, mas poluem):

           Armazenamento__Habilitado=false

       Pra testar o destino S3 de verdade, suba um LocalStack/MinIO e
       aponte Armazenamento__S3__ServiceUrl pra ele.

    5. Mesma coisa pro aviso de conclusão no SNS:

           Notificacao__Habilitado=false

       Ou, com LocalStack, crie o tópico e aponte
       Notificacao__TopicoArn + Notificacao__ServiceUrl pra ele.

==> Rode o worker com:

    Origem__Pasta=$(pwd)/.dados-teste/planilhas/entrada \
    Armazenamento__Habilitado=false \
    Notificacao__Habilitado=false \
      dotnet run --project src/CnabRetorno.ExcelCnab.Worker

    Dica: pra não esperar o próximo tique do cron, rode uma varredura só
    com Worker__Modo=CronJob

TXT
