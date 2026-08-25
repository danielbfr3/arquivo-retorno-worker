# CASH_COBRANCA — Documento de Referência Consolidado

> Extraído de fotos do ambiente de trabalho (VS Code + Swagger) em 17/07/2026,
> com complementos em 21/07/2026 (contrato real do conversor, campos novos de
> Titulo/Instrucao) e 24/07/2026 (entidade `Arquivo` real, presign real,
> shape da mensagem SQS). Trechos marcados 🆕 vêm dessas rodadas posteriores.
> Projeto: `arquivo-retorno-worker` / Solution `CnabRetorno.slnx` / ERD `cash-cobranca_v3.erd`

> **Escopo — o que o worker atual realmente consome daqui:**
>
> - `Cobranca.Arquivo` — a única tabela mapeada
>   (`ExcelCnab.Worker/Persistencia/CobrancaDbContext.cs`), e a única
>   escrita: uma linha por planilha enviada.
> - Contrato do **conversor** (§2.4) — só o endpoint **assíncrono**
>   (`/v1/convert/async/upload`), com a planilha no campo `file`.
>
> O que **não** é mais usado: `Cobranca.Parametro` (o worker não resolve
> mais `ContaHeader` nem NSA), a API **Gestor de Arquivos** (§3 — a
> planilha vai direto no multipart, sem passar por bucket), e o endpoint
> síncrono de conversão. As seções de `Titulo.*`, `Instrucao.*` e o
> de-para de `TituloConvertido` pertencem ao fluxo de retorno de cobrança,
> que não existe neste repositório. Tudo isso fica como referência do
> schema e dos contratos — que continuam reais —, não como descrição do
> que o código faz.

---

## 1. Modelo de Dados — Banco CASH_COBRANCA

### 1.1 Schema `Cobranca`

#### Cobranca.Arquivo

> 🆕 As 4 últimas colunas e a máquina de estados abaixo vêm da entidade
> real de domínio da API de cobrança (extração de 24/07/2026) — não
> constavam na extração original de 17/07/2026.

| Coluna | Tipo | Null | Observação |
|---|---|---|---|
| ArquivoNome | varchar(250) | NULL | |
| 🔑 ArquivoID | uniqueidentifier | N-N | PK |
| ClienteContaHeader | varchar(10) | NULL | Conta Cliente |
| ClienteTipoDocumento | smallint | NULL | 1 - CPF; 2 - CNPJ |
| ClienteDocumento | varchar(20) | NULL | CPF ou CNPJ |
| AppID | varchar(100) | N-N | |
| DataCriacao | datetime | NULL | |
| DataAtualizacao | datetime | NULL | |
| CriadoPor | varchar(50) | NULL | |
| ArquivoStatus | smallint | NULL | ver enum abaixo |
| ArquivoEtapa | smallint | NULL | ver enum abaixo |
| 🆕 DescricaoProduto | varchar | NULL | "Cobrança" (fixo na entidade real) |
| 🆕 LayoutBanco | varchar | NULL | não usado por estes workers |
| 🆕 LayoutTipoArquivo | varchar | NULL | não usado por estes workers |
| 🆕 ArquivoCnabID | uniqueidentifier | NULL | FK opcional pro CNAB de origem — não usado por estes workers |

**Esta é a tabela que amarra o worker ao resto do ecossistema.** O fluxo de entrada do
ecossistema já funciona assim (cliente pede URL assinada → cria linha em
`Arquivo` → manda `appId` + `Arquivo.Id` pro conversor), e o fluxo de
fluxo deste worker é exatamente esse: ele cria a linha antes de enviar e
usa o `ArquivoID` como `id` da conversão; a mensagem de conclusão devolve
o mesmo `id`, e é por ele que quem a consome recupera o cliente e o nome
do arquivo. Confirmado com o
time dono da base em 24/07/2026 ("se for persistir na tabela Arquivo o
arquivo retorno, entendo que seria o Arquivo.Id mesmo").

##### Enums `ArquivoStatus` / `ArquivoEtapa` 🆕

Nomes confirmados na entidade real; **valores numéricos não foram
fornecidos** — o mapeamento em `Core.Dominio.Arquivo` assume 1..N e está
marcado `TODO(a-confirmar)` (ver `docs/riscos-conhecidos.md`).

| ArquivoStatus | Etapas permitidas |
|---|---|
| AguardandoProcessamento | GeradoUrlBucket, ArquivoConferido |
| EmProcessamento | EnviadoParaConversao, ArquivoConvertido, ArquivoInvalido, Registrando |
| Processado | Registrado |

A entidade real impõe essa transição (`AtualizarStatus`/`AtualizarEtapa`
lançam se a etapa não pertence ao status). Estes workers **não replicam**
a validação — grava direto os pares que usa: `EmProcessamento` /
`EnviadoParaConversao` ao criar a linha, e `EmProcessamento` /
`ArquivoInvalido` se o conversor recusar o arquivo. Quem consome a
mensagem de conclusão é que leva a linha a `Processado`.

#### Cobranca.ArquivoErro

| Coluna | Tipo | Null | Observação |
|---|---|---|---|
| 🔑 ArquivoErroID | bigint | N-N | PK |
| 🔑 TipoErro | smallint | N-N | FK → TipoArquivoErro |
| 🔑 ArquivoID | uniqueidentifier | N-N | FK → Arquivo |
| CodigoArquivoErro | varchar(10) | N-N | Quebra layout |
| MotivoArquivoErro | varchar(250) | NULL | |
| DetalheArquivoErro | varchar(2000) | NULL | |

#### Cobranca.TipoArquivoErro

| Coluna | Tipo | Null | Observação |
|---|---|---|---|
| 🔑 TipoErro | smallint | N-N | 1 - Layout, 2 - Quarentena |
| Descricao | varchar(50) | NULL | |

#### Cobranca.Status

| Coluna | Tipo | Null | Observação |
|---|---|---|---|
| 🔑 CodigoStatus | smallint | N-N | PK |
| DescricaoStatus | varchar(100) | NULL | |

#### Cobranca.Parametro

Parâmetros de cobrança por cliente.

| Coluna | Tipo | Null | Observação |
|---|---|---|---|
| ParametroID | long | NULL | |
| 🆕 ClientId | varchar | NULL | identificador do cliente (não usado por estes workers — a busca é por `Documento`) |
| Documento | varchar(20) | NULL | CPF ou CNPJ |
| CodigoConta | varchar(10) | NULL | |
| TransferenciaSimples | int | NULL | |
| DataCriacao | datetime | NULL | |
| DataAtualizacao | datetime | NULL | |
| CriadoPor | varchar(50) | NULL | |
| 🆕 SequencialAtual | bigint | N-N | default 0 — **controle do número sequencial de arquivo (NSA) do cliente**, ver abaixo |

##### `SequencialAtual` — controle do sequencial de arquivo 🆕

> **Não usado pelo worker atual** — ele não gera CNAB nem monta header, e
> portanto não toca neste contador. Fica documentado porque a coluna é
> real e o ecossistema depende dela.

A série de sequencial é **compartilhada entre remessa e retorno** do mesmo
cliente: o banco recebe a remessa 1, envia o retorno 2, recebe a remessa
3, e assim por diante. Cada arquivo que entra ou sai incrementa o
contador.

Normalmente o core bancário já manda o sequencial correto no header do arquivo
V — mas se um arquivo precisar ser **regerado**, o número que vem no V
está errado (é o da remessa original). Por isso o retorno nunca reaproveita
o sequencial do V: antes de enviar o JSON pro conversor assíncrono, o
o robô de retorno incrementava `SequencialAtual` e **substituía** o valor nos dois
campos de sequencial do JSON (`arquivo.numeroSequencialArquivo` e
`lote.numeroRemessaRetorno` — o CNAB carrega o mesmo número nos dois
headers).

O incremento e a leitura acontecem num único `UPDATE ... OUTPUT`
(`Persistencia.SequencialArquivoRepository`), atômico no servidor — dois
processos concorrentes nunca recebem o mesmo número.

### 1.2 Schema `Titulo`

#### Titulo.Titulo

> 🆕 `CodigoOcorrencia`/`DescricaoOcorrencia` abaixo vêm de material novo
> fornecido em 21/07/2026 (mapeamento de campo do JSON de retorno, não de
> uma nova extração formal do ERD/Swagger) — ainda não reconciliado com o
> resto deste documento (extraído em 17/07/2026). Tratar como fonte válida,
> mas revisar se surgir uma extração formal depois.

| Coluna | Tipo | Null | Observação |
|---|---|---|---|
| 🔑 TituloID | uniqueidentifier | N-N | PK |
| 🔑 CodigoStatus | smallint | N-N | FK → Cobranca.Status |
| ArquivoID | uniqueidentifier | NULL | FK → Cobranca.Arquivo |
| ClienteContaHeader | varchar(10) | NULL | Conta Cliente |
| ClienteTipoDocumento | smallint | NULL | 1 - CPF; 2 - CNPJ |
| ClienteDocumento | varchar(20) | NULL | CPF ou CNPJ |
| CanalOrigem | varchar(50) | N-N | IB, VAN, IB_ARQ |
| ArquivoNome | varchar(250) | NULL | |
| AppID | varchar(100) | N-N | Aplicação que consumiu |
| DataAtualizacao | datetime | NULL | |
| CriadoPor | varchar(50) | NULL | |
| CodigoProduto | varchar(50) | NULL | Simples, Vinculada, Cessão |
| Observacao | varchar(200) | NULL | |
| DataCriacao | datetime | NULL | |
| 🆕 CodigoOcorrencia | varchar(10) | NULL | Código FEBRABAN de ocorrência — usado direto no JSON de retorno (`ocorrencia.codigo`) |
| 🆕 DescricaoOcorrencia | varchar(500) | NULL | `ocorrencia.descricao` no JSON de retorno |

#### Titulo.TituloInfo (dados do boleto — tabela larga)

| Coluna | Tipo | Observação |
|---|---|---|
| 🔑 TituloID | uniqueidentifier (N-N) | PK/FK → Titulo |
| TiTuloContaHeader | NVARCHAR(50) | |
| NumeroCarteira | NVARCHAR(50) | |
| CodigoBanco | NVARCHAR(10) | |
| CodigoModalidade | NVARCHAR(20) | |
| NossoNumeroCorrespondente | NVARCHAR(50) | |
| CodigoEspecie | NVARCHAR(20) | |
| ValorNominal | DECIMAL(18,2) | |
| ValorAbatimento | DECIMAL(18,2) | |
| DataEmissao | DATE | |
| DataVencimento | DATE | |
| SeuNumero | NVARCHAR(50) | |
| Aceite | NVARCHAR(5) | |
| CampoLivre | NVARCHAR(500) | |
| CodigoIndice | NVARCHAR(10) | |
| NossoNumero | NVARCHAR(50) | |
| TipoAutRecDivergente | NVARCHAR(10) | |
| PercentualMinimoTipo | NVARCHAR(10) | |
| PercentualMinimoValor | DECIMAL(18,2) | |
| PercentualMaximoTipo | NVARCHAR(10) | |
| PercentualMaximoValor | DECIMAL(18,2) | |
| PagtomentoParcial | bit | S ou N (core bancário) |
| PagamentoParcialQuantidade | INT | |
| Mensagem1..Mensagem5 | NVARCHAR(200) | 5 colunas |
| DescontoCodigo1..3 | NVARCHAR(10) | 3 colunas |
| DescontoValor1..3 | DECIMAL(18,2) | 3 colunas |
| DescontoTaxa1..3 | DECIMAL(9,6) | 3 colunas |
| DescontoData1..3 | DATE | 3 colunas |
| MultaCodigo | NVARCHAR(10) | |
| MultaData | DATE | |
| MultaTaxa | DECIMAL(9,6) | |
| MultaValor | DECIMAL(18,2) | |
| MoraCodigo | NVARCHAR(10) | |
| MoraData | DATE | |
| MoraTaxa | DECIMAL(9,6) | |
| MoraValor | DECIMAL(18,2) | |
| SacadoTipoDocumento | NVARCHAR(5) | 1 - CPF; 2 - CNPJ |
| SacadoDocumento | NVARCHAR(20) | |
| SacadoNome | NVARCHAR(200) | |
| SacadoEndereco | NVARCHAR(200) | |
| SacadoBairro | NVARCHAR(100) | |
| SacadoCidade | NVARCHAR(100) | |
| SacadoUF | NVARCHAR(2) | |
| SacadoCep | NVARCHAR(10) | |
| SacadoEmail | NVARCHAR(150) | |
| SacadoDDD | NVARCHAR(5) | |
| SacadoTelefone | NVARCHAR(20) | |
| 🆕 SacadorAvalistaTipoDocumento | smallint | CNPJ ou CPF — renomeado de `SacadorTipoDocumento` (material 21/07/2026) |
| 🆕 SacadorAvalistaDocumento | NVARCHAR(20) | renomeado de `SacadorDocumento` |
| 🆕 SacadorAvalistaNome | NVARCHAR(200) | renomeado de `SacadorNome` |
| SacadorEndereco | NVARCHAR(200) | |
| SacadorBairro | NVARCHAR(100) | |
| SacadorCep | NVARCHAR(10) | |
| SacadorCidade | NVARCHAR(100) | |
| SacadorUF | NVARCHAR(2) | |
| CartorioCriterioDias | NVARCHAR(10) | |
| CartorioNumeroDias | INT | |

O nome novo (`SacadorAvalista*`) deixa mais claro que essas colunas
descrevem o **avalista**, não o pagador — reforça, em vez de dissolver, a
suspeita de inversão semântica já registrada em §2.3 (o `sacado` do JSON
de retorno provavelmente deveria vir de `Sacado*`, não de
`SacadorAvalista*`). O de-para em §2.1/§2.2 abaixo usa `SacadorAvalista*`
porque é o mapeamento literal do material fornecido — a dúvida semântica
continua em aberto.

#### Titulo.TituloRegistroRetorno 🆕

| Coluna | Tipo | Null | Observação |
|---|---|---|---|
| 🔑 TituloID | uniqueidentifier | N-N | FK → Titulo (1:0..1) |
| CodBanco | NVARCHAR(10) | NULL | `cobrador.banco` no JSON de retorno |
| CodAgenciaCob | NVARCHAR(10) | NULL | `cobrador.agencia` no JSON de retorno |

Resolve dois `TODO(a-confirmar)` antigos deste documento (§2.1/§2.3:
"BancoCobrador"/"AgenciaCobradora" — de onde vêm?).

#### Titulo.TituloErro

| Coluna | Tipo | Null | Observação |
|---|---|---|---|
| 🔑 TituloErroID | bigint | N-N | PK |
| 🔑 TituloID | uniqueidentifier | N-N | FK → Titulo |
| CodigoOcorrenciaErro | varchar(10) | N-N | usado arquivo retorno |
| DescricaoOcorrenciaErro | varchar(500) | NULL | usado arquivo retorno |
| CodigoMotivo | varchar(50) | NULL | |
| DescricaoMotivo | varchar(500) | NULL | |

#### Titulo.NotaFiscal

| Coluna | Tipo | Null |
|---|---|---|
| 🔑 NotaFiscalID | long | N-N |
| TituloID | uniqueidentifier | N-N |
| NotaFiscalNumero | varchar(50) | NULL |
| NotaFiscalValor | decimal(16,2) | NULL |
| NotaFiscalDataEmissao | date | NULL |
| NotaFiscalChaveAcesso | varchar(50) | NULL |

#### Titulo.TituloIdempotencia

| Coluna | Tipo | Observação |
|---|---|---|
| HashID | varchar(500) | Constraint (unique) |
| TituloID | uniqueidentifier (N-N) | |
| DataCriacao | datetime2 | default getdate() |

### 1.3 Schema `Instrucao`

#### Instrucao.Instrucao

| Coluna | Tipo | Null | Observação |
|---|---|---|---|
| ClienteContaHeader | varchar(10) | NULL | Conta Cliente |
| 🔑 InstrucaoID | uniqueidentifier | N-N | PK |
| 🔑 CodigoStatus | smallint | N-N | FK → Cobranca.Status |
| ClienteTipoDocumento | smallint | NULL | 1 - CPF; 2 - CNPJ |
| ClienteDocumento | varchar(20) | NULL | CPF ou CNPJ |
| ArquivoID | uniqueidentifier | NULL | FK → Cobranca.Arquivo |
| ArquivoNome | varchar(250) | NULL | |
| AppID | varchar(100) | N-N | |
| DataCriacao | datetime | NULL | |
| DataAtualizacao | datetime | NULL | |
| CriadoPor | varchar(50) | NULL | |
| CanalOrigem | varchar(50) | N-N | IB, VAN, IB_ARQ |
| Tipo | varchar(50) | NULL | |
| Agencia | varchar(10) | NULL | |
| NumeroCarteira | varchar(50) | NULL | |
| NossoNumero | varchar(50) | NULL | |
| CodigoBaixa | varchar(50) | NULL | |
| ValorAbatimento | decimal | NULL | |
| DataAlteracaoVencimento | date | NULL | |
| DescontoTipo | varchar(50) | NULL | |
| DescontoData | date | NULL | |
| DescontoValor | DECIMAL(18,2) | NULL | |
| 🆕 CodigoOcorrencia | varchar(10) | NULL | `ocorrencia.codigo` no JSON de retorno |
| 🆕 DescricaoOcorrencia | varchar(500) | NULL | `ocorrencia.descricao` no JSON de retorno |

Uma instrução é casada com o título correspondente por
`ClienteContaHeader + ClienteDocumento + NossoNumero` (não há FK direta —
o join é feito pela aplicação, ver §2.1) — necessário porque o JSON de
retorno de uma instrução carrega campos (sacado, carteira, valor nominal…)
que só existem no título.

#### Instrucao.InstrucaoErro

| Coluna | Tipo | Null | Observação |
|---|---|---|---|
| 🔑 InstrucaoErroID | bigint | N-N | PK |
| 🔑 InstrucaoID | uniqueidentifier | N-N | FK → Instrucao |
| CodigoOcorrenciaErro | varchar(10) | N-N | usado arquivo retorno |
| DescricaoOcorrenciaErro | varchar(500) | NULL | usado arquivo retorno |
| Motivo | varchar(1000) | NULL | |

#### Instrucao.InstrucaoIdempotencia

| Coluna | Tipo | Observação |
|---|---|---|
| HashID | varchar(500) | Constraint (unique) |
| InstrucaoID | uniqueidentifier (N-N) | |
| DataCriacao | datetime2 | default getdate() |

### 1.4 Relacionamentos (notação pé-de-galinha)

- `Cobranca.Arquivo` 1 → N `Cobranca.ArquivoErro` (via ArquivoID)
- `Cobranca.TipoArquivoErro` 1 → N `Cobranca.ArquivoErro` (via TipoErro)
- `Cobranca.Arquivo` 1 → N `Titulo.Titulo` (via ArquivoID)
- `Cobranca.Arquivo` 1 → N `Instrucao.Instrucao` (via ArquivoID)
- `Cobranca.Status` 1 → N `Titulo.Titulo` (via CodigoStatus)
- `Cobranca.Status` 1 → N `Instrucao.Instrucao` (via CodigoStatus)
- `Titulo.Titulo` 1 → 0..N `Titulo.TituloErro` (via TituloID)
- `Titulo.Titulo` 1 → 1 `Titulo.TituloInfo` (via TituloID)
- `Titulo.Titulo` 1 → N `Titulo.NotaFiscal` (via TituloID)
- `Titulo.Titulo` 1 → N `Titulo.TituloIdempotencia` (via TituloID)
- `Titulo.Titulo` 1 → 0..1 `Titulo.TituloRegistroRetorno` 🆕 (via TituloID)
- `Instrucao.Instrucao` 1 → N `Instrucao.InstrucaoErro` (via InstrucaoID)
- `Instrucao.Instrucao` 1 → N `Instrucao.InstrucaoIdempotencia` (via InstrucaoID)

---

## 2. Tarefa dos Arquivos de Retorno — Segmentos T + U

### 2.1 De-para banco → TituloRetorno (C#)

Valores necessários do banco de dados para montar os Segmentos T + U.
Comentários com `?` são **perguntas em aberto**.

```csharp
new TituloRetorno
{
    NumeroRegistro = sequencial, // é gerado na hora?
    CodigoMovimento = "03", // 03-Entrada Rejeitada ou 26-Instrução Rejeitada

    // Tabela Titulo.TituloInfo
    Agencia = configuracao.Agencia, // qual seria?
    Conta = configuracao.Conta, // seria Titulo.TituloInfo - TituloContaHeader?
    Carteira = configuracao.Carteira, // NumeroCarteira

    // Tabela Titulo.TituloInfo
    NossoNumero = titulo.NossoNumero, // NossoNumero ou NossoNumeroCorrespondente?
    NumeroDocumento = titulo.NumeroDocumento, // qual seria?
    Vencimento = titulo.DataVencimento.ToString("ddMMyyyy"), // DataVencimento
    ValorTitulo = titulo.ValorTitulo, // Valor Nominal

    BancoCobrador = configuracao.CodigoBanco, // qual seria?
    AgenciaCobradora = "00000", // qual seria?

    UsoEmpresa = titulo.SeuNumero, // Titulo.TituloInfo.SeuNumero
    CodigoMoeda = "09", // 09 - Real

    // Tabela Titulo.TituloInfo
    TipoInscricaoPagador = titulo.TipoInscricaoPagador, // SacadorTipoDocumento
    InscricaoPagador = titulo.DocumentoPagador, // SacadorDocumento
    NomePagador = titulo.NomePagador, // SacadorNome

    NumeroContrato = "", // qual seria?
    ValorTarifa = 0m, // precisa mudar?

    // - Para titulos com erro, usar Titulo.TituloErro.DescricaoOcorrenciaErro
    // - Para instruções com erro, usar
    //       Instrucao.InstrucaoErro.DescricaoOcorrencia
    // - Para instruções recusadas, qual a mensagem?
    // - Para titulos recusados, qual a mensagem?
    //       onde acho o que foi recusado na base?
    MotivoOcorrencia = titulo.MotivoOcorrencia,

    Valores = new ValoresTitulo
    {
        Acrescimos = 0,
        Desconto = 0,
        Abatimento = 0,
        Iof = 0,
        ValorPago = 0,
        ValorLiquido = 0,
        OutrasDespesas = 0,
        OutrosCreditos = 0,
        DataOcorrencia = DateTime.Today.ToString("ddMMyyyy"),
        CodigoOcorrenciaPagador = "",
        BancoCorrespondente = "",
        NossoNumeroBancoCorrespondente = ""
    }
};
```

### 2.2 JSON de saída do retorno (de-para)

```jsonc
{
  "sacado": {
    "documento": {
      "codigo": TipoInscricaoPagador,
      "inscricao": InscricaoPagador
    },
    "nome": NomePagador
  },
  "numeroCarteira": Carteira,
  "nossoNumero": NossoNumero,
  "seuNumero": UsoEmpresa,
  // ou NumeroDocumento, dependendo da semântica adotada
  "valorNominal": ValorTitulo,
  "dataVencimento": Vencimento,
  "codigoIndice": CodigoMoeda,
  "ocorrencia": {
    "codigo": CodigoMovimento
  },
  "motivos": MotivoOcorrencia,
  "cobrador": {
    "banco": BancoCobrador,
    "agencia": AgenciaCobradora
  },
  "valorPago": Valores.ValorPago,
  "valorLiquido": Valores.ValorLiquido,
  "valorDesconto": Valores.Desconto,
  "valorAbatimento": Valores.Abatimento,
  "valorJurosMultaEncargos": Valores.Acrescimos,
  "valorIof": Valores.Iof,
  "valorOutrasDespesas": Valores.OutrasDespesas,
  "valorOutrosCreditos": Valores.OutrosCreditos,
  "valorTarifaCustas": ValorTarifa,
  "dataOcorrencia": Valores.DataOcorrencia,
  "numeroRegistro": NumeroRegistro
}
```

### 2.3 Perguntas em aberto (checklist)

- [x] `NumeroRegistro` — sequencial: gerado na montagem do JSON combinado
      (V+PV+pendências), não pela API. Ver §2.4 — sequência `1, 3, 5, 7...`.
- [ ] `Agencia` / `AgenciaCobradora` — de onde vem? (configuração fixa "00000"?)
- [ ] `Conta` — seria `Titulo.TituloInfo.TituloContaHeader`?
- [ ] `NossoNumero` — usar `NossoNumero` ou `NossoNumeroCorrespondente`?
- [ ] `NumeroDocumento` — qual coluna corresponde?
- [x] `BancoCobrador`/`AgenciaCobradora` — resolvido: `Titulo.TituloRegistroRetorno.CodBanco`/`CodAgenciaCob` (ver §1.2).
- [ ] `NumeroContrato` — qual seria a origem?
- [x] `ValorTarifa`/`Motivos` — resolvido pelo material de 21/07/2026: `motivos`
      é literal fixo `"0000000000"`, não mensagem de erro (ver §2.4).
- [ ] Para instruções/títulos recusados — qual mensagem usar e onde localizar na base?
      (`motivos` agora é fixo — ver acima; ainda não está claro onde a
      mensagem humana do motivo de recusa, se existir, deveria aparecer).
- [ ] ⚠️ Possível inversão semântica: `TipoInscricaoPagador/InscricaoPagador/NomePagador`
      estão mapeados de `Sacador*` (agora `SacadorAvalista*`, ver §1.2), mas
      no CNAB o **pagador** normalmente é o **Sacado** (quem paga o
      boleto), não o Sacador/Avalista. O material de 21/07/2026 confirma o
      mapeamento literal `SacadorAvalista*` — **não resolve** a dúvida
      semântica, só a torna mais explícita (o nome novo já deixa claro que
      são dados do avalista). Conferir com o time antes de fechar em produção.

### 2.4 Contrato real da API de conversão (confirmado em 21/07/2026)

Substitui as suposições de §2.1/§2.2 (que eram um rascunho anterior à
confirmação do contrato real). Endpoints usam **multipart/form-data com
upload de arquivo**, não JSON body:

- `POST /v1/convert/sync/upload` — campos `file` (binário), `appId`
  (`"cash-cobranca"`), `pipeline` (`"conversao-cobranca-retorno-para-json"`
  pra CNAB→JSON; reverso ainda não confirmado), `id` (correlação escolhida
  pelo chamador). Responde na hora: `{appId, id, success, outputFormat,
  binary, data: {arquivo, lote, titulos[], totais}}`.
- `POST /v1/convert/async/upload` — mesmos campos. Responde
  `{jobId, appId, id, status:"pending", statusUrl}` — não bloqueia; o
  resultado chega depois via SQS (ver shape abaixo).
- **`id` é o `Cobranca.Arquivo.ArquivoID`** (Guid) — não um GUID
  descartável. É ele que a mensagem de conclusão devolve, permitindo ao
  quem consome a conclusão recuperar cliente/nome do arquivo direto da
  tabela.

#### Mensagem SQS de conclusão 🆕 (observada em 24/07/2026)

```jsonc
{
  "id": "11111111-1111-1111-1111-111111111111", // = Cobranca.Arquivo.ArquivoID
  "success": true,
  "data": { "outputUrl": "https://...(URL assinada de download)" }
}
```

Substitui o shape que se supunha antes (`{jobId, status:"succeeded",
resultUrl, inputFileName...}`, derivado do endpoint de polling de status
de job). Modelado em `ConversaoConcluidaMessage` só com esses três campos
— o que a mensagem real trouxer a mais é ignorado na desserialização.
- Cada item de `data.titulos[]` (`TituloConvertido` no código) representa
  um título **ou** uma pendência (título/instrução negado ou com erro) —
  mesmo shape pros dois casos. Campo `motivos` é **literal fixo
  `"0000000000"`**, não a descrição do erro. `ocorrencia.codigo`/
  `descricao` vêm direto de `Titulo.Titulo.CodigoOcorrencia`/
  `DescricaoOcorrencia` (ou o equivalente em `Instrucao.Instrucao`) — não
  mais um `CodigoMovimento` fixo por tipo.
- `numeroRegistro` de cada item é atribuído pelo **chamador** (não pela
  API) ao montar o JSON combinado — sequência `1, 3, 5, 7...` (cada item
  representa implicitamente um par Segmento T+U na conversão final).

---

## 3. API Gestor Arquivo (abstração do S3)

**Base URL (homologação):**
`https://TODO-confirmar/gestor-arquivo-api` (Swagger em `/swagger/index.html`)

### 3.1 Objects (operações estilo S3)

| Método | Rota | Descrição |
|---|---|---|
| HEAD | `/{bucket}/{objectKey}` | Metadados do objeto sem corpo (equivalente ao HEAD Object do S3) |
| DELETE | `/{bucket}/{objectKey}` | Remove o objeto (equivalente ao DELETE Object do S3) |

Path params em ambos: `bucket` (string, required), `objectKey` (string, required).
Response: 200 OK.
*(Há mais endpoints no grupo acima do recorte visível — provavelmente GET/PUT do objeto.)*

### 3.2 ObjectsMetadata

**GET `/api/objects/metadata`** — Retorna os metadados de um objeto identificado por bucket + key.

Query params: `bucket` (string), `key` (string)

Response 200 (text/plain):

```json
{
  "appId": "string",
  "id": "string",
  "fileName": "string",
  "contentType": "string",
  "size": 0,
  "eTag": "string",
  "createdAt": "2026-07-17T19:07:26.379Z",
  "updatedAt": "2026-07-17T19:07:26.379Z"
}
```

Response 400: Bad Request (text/plain)

### 3.3 Presign

**POST `/presign/upload`** — Emite uma URL assinada para UPLOAD (verbo PUT).
**POST `/presign/download`** — Emite uma URL assinada para DOWNLOAD (verbo GET).

Request body (application/json) — igual para ambos:

```json
{
  "appId": "string",
  "id": "00000000-0000-0000-0000-000000000000"
}
```

> 🆕 **Atualizado em 24/07/2026** pelo client real
> (`ArquivoApiClient.CreatePresignedUploadUrlAsync`, na lib core de
> arquivo): o corpo tem só `appId` + `id`, e o
> `id` é um **Guid** — o `ArquivoID` da linha em `Cobranca.Arquivo`, não um
> identificador arbitrário. O campo `expiresInSeconds` que aparecia no
> Swagger não é enviado pelo client real. As rotas também são
> `/presign/*`, não `/api/presign/*` (o prefixo `/api` vinha do
> recorte do Swagger; o client monta `BaseUrl + "presign/upload"`).

Response 200 (text/plain) — igual para ambos:

```json
{
  "method": "string",
  "url": "string",
  "appId": "string",
  "id": "string",
  "issuedAt": "2026-07-17T19:07:17.694Z",
  "expiresAt": "2026-07-17T19:07:17.694Z"
}
```

---

## 4. Configuração visível (appsettings — cash-cobranca-api)

Repositório: `cash-cobranca-api`

### 4.1 Clientes HTTP

| Cliente | Base URL |
|---|---|
| CoreBancarioService | `https://TODO-confirmar/core-bancario-api` |
| ArquivoApiClient | `https://TODO-confirmar/gestor-arquivo-api` (API Gestor Arquivo) |

### 4.2 Resiliência (ambos os clientes)

| Parâmetro | Valor |
|---|---|
| TimeoutSeconds | 30 |
| Retries | 3 |
| BackoffSeconds | 2 |
| CircuitBreaker.Enabled | true |
| CircuitBreaker.FailureRatio | 0.5 |
| CircuitBreaker.SamplingSeconds | 30 |
| CircuitBreaker.BreakSeconds | 15 |
| CircuitBreaker.MinimumThroughput | 5 |

### 4.3 S3 / LocalStack (dev)

| Parâmetro | Valor |
|---|---|
| ServiceURL | `http://localhost:4566` |
| Região | `...east-1` |
| Bucket | `cash-cobranca` |
| Prefixo | `modelos/boleto/` |

---

## 5. Visão geral da arquitetura (síntese)

1. **Cedente/Canal** (IB, VAN, IB_ARQ) envia arquivo de remessa → registrado em
   `Cobranca.Arquivo` (com `AppID`, `ArquivoStatus`, `ArquivoEtapa`).
2. Títulos e instruções extraídos → `Titulo.Titulo` + `Titulo.TituloInfo` e
   `Instrucao.Instrucao`, com idempotência via `HashID` nas tabelas
   `*Idempotencia`.
3. Erros de parsing/validação → `Cobranca.ArquivoErro` (tipo 1-Layout,
   2-Quarentena), `Titulo.TituloErro`, `Instrucao.InstrucaoErro` — estes dois
   últimos alimentam o **arquivo de retorno**.
4. O **worker de retorno** (`arquivo-retorno-worker`) monta o JSON de retorno
   (um item por título/instrução, ver §2.4), registra o arquivo em
   `Cobranca.Arquivo` e manda pro conversor com esse `ArquivoID` como `id`.
5. A conclusão volta por SQS com o mesmo `id`, e o armazenamento passa pela
   **API Gestor Arquivo**: presigned URLs (`/presign/upload|download` com
   `appId` + `id`) e metadados (`/api/objects/metadata`) — nunca acesso
   direto ao S3.
