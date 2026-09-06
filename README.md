# arquivo-retorno-worker

Worker .NET 10 que varre uma pasta de planilhas, preenche cada uma com
dados vindos de uma tabela SQL e entrega o arquivo ao conversor de layout,
que gera o CNAB.

| Etapa | O que acontece |
|---|---|
| 1 | Varre a pasta de entrada (diretório local em dev, compartilhamento **SMB** em hml/prd) |
| 2 | Lê o CNPJ do nome do arquivo — `Simplificado_<cnpj>.xlsx` |
| 3 | Busca os dados de preenchimento em `Cobranca.DocumentoDados`, pelo mesmo CNPJ — inclui a razão social, na chave reservada `"Razão Social"` |
| 4 | Extrai a razão social do JSON — sem ela o envio é barrado |
| 5 | Abre a planilha em memória e escreve cada valor do JSON na coluna cujo cabeçalho bate, em todas as linhas de dados |
| 6 | Cria a linha do arquivo em `Cobranca.Arquivo`, com um `ArquivoID` novo — só depois que a planilha já foi preenchida com sucesso |
| 7 | Envia a planilha **já preenchida** ao **conversor assíncrono** — pipeline `excel-cnab`, appId `cash-cobranca` —, com CNPJ e razão social em JSON no corpo da mensagem |
| 8 | Grava a versão preenchida em `Backup` (ou move o original pra `Quarentena`, se algo falhou) |

A planilha **é aberta e editada** em memória antes do envio — ao contrário
do desenho anterior, que só repassava bytes opacos. Quem entende o formato
final (CNAB) continua sendo o pipeline `excel-cnab` do conversor; este
worker só preenche colunas que já existem na planilha, casando pelo nome
do cabeçalho.

O robô termina no aceite do conversor. A conclusão da conversão chega
depois por fila, correlacionada pelo `ArquivoID`, e é tratada por outro
worker do ecossistema.

## Estrutura

```
src/
  CnabRetorno.Core/                 domínio + contratos (sem PackageReference)
    Aplicacao/                      interface e DTOs da API de conversão
    Dominio/                        Arquivo, DocumentoDados
  CnabRetorno.Common/               infra compartilhada
    Http/                           base HTTP (multipart + JSON)
  CnabRetorno.ExcelCnab.Worker/     o worker
    Origem/                         pasta de entrada, backup, quarentena, leitura do nome
    Planilha/                       preenchimento da planilha (ClosedXML)
    Persistencia/                   CASH_COBRANCA (Arquivo + DocumentoDados) + lock entre réplicas
    Http/                           client do conversor de layout
    Pipeline/                       varredura e processamento de um arquivo
tests/CnabRetorno.Tests/
docs/
```

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
`deploy/setup-local.sh` — ele sobe a base vazia e deixa uma planilha
`.xlsx` de exemplo (`deploy/exemplos/`) na pasta de entrada.

Não há SQL Server nem as APIs externas neste ambiente — a verificação
possível é build + testes de unidade. Os testes cobrem as partes puras
(leitura do nome do arquivo, preenchimento da planilha em memória, JSON
enviado ao conversor, envelope de resposta) e a construção do modelo EF,
que valida o mapeamento sem abrir conexão.

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
| `Nomenclatura:Extensoes` | Extensões aceitas — padrão só `.xlsx` (o ClosedXML não abre `.xls`) |
| `Preenchimento:ComparacaoCabecalho` | `IgnorarCaixaEEspacos` (padrão) ou `Exata` — como o cabeçalho da planilha é comparado com as chaves do JSON |
| `Preenchimento:NomeAba` | Aba a preencher — vazio usa a primeira |
| `Conversao:Pipeline` | Pipeline do conversor — `excel-cnab` |
| `Conversao:AppId` | AppID da chamada — `cash-cobranca` |
| `Conversao:CampoMetadados` | Campo do multipart que leva o JSON do cliente (**em aberto**) |
| `LayoutConversaoApi:BaseUrl` | Base URL do conversor (**em aberto**) |
| `RegistroArquivo:AppId` / `CriadoPor` | O que é gravado na linha de `Cobranca.Arquivo` |
| `Worker:Modo` / `Cron` | `Loop` (residente, com cron interno) ou `CronJob` (roda e encerra) |
| `Pipeline:MaxArquivosConcorrentes` | Quantos arquivos em paralelo — um escopo de DI por arquivo |
| `ConnectionStrings:Cobranca` | CASH_COBRANCA — `Cobranca.Arquivo` (escrita) e `Cobranca.DocumentoDados` (leitura, inclui a razão social) |

## Antes de homologação

O código roda, mas há dados de integração que ninguém confirmou. Estão
marcados com `TODO(a-confirmar)` e listados em
[`docs/regras-de-negocio.md`](docs/regras-de-negocio.md#em-aberto). Os
mais críticos:

- **Schema de `Cobranca.DocumentoDados`** — tabela nova, dona de outro
  sistema; ver `deploy/criar-tabela-documento-dados.sql`. Formato exato de
  `NumeroDocumento` (14 dígitos sem pontuação?) ainda não confirmado. É
  caminho crítico de **todo** arquivo: sem a chave `"Razão Social"` no
  JSON, nada é enviado.
- **Nome do campo de metadados** no multipart — `metadata` é suposição. Um
  campo que o conversor não conhece é ignorado em silêncio, e o upload
  ainda assim é aceito.
- **Valores numéricos de `ArquivoStatus`/`ArquivoEtapa`** — os nomes são
  reais, os números são suposição, e a tabela é compartilhada com o
  ecossistema CASH inteiro.
- **Base URL e autenticação** do conversor.
- **`.xls` ainda chega em produção?** Se sim, o ClosedXML precisa ser
  trocado por NPOI — ver `docs/riscos-conhecidos.md`.

## Documentação

- [`docs/regras-de-negocio.md`](docs/regras-de-negocio.md) — o que o robô
  faz, com diagrama e o porquê de cada decisão.
- [`docs/riscos-conhecidos.md`](docs/riscos-conhecidos.md) — auditoria de
  riscos de comportamento.
- [`docs/cash-cobranca-referencia.md`](docs/cash-cobranca-referencia.md) —
  schema do CASH_COBRANCA e contratos das APIs externas.
- [`docs/conceitos-dotnet-ef-core.md`](docs/conceitos-dotnet-ef-core.md) —
  como o código usa .NET e EF Core.
