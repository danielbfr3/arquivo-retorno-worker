# arquivo-retorno-worker

Dois workers .NET 10, independentes entre si:

| Robô | Projeto | O que faz |
|---|---|---|
| **1** | `CnabRetorno.RemessaVan.Worker` | Varre a pasta onde as VANs depositam remessas CNAB, renomeia no padrão ASA, guarda no Gestor de Arquivos (ou no S3) e registra em `Cobranca.Arquivo`. |
| **2** | `CnabRetorno.PagamentoRetorno.Worker` | Das 7h às 18h, gera arquivos de retorno de pagamentos — parciais de hora em hora e um consolidado no fim do dia — via conversor síncrono, guardados e registrados em `Pagamento.Arquivo`. |

Os dois não conversam. Compartilham `CnabRetorno.Core` (domínio e
contratos, zero dependência externa) e `CnabRetorno.Common`
(infraestrutura: HTTP, storage, mensageria).

## Estrutura

```
src/
  CnabRetorno.Core/                 domínio + contratos (sem PackageReference)
    Aplicacao/                      interfaces e DTOs das APIs externas
    Cnab240/                        leitura posicional do layout FEBRABAN 240
    Dominio/                        Arquivo, MovimentacaoPagamento, enums
  CnabRetorno.Common/               infra compartilhada
    Http/                           base HTTP + client do Gestor de Arquivos
    Storage/                        upload via presigned URL
    Mensageria/                     SQS (não usado hoje — ver "Filas")
  CnabRetorno.RemessaVan.Worker/    Robô 1
    Vans/                           máscaras das VANs, nome padrão ASA
    Origem/                         pasta de entrada, backup, quarentena
    Storage/                        modo S3 direto
    Persistencia/                   CASH_COBRANCA
    Pipeline/
  CnabRetorno.PagamentoRetorno.Worker/  Robô 2
    Agendamento/                    grade de janelas 7h–18h
    Persistencia/                   ASA_CASH_PAGAMENTO (UNION dos 5 meios)
    Json/                           parse da remessa gravada + montagem do JSON
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

Não há SQL Server, SQS nem as APIs externas neste ambiente — a verificação
possível é build + testes de unidade. Os testes cobrem as partes puras
(máscaras, nomenclatura, grade de janelas, montagem do JSON, parse do CNAB
gravado) e a construção do modelo EF, que valida o mapeamento sem abrir
conexão.

## Configuração

Tudo que é nome de recurso de infra é configuração — pasta de origem,
bucket, prefixo, filas, base URLs, AppIDs, templates de nome, horários.
Nada literal em código. Em cluster, sobrescrever por variável de ambiente
com `__` no lugar de `:` (ex.: `Sqs__Filas__ConversorValido`).

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
| `Conversao:Pipeline` | Pipeline do conversor (**em aberto**) |
| `Retorno:CodigoBanco` / `TipoServico` | Header do arquivo |
| `ConnectionStrings:Pagamento` | ASA_CASH_PAGAMENTO |

## Filas

Nenhum dos dois robôs consome fila hoje: o Robô 1 é ingestão pura e o Robô
2 usa o conversor síncrono. O suporte a SQS fica no `CnabRetorno.Common`
como capacidade da biblioteca, com todo nome de fila resolvido por
configuração.

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
