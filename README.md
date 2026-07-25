# cnab-retorno-worker

Dois workers (.NET 10) que processam arquivos de retorno de cobrança a
partir de uma pasta compartilhada, convertem via API externa e publicam o
retorno final no padrão do cliente — desenhados como modelo de como isso
vai virar dois repositórios reais no futuro (ver `docs/evoluindo-com-libs-externas.md`).

> Este é um **scaffold**: a orquestração dos dois robôs está implementada
> de ponta a ponta, mas várias peças de regra de negócio e de contrato com
> sistemas externos ainda não foram confirmadas (ver seção
> [Pontos em aberto](#pontos-em-aberto)). Procure por `TODO(a-confirmar)`
> no código — é o grep pra achar tudo que falta decidir.

## Fluxo

**Robô 1 (`CnabRetorno.RetornoCron.Worker`)** — roda por cron (arquivos
chegam por volta das 6h): varre a pasta X por arquivos V, extrai o CNPJ do
header do V, localiza o PV correspondente. **Envia V e PV separadamente
pro conversor síncrono** (`/v1/convert/sync/upload`, multipart) — cada um
vira seu próprio JSON. Consulta pendências (títulos e instruções negados
ou com erro) na base **CASH_COBRANCA** e converte cada uma num objeto no
mesmo shape de título (`TituloConvertido`). **Mescla os três — títulos de
V, títulos de PV e pendências — num único JSON** (headers comparados,
sequenciais renumerados, totais recalculados), **registra o arquivo de
retorno em `Cobranca.Arquivo`** e manda pro conversor assíncrono
(`/v1/convert/async/upload`) usando o `ArquivoID` como identificador — é
essa correlação que volta pro Robô 2. Depois do lote, verifica quais CNPJs
com pendência não tiveram nenhum arquivo e repete o ciclo pra eles.

```
Pasta X ─► lista arquivos V
   │
   ▼
MD5 já processado hoje? ──sim──► move pra Backup (fim)
   │ não
   ▼
Extrai CNPJ do header do arquivo V
   │
   ▼
Localiza PV correspondente (se existir, lê os dois)
   │
   ▼
Converte V e PV separadamente (POST /v1/convert/sync/upload — 1 ou 2 chamadas)
   │
   ▼
Consulta CASH_COBRANCA por CNPJ: títulos/instruções
negados ou com erro (D-1) ─► converte cada um em TituloConvertido
   │
   ▼
Mescla titulos[] de V+PV+pendências num único JSON (headers comparados,
sequenciais renumerados 1,3,5..., totais recalculados) ──diverge──► falha isolada
   │ ok
   ▼
Reserva o sequencial do retorno (Cobranca.Parametro.SequencialAtual += 1)
e substitui nos dois headers do JSON ──sem parâmetro──► falha isolada
   │
   ▼
INSERT em Cobranca.Arquivo ─► ArquivoID
   │
   ▼
POST /v1/convert/async/upload (JSON combinado, id = ArquivoID)
   │            └──falha──► DELETE da linha (compensação)
   ▼
Registra MD5 no controle diário ─► move V(+PV) pra Backup
```

Depois do lote: compara CNPJs processados **nesta execução** (em memória,
não em banco) com a lista de CNPJs com pendência no CASH_COBRANCA; para
quem ficou de fora, repete a consulta + conversão, montando o JSON com um
arquivo/lote sintético (sem V real de origem, sem chamada síncrona — não
há CNAB pra converter).

**Robô 2 (`CnabRetorno.RetornoSubscriber.Worker`)** — fica inscrito numa
fila **SQS** de conclusão da conversão assíncrona (publicada por um sistema
externo — **não** pelo Robô 1; os dois robôs são desacoplados, ver
[Decisões](#decisões)):

```
Mensagem SQS recebida { id (= ArquivoID), success, data.outputUrl }
   │
   ▼
success = true? ──não──► loga erro (fim, sem retry)
   │ sim
   ▼
SELECT em Cobranca.Arquivo pelo ArquivoID ──não achou──► exceção (volta pra fila)
   │ (documento, nome do arquivo, conta do cliente)
   ▼
Baixa o arquivo CNAB gerado (data.outputUrl — URL assinada)
   │
   ▼
Pede URL assinada de upload à API Gestor Arquivo (POST /presign/upload,
appId + o mesmo ArquivoID) e faz o PUT nela — sem acesso direto a S3
   │
   ▼
UPDATE status/etapa (Processado / Registrado) ─► deleta a mensagem da fila
```

## Estrutura

| Projeto | Responsabilidade |
|---|---|
| `CnabRetorno.Core` | Domínio compartilhado entre os dois robôs, zero dependência de infra: parsing de nome V/PV, `Cnab240.Cnab240Campos` (leitura/escrita posicional CNAB240), entidades `Arquivo`/`Titulo`/`TituloErro`/`Instrucao`/`InstrucaoErro`/`InstrucaoComTitulo` (schema real do CASH_COBRANCA), contratos `ILayoutConversaoApiClient`/`IGestorArquivosApiClient`, DTOs fiéis ao contrato de conversão real (multipart upload) |
| `CnabRetorno.Common` | Infra genérica reutilizável por qualquer worker: `IMessageService<T>` + implementação SQS (`SqsConsumerHostedService`), HTTP client base (JSON e multipart) |
| `CnabRetorno.RetornoCron.Worker` | Robô 1 — `Json.MesclagemDadosConvertidos` (união V+PV+pendências a nível de JSON, e o sequencial do retorno), `Json.PendenciasParaTitulosConvertidosFactory` (pendência → `TituloConvertido`), `Persistencia.CobrancaPendenciasRepository` (consulta ao CASH_COBRANCA), `Persistencia.ArquivoRepository` (registro do retorno em `Cobranca.Arquivo`), `Persistencia.SequencialArquivoRepository` (NSA por cliente), `ControleIdempotenciaDiario`/`ControlePendenciasReportadasDiario` (idempotência via arquivo), pipeline |
| `CnabRetorno.RetornoSubscriber.Worker` | Robô 2 — consumidor SQS, `Persistencia.ArquivoRepository` (busca por `ArquivoID` + update de status/etapa), `Http.GestorArquivosApiClient` (presign real), `Storage.GestorArquivoStorage` (upload via presigned URL) |
| `Tests` | xUnit — testes puros (parsing, mesclagem de JSON, mapeamento de pendências, DTOs, mensagem SQS); ver `docs/` pra como testar contra infraestrutura real |

Nenhuma interface criada por especulação: as implementações concretas de
`IMessageService<T>`/HTTP client ficam locais a cada robô, porque só ele
usa. `Core` só carrega o que **os dois** robôs compartilham de fato
(parsing de nome V/PV, leitura/escrita posicional CNAB240, a entidade
`Arquivo`) mais os contratos que fariam sentido num terceiro worker futuro
da mesma família (`ILayoutConversaoApiClient`, `IGestorArquivosApiClient`)
e as demais entidades do CASH_COBRANCA, usadas hoje só pelo Robô 1.

## Decisões

- **V, PV e pendências do CASH_COBRANCA são unidos a nível de JSON, não de
  CNAB bruto.** V e PV são enviados **separadamente** pro conversor
  síncrono (dois uploads, dois JSONs); pendências viram objetos no mesmo
  shape (`TituloConvertido`). Os três `titulos[]` são concatenados,
  renumerados (`1, 3, 5...`) e os totais recalculados num único JSON, só
  então enviado ao conversor assíncrono. Reabre conscientemente uma
  abordagem descartada no início do projeto ("juntar JSONs já
  convertidos") — na época, sem visibilidade do contrato real; agora,
  com o shape confirmado por exemplo real, a reconciliação é direta. Ver
  `Json.MesclagemDadosConvertidos` e `docs/regras-de-negocio.md` pro
  algoritmo completo. Substitui por completo a mesclagem a nível de CNAB
  bruto (`Cnab240.MesclagemCnab240`, apagada).
- **O CNPJ do cliente vem do header do arquivo V** (extraído logo na
  leitura, `Cnab240Campos.ExtrairCnpjHeaderArquivo`), não mais de uma
  tabela `ParametroRetorno` fictícia — substituída pelo schema real do
  CASH_COBRANCA (`docs/cash-cobranca-referencia.md`).
- **A API de conversão usa multipart/form-data com upload de arquivo**
  (`file`+`appId`+`pipeline`+`id`), confirmado nesta rodada — substitui o
  corpo JSON `{layout, conteudo}` que era só um palpite. `appId =
  "cash-cobranca"` é reutilizado também nas chamadas do Gestor de
  Arquivos (mesma identidade nos dois sistemas). Pipeline reverso
  (JSON → CNAB do cliente) continua sem exemplo confirmado — ver
  `docs/cash-cobranca-referencia.md` §2.4.
- **Nenhum robô tem banco próprio — mas os dois escrevem em
  `Cobranca.Arquivo`.** Sem tabela nossa, sem Postgres, sem auditoria
  paralela: a única escrita é no registro que o ecossistema CASH já usa
  pra rastrear arquivo. Idempotência de reprocessamento continua fora do
  banco, num arquivo de controle (`.processados-hoje.json`) na pasta de
  origem, resetado diariamente (`ControleIdempotenciaDiario`).
  Consequência aceita: o mesmo arquivo reenviado em **dias diferentes** é
  reprocessado (sem histórico entre execuções).
- **O sequencial do arquivo (NSA) vem de `Cobranca.Parametro.SequencialAtual`,
  não do header do V.** A série é compartilhada entre remessa e retorno do
  mesmo cliente (remessa 1, retorno 2, ...). O core bancário normalmente manda o
  número certo no V, mas se um arquivo for **regerado** o número vem
  errado — por isso o retorno sempre incrementa o contador próprio e
  sobrescreve os dois campos de sequencial do JSON (header de arquivo e
  header de lote, que no CNAB carregam o mesmo número). O incremento é um
  `UPDATE ... OUTPUT` atômico, então execuções concorrentes nunca pegam o
  mesmo número. Ver `Persistencia.SequencialArquivoRepository`.
- **`Cobranca.Arquivo.ArquivoID` é a correlação de ponta a ponta.** O
  Robô 1 registra a linha do retorno antes de enviar e usa esse Guid como
  `id` da conversão; a mensagem de conclusão devolve o mesmo `id`; o
  Robô 2 lê a linha pra saber de quem é o arquivo e presigna o storage com
  ele. Confirmado com o time dono da base — é exatamente o que o fluxo de
  entrada do sistema já faz. Substitui as heurísticas anteriores (ClientId
  do nome do arquivo, CNPJ do header do CNAB baixado). Efeito colateral
  positivo: como o id do presign passou a ser determinístico, um redelivery
  do SQS sobrescreve o mesmo objeto em vez de duplicar.
- **Os dois robôs continuam desacoplados.** Não há chamada direta nem
  mensagem entre eles — o Robô 1 dispara a conversão assíncrona e segue
  seu ciclo (roda de madrugada, uma vez); o Robô 2 reage à fila SQS
  publicada por outro sistema. O que compartilham é um **registro**
  (`Cobranca.Arquivo`), não um canal: nenhum depende do estado de execução
  do outro — se o Robô 2 nunca rodar, o Robô 1 não trava nem muda de
  comportamento. Escolha deliberada pensando na divisão futura em dois
  repositórios.
- **Robô 2 não tem tabela própria.** A única escrita é avançar
  status/etapa da linha que o Robô 1 já criou. Idempotência de mensagem
  fica com o delete manual do SQS (sem delete, a mensagem reaparece
  sozinha após o visibility timeout — equivalente a nack + requeue).
- **Message broker é AWS SQS** (`AWSSDK.SQS`), confirmado nesta rodada.
  Trocar de tecnologia significa reescrever só `SqsConsumerHostedService`
  em `CnabRetorno.Common`, atrás de `IMessageService<T>`. Só o Robô 2 usa
  broker hoje — o Robô 1 não publica mensagem de tracking (removido do
  escopo).
- **Títulos/instruções negados ou com erro** vêm da base CASH_COBRANCA
  real (SQL Server, existente, fora do controle deste projeto) — schema
  em `docs/cash-cobranca-referencia.md`, extraído do ambiente real do time
  dono da base (não mais placeholder). Ver
  `docs/segunda-fonte-de-dados-sql-server.md` pro padrão de `DbContext`
  somente-leitura usado.
- **O documento do cliente no Robô 2 vem da linha em `Cobranca.Arquivo`**,
  recuperada pelo `id` da mensagem. Substituiu duas heurísticas frágeis
  que existiam antes: extrair `ClientId` do nome do arquivo e extrair CNPJ
  do header do CNAB baixado (que assumia layout FEBRABAN preservado).
- **Armazenamento do arquivo final via presigned URL da API Gestor
  Arquivo, nunca S3 direto.** Reescrito a partir do client real
  (`docs/cash-cobranca-referencia.md` §3): `POST /presign/upload` com
  `{appId, id}` dá uma URL assinada, o Robô 2 faz o PUT do binário nela —
  não existe endpoint de "registrar arquivo" separado, upload e registro
  são a mesma operação. O `id` é o `ArquivoID`, o mesmo em toda a cadeia.
  Removida a dependência de `AWSSDK.S3`/`AWSSDK.Extensions.NETCore.Setup`
  do Robô 2. Ver `Storage.GestorArquivoStorage`.
- **Resiliência do client HTTP do Gestor de Arquivos** segue os
  parâmetros documentados em `docs/cash-cobranca-referencia.md` §4.2
  (timeout, retry, circuit breaker), via
  `Microsoft.Extensions.Http.Resilience`. `TimeoutSeconds: 30` do
  documento é tratado como orçamento **total** da chamada (com retries),
  não por tentativa — `TODO(a-confirmar)`: o documento não distingue os
  dois.

## Pontos em aberto

> Riscos de comportamento **incorreto** (não só regra não confirmada) já
> identificados por auditoria — duplicidade de pendência reportada,
> perda silenciosa de dado, trailer inconsistente — estão documentados
> separadamente em [`docs/riscos-conhecidos.md`](docs/riscos-conhecidos.md),
> por serem mais graves num contexto de processamento bancário.

Marcados como `TODO(a-confirmar)` no código — a lista abaixo é o mesmo
levantamento do documento de tarefa original, agora rastreável por grep:

```bash
grep -rn "TODO(a-confirmar)" src/
```

Principais:
- **Valores numéricos de `ArquivoStatus`/`ArquivoEtapa`** — nomes vêm da
  entidade real, mas os `smallint` gravados em `Cobranca.Arquivo` são
  suposição (ver `Core.Dominio.Arquivo`). Como a tabela é compartilhada com
  o resto do ecossistema, isso está registrado como risco em
  `docs/riscos-conhecidos.md`, não só como pendência.
- Nomenclatura do arquivo de retorno — placeholder
  `RETORNO-{documento}-{yyyyMMdd}` em
  `ProcessadorArquivoRetornoService.MontarNomeArquivoRetorno`.
- Estouro do sequencial acima de 999999 (limite de 6 posições do campo no
  CNAB) — sem regra de rotação nem alerta; ver
  `MesclagemDadosConvertidos.AplicarSequencial`.
- Valor real de `CodigoStatus` "negado" (`Cobranca.Status`) — não
  documentado, placeholder em `CobrancaPendenciasRepository.CodigoStatusNegado`.
- Vários campos do `TituloConvertido` de pendência sem fonte definida no
  de-para (Agência, NumeroDocumento, NumeroContrato) — ver
  `PendenciasParaTitulosConvertidosFactory`.
- Possível inversão semântica Sacado/Sacador no de-para do pagador — o
  material de 21/07/2026 confirma o mapeamento literal `SacadorAvalista*`,
  mas não resolve a dúvida (ver `Core.Dominio.Titulo`,
  `docs/cash-cobranca-referencia.md` §2.3).
- Pipeline reverso do conversor (JSON → CNAB no layout do cliente) — só o
  de CNAB → JSON (`conversao-cobranca-retorno-para-json`) foi confirmado
  por exemplo real; ver `LayoutConversaoApiClient`.
- Fórmula de `Totais.QuantidadeRegistros` ao combinar V+PV+pendências —
  nenhum exemplo confirma; assumido `2 por item` (T+U implícito). Ver
  `Json.MesclagemDadosConvertidos`.
- Arquivo/lote sintético do laço pós-lote (`PipelineOptions.BancoPadrao`) —
  sem banco "dono" óbvio pra um cliente sem V real de origem.
- Shape exato da mensagem SQS de conclusão (`ConversaoConcluidaMessage`) —
  modelado a partir do handler real em depuração (`{id, success,
  data.outputUrl}`); campos extras são ignorados, então o risco residual é
  a mensagem não trazer algum desses três.
- `NumeroCarteira`/dados do título pra uma instrução sem título
  correspondente (`InstrucaoComTitulo` com `TituloID` nulo) — comportamento
  hoje é degradar pra campos vazios, sem alertar; ver
  `PendenciasParaTitulosConvertidosFactory.ConverterInstrucao`.
- Comportamento pra V sem PV / PV sem V, política de reprocessamento além
  do mesmo dia (idempotência hoje reseta diariamente).

## Rodando

```bash
dotnet build
dotnet test
```

Cada robô roda como um Worker Service independente:

```bash
dotnet run --project src/CnabRetorno.RetornoCron.Worker        # requer SQL Server + API de conversão
dotnet run --project src/CnabRetorno.RetornoSubscriber.Worker  # requer SQL Server + SQS + API Gestor Arquivo
```

Nenhum dos dois robôs usa banco próprio (Postgres) — os dois usam a base
de cobrança (SQL Server): o Robô 1 lê pendências e registra o retorno em
`Cobranca.Arquivo`, o Robô 2 lê essa linha e avança status/etapa. Nenhum
dos dois usa SDK de S3 — armazenamento passa pela API Gestor Arquivo
(presigned URLs).

## Rodando localmente

```bash
# 1. Sobe SQL Server (base de cobrança)
./deploy/setup-local.sh

# 2. Roda os dois robôs apontando pras dependências locais
DOTNET_ENVIRONMENT=Local dotnet run --project src/CnabRetorno.RetornoCron.Worker
DOTNET_ENVIRONMENT=Local dotnet run --project src/CnabRetorno.RetornoSubscriber.Worker
```

Não há emulação local ainda da fila SQS, da API Gestor Arquivo nem da API
de conversão — os dois robôs em `Local` apontam pros endpoints reais
(homologação), configurados em `appsettings.json` de cada worker.

## Documentação

Ver [`docs/README.md`](docs/README.md) para o índice completo. Destaques:
- `regras-de-negocio.md` — fluxo completo dos dois robôs com diagramas
  Mermaid (fluxo, sequência entre sistemas, máquina de estados).
- `conceitos-dotnet-ef-core.md` — .NET/EF Core/padrões de arquitetura
  usados no código, com referência ao arquivo real de cada tópico.
- `evoluindo-com-libs-externas.md`, `segunda-fonte-de-dados-sql-server.md`
  — guias de evolução (git submodules, segunda fonte de dados).
