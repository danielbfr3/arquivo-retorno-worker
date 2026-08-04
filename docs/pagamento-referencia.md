# Referência — retorno de pagamentos

De-para entre a base `ASA_CASH_PAGAMENTO` e o layout FEBRABAN 240 V10.11
(`Layout padrao CNAB240 V 10 11 - 21_08_2023-2.pdf`, nesta mesma pasta).
Fonte primária de `PagamentoDbContext`, `MontagemRetornoPagamento` e
`Cnab240Pagamento`.

Os schemas vêm da extração de 03/08/2026 (`INFORMATION_SCHEMA.COLUMNS` no
ambiente de homologação). Nenhum dado de cliente foi versionado aqui.

---

## 1. Estrutura das tabelas

Padrão por meio de pagamento: `<Tipo>` (cabeçalho/status), `<Tipo>Info`
(dados da transação), `<Tipo>Erro` (erros de ocorrência/layout),
`<Tipo>Idempotencia`. Cinco meios: `Pix`, `Ted`, `Tef`, `Tricon`,
`Boleto`.

### 1.1 Tabelas de cabeçalho — estrutura idêntica nas cinco

Só a PK muda (`PixID`, `TedID`, `TefID`, `TriconID`, `BoletoID`).

| Campo | Tipo | Nulo | Uso no retorno |
|---|---|---|---|
| `<Tipo>ID` | uniqueidentifier | não | identidade da movimentação |
| `CodigoStatus` | smallint | não | filtro (só finais) e fallback de ocorrência |
| `ClienteContaHeader` | varchar(10) | sim | conta do header quando não há débito |
| `ClienteTipoDocumento` | smallint | não | G005 do header |
| `ClienteDocumento` | varchar(20) | não | **agrupador**: um arquivo por cliente |
| `ArquivoID` | uniqueidentifier | sim | arquivo de entrada (não usado) |
| `AppID` | varchar(100) | não | — |
| `DataCriacao` | datetime2 | não | fallback do corte de janela |
| `DataAtualizacao` | datetime2 | sim | **corte de janela** e data real da efetivação |
| `CriadoPor` | varchar(50) | sim | — |
| `CanalOrigem` | varchar(50) | não | — |
| `CodigoOcorrencia` | varchar(10) | não | **G059** (ver §3) |
| `DescricaoOcorrencia` | varchar(100) | sim | descrição da ocorrência |
| `CodigoAutenticacao` | varchar(50) | sim | fallback de "Nosso Número" |
| `PreAprovado` | bit | não | — |

### 1.2 Domínios

`Pagamento.TipoTransacao` — `1` Arquivo, `2` TEF, `3` PIX, `4` BOLETO,
`5` TRICON, `6` TED. O `1` não é meio de pagamento (é canal de entrada) e
por isso não aparece no enum `MeioPagamento`.

`Pagamento.Status` — `1` Incluido, `2` Processando, `3` Rejeitado,
`4` Cancelado, `5` Erro, `6` Finalizado. **Valores reais**, diferente dos
enums de `Arquivo`, que são suposição.

### 1.3 Campo `Linhas`

Presente nas cinco tabelas `*Info`, `varchar(8000)`. Guarda os segmentos
CNAB da remessa original daquele pagamento, concatenados sem separador.
Um segmento tem 240 posições, então cabem até 33 — folga larga pros 2 que
um pagamento usa.

`SegmentosRemessa.Analisar` aceita as duas formas (com e sem quebra de
linha) e descarta o que não tiver exatamente 240 posições.

### 1.4 Colunas por meio usadas na montagem

| Conceito | Pix | Ted | Tef | Boleto | Tricon |
|---|---|---|---|---|---|
| Valor | `ValorPagamento` | `ValorTransacao` | `ValorTransacao` | `ValorPagamento` | `ValorPagamento` |
| Data da transação | `DataTransacao` | `DataTransacao` | `DataTransacao` | — | — |
| Banco do favorecido | `FavorecidoBancoIspb` | `FavorecidoBanco` | — (mesmo banco) | — | — |
| Beneficiário do título | — | — | — | `Sacador*` | `Sacador*` |
| Chave/QR | `ChavePixUrl` | — | — | — | — |

Boleto e Tricon não têm data de transação nem dados de débito; o robô usa
`COALESCE(DataAtualizacao, DataCriacao)` e cai no `ClienteContaHeader`.

> ⚠️ Na foto de `TefErro`, a PK aparece como `TedErroID` (com "d") e a FK
> como `TefID` — possível typo real no schema. As tabelas `*Erro` não são
> lidas por este robô (a ocorrência vem do cabeçalho), então não afeta o
> código, mas vale conferir com o time dono.

---

## 2. De-para com o layout FEBRABAN 240

### 2.1 Estrutura do arquivo

```
Header de arquivo   (tipo 0)   ← 1 por arquivo
  Header de lote    (tipo 1)   ← 1 por forma de lançamento
    Detalhe         (tipo 3)   ← segmentos A+B ou J+J-52
  Trailer de lote   (tipo 5)
Trailer de arquivo  (tipo 9)
```

O indicador Remessa/Retorno é o campo **G015, header de arquivo posição
143** (`1` remessa, `2` retorno) — e não o `TipoOperacao` do header de
lote, que vale `C` (lançamento a crédito) nos dois sentidos.

### 2.2 Forma de Lançamento (G029, header de lote 12-13)

O header de lote comporta **uma única** forma. Um cliente com TEF, TED e
boleto no mesmo dia gera três lotes.

| Código | Descrição | Segmento | Meio |
|---|---|---|---|
| `01` | Crédito em Conta Corrente/Salário | A | TEF |
| `41` | TED — Outra Titularidade | A | TED |
| `43` | TED — Mesma Titularidade | A | TED (alternativa) |
| `45` | PIX Transferência | A | PIX sem chave |
| `47` | PIX QR-Code | J | PIX com chave/URL |
| `30` | Liquidação de Título do Próprio Banco | J | Boleto/Tricon do ASA |
| `31` | Pagamento de Título de Outros Bancos | J | Boleto/Tricon de terceiros |

Versão do layout do lote (G030, 14-16): `046` pro segmento A, `040` pro J.

### 2.3 Segmento A — transferências

| Pos. | Campo | Origem |
|---|---|---|
| 15 | Tipo de Movimento (G060) | `0` inclusão (`3` estorno é o outro caso de retorno) |
| 16-17 | Código da Instrução (G061) | `00` inclusão de registro liberado |
| 18-20 | Câmara Centralizadora | linha da remessa |
| 21-23 | Banco do favorecido | linha da remessa / `FavorecidoBanco` |
| 24-43 | Agência/conta/DVs do favorecido | idem |
| 44-73 | Nome do favorecido | idem / `FavorecidoNome` |
| 74-93 | Seu Número (G064) | `IdentificadorExterno` |
| 94-101 | Data do pagamento | linha da remessa / `DataTransacao` |
| 120-134 | Valor do pagamento | valor agendado |
| 135-154 | Nosso Número (G043) | `CodigoAutenticacao` |
| **155-162** | **Data Real da Efetivação (P003)** | `DataAtualizacao` — **só se Finalizado** |
| **163-177** | **Valor Real da Efetivação (P004)** | valor — **só se Finalizado** |
| 231-240 | Ocorrências (G059) | ver §3 |

O segmento **B** (obrigatório na prática) carrega tipo e número de
inscrição do favorecido nas posições 18-32.

### 2.4 Segmento J — títulos

| Pos. | Campo | Origem |
|---|---|---|
| 18-61 | Código de barras (G063) | linha da remessa / `CodigoBarra` |
| 62-91 | Nome do beneficiário | linha / `SacadorNome` |
| 92-99 | Data de vencimento | linha / `DataVencimento` |
| 100-114 | Valor do título | linha / `ValorNominal` |
| 115-129 | Desconto + abatimento | linha / `ValorAbatimento` |
| 130-144 | Mora + multa | linha |
| 145-152 | Data do pagamento | desfecho |
| 153-167 | Valor do pagamento | `ValorPagamento` |
| 183-202 | Referência do pagador | `IdentificadorExterno` |
| 203-222 | Nosso Número | `NossoNumero` |
| 223-224 | Código da moeda (G065) | `09` Real |
| 231-240 | Ocorrências (G059) | ver §3 |

O **J-52** identifica pagador e beneficiário — atenção: os campos de
inscrição têm **15** posições aqui, contra 14 no header e no segmento B.
`J-52` e `J` compartilham o código de segmento `J`; o que os distingue é
o `52` nas posições 18-19.

### 2.5 Totalizadores

| Campo | Regra do layout |
|---|---|
| Trailer de lote, 18-23 (G057) | registros do lote — soma dos tipos 1, 2, 3, 4 e 5 (header e trailer inclusos) |
| Trailer de lote, 24-41 | somatória dos valores (P007 no segmento A, L001 no J) |
| Trailer de arquivo, 18-23 (G049) | quantidade de lotes |
| Trailer de arquivo, 24-29 (G056) | registros do arquivo — soma dos tipos 0, 1, 3, 5 e 9 |

Como cada pagamento ocupa 2 registros de detalhe, um lote com *n*
pagamentos tem `2 + 2n` registros, e o arquivo tem `2 + Σ(lotes)`.

---

## 3. Ocorrências (G059)

Campo de 10 posições, até 5 ocorrências de 2 dígitos cada. Códigos
relevantes:

| Código | Significado |
|---|---|
| `00` | Crédito ou Débito Efetivado — **pagamento confirmado** |
| `01` | Insuficiência de fundos — débito não efetuado |
| `02` | Crédito ou Débito cancelado pelo pagador/credor |
| `AE`, `AG`, `AN`, `AT`… | inscrição/agência/conta/documento inválidos |
| `CA`–`CE` | código de barras inválido (banco, moeda, DV, valor, campo livre) |
| `CF`–`CP` | valores inválidos (documento, abatimento, desconto, mora, multa, IR, ISS, IOF, INSS…) |
| `HA`–`HZ` | lote/arquivo/contrato não aceitos |
| `PA`–`PN` | falhas específicas de PIX |
| `ZA`–`ZK` | informativos (conta substituída, boleto já liquidado, antecipação…) |

`CodigoOcorrencia varchar(10)` na base tem exatamente essa largura, o que
sugere que o dado já é gravado no formato de destino — por isso ele
prevalece sobre o mapeamento por status.

> `TODO(a-confirmar)`: se for código interno de mesma largura em vez de
> FEBRABAN, é preciso uma tabela de-para. Sem ela, o cliente recebe código
> inválido.

---

## 4. Tabela criada por este projeto

`Pagamento.ControleJanelaRetorno` — a marca d'água dos arquivos parciais.
É a **única** tabela que este repositório cria; todas as outras pertencem
a outros times. DDL em
[`deploy/pagamento-controle-janela.sql`](../deploy/pagamento-controle-janela.sql).

O mesmo script adiciona `SequencialAtual` a `Pagamento.Parametro`, espelho
do que já existe em `Cobranca.Parametro`.

---

## 5. O que não foi capturado

A extração de 03/08/2026 registra explicitamente: *"Não fotografadas
(schema não capturado): Arquivo, ArquivoErro, Parametro, TipoArquivoErro e
as 5 tabelas de Idempotencia"*.

Consequência direta: `Pagamento.Arquivo` e `Pagamento.Parametro` estão
mapeados como **espelho** dos equivalentes de cobrança, com
`TODO(a-confirmar)` no `PagamentoDbContext`. As tabelas de idempotência
não são usadas por este robô — o controle de reenvio é a marca d'água do
§4, que é por cliente e janela, não por pagamento.
