# Documentação

Guias funcionais e de referência técnica dos dois robôs
(`CnabRetorno.RemessaVan.Worker` e `CnabRetorno.PagamentoRetorno.Worker`).

## Comece por aqui

- [`regras-de-negocio.md`](regras-de-negocio.md) — **o que** cada robô
  faz: fluxo com diagramas, regra por passo com o porquê de cada decisão,
  e a lista do que ainda está em aberto.
- [`riscos-conhecidos.md`](riscos-conhecidos.md) — auditoria de riscos de
  **comportamento** (o código roda e produz o resultado errado), diferente
  da lista de dados de integração faltando.

## Referências de integração

- [`pagamento-referencia.md`](pagamento-referencia.md) — de-para entre a
  base `ASA_CASH_PAGAMENTO` e o layout FEBRABAN 240: estrutura das cinco
  duplas de tabelas, formas de lançamento, segmentos A/B/J/J-52,
  totalizadores e domínio de ocorrências. Fonte primária de
  `PagamentoDbContext`, `MontagemRetornoPagamento` e `Cnab240Pagamento`.
- [`cash-cobranca-referencia.md`](cash-cobranca-referencia.md) — schema da
  base `CASH_COBRANCA` e contratos das APIs de conversão e Gestor de
  Arquivo. Fonte primária do Robô 1 e do client de storage compartilhado.
- `Layout padrao CNAB240 V 10 11 - 21_08_2023-2.pdf` — o manual FEBRABAN
  completo. As seções que interessam são §2.2 (header/trailer de
  arquivo), §3.1 (pagamentos) e §4-G (campos genéricos).

## Como o código funciona

- [`conceitos-dotnet-ef-core.md`](conceitos-dotnet-ef-core.md) — DI e
  ciclo de vida (por que `DbContext` precisa de escopo por unidade de
  trabalho), options pattern, leitura de banco existente sem ser dono do
  schema, padrões aplicados — cada tópico aponta pro arquivo real.
- [`segunda-fonte-de-dados-sql-server.md`](segunda-fonte-de-dados-sql-server.md) —
  o padrão usado pelas duas bases de outros times, implementado em
  `src/*/Persistencia/*DbContext.cs`.
- [`evoluindo-com-libs-externas.md`](evoluindo-com-libs-externas.md) —
  como trazer libs externas sem reintroduzir camadas desnecessárias.
  Mantido como referência conceitual (ver nota no topo do doc).
