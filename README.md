# arquivo-retorno-worker

Worker .NET 10 que varre uma pasta de planilhas, identifica o cliente dono
de cada uma e entrega o arquivo ao conversor de layout, que gera o CNAB.

| Etapa | O que acontece |
|---|---|
| 1 | Varre a pasta de entrada (diretório local em dev, compartilhamento **SMB** em hml/prd) |
| 2 | Lê o CNPJ do nome do arquivo — `Simplificado_<cnpj>.xlsx` ou `.xls` |
| 3 | Busca o cliente na base de **adesão** pra pegar a razão social |
| 4 | Cria a linha do arquivo em `Cobranca.Arquivo`, com um `ArquivoID` novo |
| 5 | Guarda uma cópia da planilha no **Gestor de Arquivos e no bucket S3** (os dois ao mesmo tempo) |
| 6 | Envia a planilha ao **conversor assíncrono** — pipeline `excel-cnab`, appId `cash-cobranca` —, com CNPJ e razão social em JSON no corpo da mensagem |
| 7 | Move o arquivo pra `Backup` (ou pra `Quarentena`, se algo falhou) |
| 8 | Publica no **tópico SNS** um aviso de que terminou o processamento daquela planilha |

A planilha **não é aberta** em momento nenhum: o CNPJ vem do nome e o
conteúdo é repassado como bytes opacos pro pipeline, que é quem entende o
formato. Por isso não há biblioteca de Excel no projeto.

O robô termina no aceite do conversor. A conclusão da conversão chega
depois por fila, correlacionada pelo `ArquivoID`, e é tratada por outro
worker do ecossistema.

## Estrutura

```
src/
  CnabRetorno.Core/                 domínio + contratos (sem PackageReference)
    Aplicacao/                      interface e DTOs da API de conversão
    Dominio/                        Arquivo, EmpresaAdesao, enums de status
  CnabRetorno.Common/               infra compartilhada
    Http/                           base HTTP (multipart + JSON)
  CnabRetorno.ExcelCnab.Worker/     o worker
    Origem/                         pasta de entrada, backup, quarentena, leitura do nome
    Persistencia/                   CASH_COBRANCA + base de adesão + lock entre réplicas
    Http/                           client do conversor de layout
    Armazenamento/                  cópias no Gestor de Arquivos + S3 (recurso destacável)
    Notificacao/                    aviso de conclusão no SNS (recurso destacável)
    Pipeline/                       varredura e processamento de um arquivo
tests/CnabRetorno.Tests/
docs/
```

As pastas `Armazenamento/` e `Notificacao/` são autocontidas de propósito
— contrato, implementações e registro no DI moram nelas, e nada em
`Core`/`Common` sabe que storage ou mensageria existem. Desligar cada uma
é uma chave de configuração; remover é apagar a pasta e mais quatro
linhas, listadas em
[`docs/regras-de-negocio.md`](docs/regras-de-negocio.md#como-desativar--como-remover).

`Core` e `Common` continuam separados do worker de propósito: `Core`
modela o futuro `arquivo-core-lib` (domínio e contratos, zero dependência
externa) e `Common` é a infraestrutura que um segundo worker da família
reusaria.

## Rodando

```bash
dotnet build CnabRetorno.slnx
dotnet test  CnabRetorno.slnx

dotnet run --project src/CnabRetorno.ExcelCnab.Worker
```

Pra experimentar localmente sem SQL Server nem o conversor, veja
`deploy/setup-local.sh` — ele sobe as duas bases vazias e deixa uma
planilha de exemplo na pasta de entrada.

Não há SQL Server nem as APIs externas neste ambiente — a verificação
possível é build + testes de unidade. Os testes cobrem as partes puras
(leitura do nome do arquivo, JSON enviado ao conversor, envelope de
resposta) e a construção do modelo EF, que valida o mapeamento sem abrir
conexão.

## Configuração

Tudo que é nome de recurso de infra é configuração — pasta de origem,
máscara do nome, base URL, AppID, nome do pipeline. Nada literal em
código. Em cluster, sobrescrever por variável de ambiente com `__` no
lugar de `:` (ex.: `Origem__Pasta`).

| Chave | Para quê |
|---|---|
| `Origem:Pasta` | Pasta de entrada (ponto de montagem do SMB em hml/prd) |
| `Origem:SegundosEstabilidade` | Ignora arquivo ainda sendo copiado pra pasta |
| `Nomenclatura:Mascara` | Máscara do nome, com o token `{cnpj}` — padrão `Simplificado_{cnpj}` |
| `Nomenclatura:Extensoes` | Extensões aceitas — padrão `.xlsx` e `.xls` |
| `Conversao:Pipeline` | Pipeline do conversor — `excel-cnab` |
| `Conversao:AppId` | AppID da chamada — `cash-cobranca` |
| `Conversao:CampoMetadados` | Campo do multipart que leva o JSON do cliente (**em aberto**) |
| `Armazenamento:Habilitado` | Chave-mestra das cópias — `false` desliga os dois destinos |
| `Armazenamento:FalhaBloqueiaEnvio` | Cópia que falha impede o envio ao conversor? Padrão `false` |
| `Armazenamento:GestorArquivos:*` | `Habilitado`, `AppId`, `BaseUrl`, `ApiKey` — presign + PUT |
| `Armazenamento:S3:*` | `Habilitado`, `Bucket`, `Prefixo`, `Region`, `ServiceUrl` — `PutObject` direto |
| `Notificacao:Habilitado` | Liga/desliga o aviso de conclusão no SNS |
| `Notificacao:TopicoArn` | ARN do tópico (**em aberto**) |
| `Notificacao:Region` / `ServiceUrl` / `Assunto` | Região, endpoint alternativo (LocalStack) e assunto da mensagem |
| `LayoutConversaoApi:BaseUrl` | Base URL do conversor (**em aberto**) |
| `RegistroArquivo:AppId` / `CriadoPor` | O que é gravado na linha de `Cobranca.Arquivo` |
| `Worker:Modo` / `Cron` | `Loop` (residente, com cron interno) ou `CronJob` (roda e encerra) |
| `Pipeline:MaxArquivosConcorrentes` | Quantos arquivos em paralelo — um escopo de DI por arquivo |
| `ConnectionStrings:Cobranca` | CASH_COBRANCA |
| `ConnectionStrings:Adesao` | Base de adesão (**schema inteiro em aberto**) |

## Antes de homologação

O código roda, mas há dados de integração que ninguém confirmou. Estão
marcados com `TODO(a-confirmar)` e listados em
[`docs/regras-de-negocio.md`](docs/regras-de-negocio.md#em-aberto). Os
mais críticos:

- **Schema da base de adesão** — nome de schema, tabela e colunas são
  chute; a base nunca foi inspecionada. É caminho crítico de **todo**
  arquivo: sem razão social nada é enviado.
- **Nome do campo de metadados** no multipart — `metadata` é suposição. Um
  campo que o conversor não conhece é ignorado em silêncio, e o upload
  ainda assim é aceito.
- **Valores numéricos de `ArquivoStatus`/`ArquivoEtapa`** — os nomes são
  reais, os números são suposição, e a tabela é compartilhada com o
  ecossistema CASH inteiro.
- **Base URL e autenticação** do conversor.
- **Bucket S3 e base URL do Gestor de Arquivos** — sem eles as cópias
  falham. Por padrão isso **não** impede a conversão: sai erro no log e o
  arquivo segue. Ligue `Armazenamento:FalhaBloqueiaEnvio` se a cópia for
  requisito, ou `Armazenamento:Habilitado=false` até os valores existirem.
- **ARN do tópico SNS** — sem ele o aviso falha. Nunca derruba o
  processamento (o arquivo já foi enviado quando o aviso acontece); use
  `Notificacao:Habilitado=false` até o tópico existir.

## Documentação

- [`docs/regras-de-negocio.md`](docs/regras-de-negocio.md) — o que o robô
  faz, com diagrama e o porquê de cada decisão.
- [`docs/riscos-conhecidos.md`](docs/riscos-conhecidos.md) — auditoria de
  riscos de comportamento.
- [`docs/cash-cobranca-referencia.md`](docs/cash-cobranca-referencia.md) —
  schema do CASH_COBRANCA e contratos das APIs externas.
- [`docs/conceitos-dotnet-ef-core.md`](docs/conceitos-dotnet-ef-core.md) —
  como o código usa .NET e EF Core.
