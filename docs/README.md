# Documentação

Guias funcionais e de referência técnica do worker
`CnabRetorno.ExcelCnab.Worker`.

## Comece por aqui

- [`regras-de-negocio.md`](regras-de-negocio.md) — **o que** o robô faz:
  fluxo com diagrama, regra por passo com o porquê de cada decisão, o que
  ele deliberadamente não faz, e a lista do que ainda está em aberto.
- [`riscos-conhecidos.md`](riscos-conhecidos.md) — auditoria de riscos de
  **comportamento** (o código roda e produz o resultado errado), diferente
  da lista de dados de integração faltando.

## Referências de integração

- [`cash-cobranca-referencia.md`](cash-cobranca-referencia.md) — schema da
  base `CASH_COBRANCA` e contratos das APIs do ecossistema. Do que está
  ali, o worker usa `Cobranca.Arquivo` (§1.1, escrita), `Cobranca.DocumentoDados`
  (leitura — tabela nova, ver `deploy/criar-tabela-documento-dados.sql`) e
  o endpoint assíncrono do conversor (§2.4); o resto fica como referência
  do schema real, não como descrição do que o código faz.
- `Layout padrao CNAB240 V 10 11 - 21_08_2023-2.pdf` — o manual FEBRABAN
  completo. Este worker não escreve CNAB (quem faz é o pipeline
  `excel-cnab`), mas o manual continua sendo a referência do formato que
  sai do outro lado.

## Como o código funciona

- [`conceitos-dotnet-ef-core.md`](conceitos-dotnet-ef-core.md) — DI e
  ciclo de vida (por que `DbContext` precisa de escopo por unidade de
  trabalho), options pattern, leitura de banco existente sem ser dono do
  schema, padrões aplicados — cada tópico aponta pro arquivo real.
- [`segunda-fonte-de-dados-sql-server.md`](segunda-fonte-de-dados-sql-server.md) —
  o padrão usado pelas duas bases de outros times, implementado em
  `src/CnabRetorno.ExcelCnab.Worker/Persistencia/*DbContext.cs`.
- [`evoluindo-com-libs-externas.md`](evoluindo-com-libs-externas.md) —
  como trazer libs externas sem reintroduzir camadas desnecessárias.
  Mantido como referência conceitual (ver nota no topo do doc).
