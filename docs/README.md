# Documentação

Guias funcionais e de referência técnica para os dois robôs de
processamento de retorno CNAB (`CnabRetorno.RetornoCron.Worker` e
`CnabRetorno.RetornoSubscriber.Worker`).

## Comece por aqui

- [`regras-de-negocio.md`](regras-de-negocio.md) — **o que** o sistema
  faz: fluxo completo dos dois robôs com diagramas (Mermaid) de fluxo,
  sequência entre sistemas e mesclagem de JSON (V+PV+pendências), tabela
  de regras por passo e o que ainda está em aberto.
- [`cash-cobranca-referencia.md`](cash-cobranca-referencia.md) — schema
  real do banco CASH_COBRANCA, contrato real da API de conversão
  (multipart upload, sync/async) e da API Gestor de Arquivo, de-para pro
  `TituloConvertido` de pendência — fonte primária usada em
  `CobrancaDbContext`, `CobrancaPendenciasRepository`,
  `Json.PendenciasParaTitulosConvertidosFactory`/`MesclagemDadosConvertidos`
  (Robô 1) e `GestorArquivosApiClient`/`GestorArquivoStorage` (Robô 2,
  presign de upload).
- [`conceitos-dotnet-ef-core.md`](conceitos-dotnet-ef-core.md) — **como**
  o código usa .NET/C# e EF Core: DI e ciclo de vida (por que `DbContext`
  precisa de escopo por unidade de trabalho), options pattern, leitura de
  banco existente sem dono de schema, padrões de arquitetura aplicados —
  cada tópico aponta pro arquivo real do projeto.
- [`riscos-conhecidos.md`](riscos-conhecidos.md) — auditoria de riscos de
  comportamento incorreto (duplicidade, perda silenciosa de dado,
  inconsistência de trailer) encontrados no código já implementado,
  diferente da lista de regras de negócio ainda não confirmadas
  (`TODO(a-confirmar)`) — nenhum desses pontos foi corrigido ainda.

## Guias de evolução

Para quando o pipeline precisar crescer além do scaffold atual:

- [`evoluindo-com-libs-externas.md`](evoluindo-com-libs-externas.md) —
  como trazer libs de conversão/persistência via git submodule sem
  reintroduzir camadas desnecessárias. Escrito antes dos dois robôs
  existirem; mantido como referência conceitual (ver nota no topo do doc).
- [`segunda-fonte-de-dados-sql-server.md`](segunda-fonte-de-dados-sql-server.md) —
  o padrão usado pela base de cobrança (`CobrancaDbContext`), já
  implementado de verdade em `src/*/Persistencia/CobrancaDbContext.cs`.

Mensageria (AWS SQS) está coberta em `conceitos-dotnet-ef-core.md` §5 e
implementada em `CnabRetorno.Common/Mensageria/SqsConsumerHostedService.cs`
— sem guia dedicado, dado que é uma dependência padrão do SDK da AWS, não
uma lib de terceiro que precisasse de tutorial próprio.
