# arquivo-retorno-worker

Dois workers .NET 10, independentes entre si:

| Robô | Projeto | O que faz |
|---|---|---|
| **1** | `CnabRetorno.RemessaVan.Worker` | Varre a pasta onde as VANs depositam remessas CNAB, renomeia no padrão ASA, guarda no Gestor de Arquivos (ou no S3) e registra em `Cobranca.Arquivo`. |
| **2** | `CnabRetorno.PagamentoRetorno.Worker` | Das 7h às 18h, gera arquivos de retorno de pagamentos — parciais de hora em hora e um consolidado no fim do dia —, guardados e registrados em `Pagamento.Arquivo`. CNAB via conversor externo (padrão) ou escrito pelo próprio robô, ver `Geracao:Modo`. |

Os dois não conversam. Compartilham `CnabRetorno.Core` (domínio e
contratos, zero dependência externa) e `CnabRetorno.Common`
(infraestrutura: HTTP e storage).

## Estrutura

```
src/
  CnabRetorno.Core/                 domínio + contratos (sem PackageReference)
    Aplicacao/                      interfaces e DTOs das APIs externas
    Cnab240/                        leitura posicional do layout FEBRABAN 240
    Dominio/                        Arquivo, MovimentacaoPagamento, enums
  CnabRetorno.Common/               infra compartilhada
    Http/                           base HTTP + client do Gestor de Arquivos
    Storage/                        upload via presigned URL ou S3 direto
  CnabRetorno.RemessaVan.Worker/    Robô 1
    Vans/                           máscaras das VANs, nome padrão ASA
    Origem/                         pasta de entrada, backup, quarentena
    Persistencia/                   CASH_COBRANCA
    Pipeline/
  CnabRetorno.PagamentoRetorno.Worker/  Robô 2
    Agendamento/                    grade de janelas 7h–18h
    Persistencia/                   ASA_CASH_PAGAMENTO (UNION dos 5 meios) + ASA_CASH_ADESAO
    Json/                           parse da remessa gravada + montagem do JSON
    Cnab/                           gerador de CNAB: via conversor ou direto (Geracao:Modo)
    Http/                           conversor síncrono
    Pipeline/
tests/CnabRetorno.Tests/
deploy/                             DDL da tabela de controle + setup local
docs/
```

## Rodando

```bash
dotnet build CnabRetorno.slnx
dotnet test  CnabRetorno.slnx

dotnet run --project src/CnabRetorno.RemessaVan.Worker
dotnet run --project src/CnabRetorno.PagamentoRetorno.Worker
```

Não há SQL Server nem as APIs externas neste ambiente — a verificação
possível é build + testes de unidade. Os testes cobrem as partes puras
(máscaras, nomenclatura, grade de janelas, montagem do JSON, parse do CNAB
gravado) e a construção do modelo EF, que valida o mapeamento sem abrir
conexão.

## Configuração

Tudo que é nome de recurso de infra é configuração — pasta de origem,
bucket, prefixo, base URLs, AppIDs, templates de nome, horários. Nada
literal em código. Em cluster, sobrescrever por variável de ambiente com
`__` no lugar de `:` (ex.: `Storage__S3__Bucket`).

### Robô 1 — o essencial

| Chave | Para quê |
|---|---|
| `Origem:Pasta` | Pasta das VANs (SMB montado no pod em produção) |
| `Origem:SegundosEstabilidade` | Ignora arquivo ainda sendo gravado |
| `Vans:Mascaras` | Máscaras por VAN — ver `docs/regras-de-negocio.md` |
| `Nomenclatura:Template` | Padrão ASA, com tokens |
| `Storage:Modo` | `GestorArquivos` (padrão) ou `S3` |
| `Storage:S3:Bucket` / `Prefixo` | Destino no modo S3 |
| `ConnectionStrings:Cobranca` | CASH_COBRANCA |

### Robô 2 — o essencial

| Chave | Para quê |
|---|---|
| `Janela:HoraInicio` / `HoraFim` / `IntervaloParcial` | Grade de geração |
| `Janela:FusoHorario` | Sem isso, o "arquivo das 7h" sai às 4h num pod em UTC |
| `Janela:TimestampsBancoEmUtc` | Em que fuso a base grava os timestamps (**em aberto** — errado desloca o corte em 3h) |
| `Geracao:Modo` | `Conversor` (padrão) ou `CnabDireto` — ver `docs/pagamento-referencia.md` §6 |
| `Conversao:Pipeline` | Pipeline do conversor (**em aberto**, só usado em `Geracao:Modo=Conversor`) |
| `ConnectionStrings:Adesao` | ASA_CASH_ADESAO — só usada em `Geracao:Modo=CnabDireto` (**schema inteiro em aberto**) |
| `Retorno:CodigoBanco` / `TipoServico` | Header do arquivo |
| `Storage:Modo` | `GestorArquivos` (padrão) ou `S3` |
| `ConnectionStrings:Pagamento` | ASA_CASH_PAGAMENTO |

## Antes de homologação

O código roda, mas há dados de integração que ninguém confirmou ainda.
Estão marcados com `TODO(a-confirmar)` e listados em
[`docs/regras-de-negocio.md`](docs/regras-de-negocio.md#em-aberto). Os
mais críticos:

- **Nome do pipeline** de conversão de pagamentos — sem ele o conversor
  rejeita a chamada.
- **Shape do JSON de pagamentos** — é proposta derivada do layout
  FEBRABAN, não contrato observado.
- **Schema de `Pagamento.Arquivo` e `Pagamento.Parametro`** — não
  capturados; mapeados como espelho dos de cobrança.
- **Valores numéricos de `ArquivoStatus`/`ArquivoEtapa`** — os nomes são
  reais, os números são suposição, e a tabela é compartilhada com o
  ecossistema CASH inteiro.
- **Fuso dos timestamps de `ASA_CASH_PAGAMENTO`** — `Janela:TimestampsBancoEmUtc`
  corrige com uma chave, mas precisa da resposta do time dono da base.
- **Schema de `ASA_CASH_ADESAO` inteiro** (só se `Geracao:Modo=CnabDireto`
  for ativado) — base nunca inspecionada; nome de tabela e todas as
  colunas de `EmpresaAdesao` são chute. Ver
  [`docs/pagamento-referencia.md`](docs/pagamento-referencia.md#6-modo-cnabdireto--o-robô-escreve-o-cnab-sem-passar-pelo-conversor) §6.

## Documentação

- [`docs/regras-de-negocio.md`](docs/regras-de-negocio.md) — o que cada
  robô faz, com diagramas e o porquê de cada decisão.
- [`docs/pagamento-referencia.md`](docs/pagamento-referencia.md) —
  de-para entre `ASA_CASH_PAGAMENTO` e o layout FEBRABAN 240.
- [`docs/cash-cobranca-referencia.md`](docs/cash-cobranca-referencia.md) —
  schema do CASH_COBRANCA e contratos das APIs externas.
- [`docs/riscos-conhecidos.md`](docs/riscos-conhecidos.md) — auditoria de
  riscos de comportamento.
- [`docs/conceitos-dotnet-ef-core.md`](docs/conceitos-dotnet-ef-core.md) —
  como o código usa .NET e EF Core.
