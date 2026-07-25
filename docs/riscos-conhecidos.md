# Riscos conhecidos e inconsistências (auditoria)

Este documento é diferente da seção "Pontos em aberto" do `README.md` e de
`regras-de-negocio.md`: aquelas listam **regras de negócio ainda não
confirmadas** com o time dono do CASH_COBRANCA (`TODO(a-confirmar)` no
código — coisas que podem estar certas, só não foram validadas). Este
documento lista **riscos concretos de comportamento incorreto** encontrados
numa auditoria do código já implementado — cenários onde o sistema, do
jeito que está hoje, pode processar dado bancário de forma silenciosamente
errada, mesmo com build limpo e testes passando. Dado o domínio (retorno de
cobrança bancária), a maioria dos itens abaixo tem como consequência
possível **perda ou duplicação silenciosa de informação que deveria chegar
ao cliente**, sem erro visível.

Nenhum destes pontos foi corrigido ainda — este documento só registra o
achado, onde está no código, um cenário concreto de como ele se manifesta,
e uma direção possível de correção (não decidida). Itens já corrigidos
ficam marcados como **[CORRIGIDO]** no título, com uma seção "Correção
aplicada" no lugar da antiga sugestão.

---

## 1. [CORRIGIDO] Pendência pode ser reportada em duplicidade — múltiplos V do mesmo cliente

**Severidade:** Alta — duplicação de dado enviado ao cliente.

**Onde:**
- `Origem/PastaOrigemArquivosRetorno.cs` (`ListarArquivosVAsync`) — lista
  **todos** os arquivos V da pasta, sem dedupe por `ClientId`.
- `Pipeline/ProcessarArquivosVePvPipeline.cs:36` — processa a lista inteira
  em paralelo (`Parallel.ForEachAsync`, `MaxArquivosConcorrentes` padrão 8),
  cada arquivo num escopo de DI (e `CobrancaDbContext`) próprio.
- `Pipeline/ProcessadorArquivoRetornoService.cs:114`
  (`GerarLinhasExtrasAsync`) — roda por arquivo, sem saber nada sobre
  outros arquivos do mesmo cliente sendo processados na mesma execução.

**Problema:** a única idempotência do Robô 1 é por **MD5 do conteúdo do
arquivo** (`ControleIdempotenciaDiario`). Não existe nenhum controle de
"este título/instrução já foi reportado hoje" — a decisão de incluir uma
pendência na linha T/U é recalculada do zero, de forma independente, toda
vez que `GerarLinhasExtrasAsync` roda para aquele CNPJ.

**Cenário concreto:** o cliente ABC envia dois arquivos V no mesmo dia
(dois lotes intraday, por exemplo) — ou o mesmo V é reprocessado
manualmente antes de ter sido movido pra Backup (falha anterior na
movimentação, reexecução manual). Os dois arquivos têm conteúdo diferente
(MD5 diferente), então a idempotência por MD5 não pega nada. O pipeline
lista os dois na mesma execução e processa em paralelo. **Cada** um
consulta `CobrancaPendenciasRepository.ObterTitulosNegadosOuComErroAsync`
pro CNPJ do cliente ABC (janela D-1 idêntica) e gera a **mesma** linha T
pro título X negado. O cliente recebe dois arquivos convertidos no mesmo
dia, cada um reportando o título X como rejeitado.

**Correção aplicada:** novo `Origem/ControlePendenciasReportadasDiario.cs`
— mesmo padrão de `ControleIdempotenciaDiario` (estado em arquivo próprio
na pasta de origem, `.pendencias-reportadas-hoje.json`, resetado
diariamente, sem banco). Duas peças:

1. **Filtro + marcação.** Antes de gerar as linhas T/U,
   `FiltrarNaoReportados` remove títulos/instruções cuja chave
   (`T:{TituloID}` / `I:{InstrucaoID}`) já foi registrada hoje. Depois que
   a conversão do arquivo tem sucesso, `RegistrarReportadas` marca as
   chaves usadas — **nessa ordem em relação ao MD5**:
   `controleIdempotencia.RegistrarProcessado(md5)` primeiro, depois
   `controlePendencias.RegistrarReportadas(chaves)`. Essa ordem importa: se
   o processo morrer entre as duas escritas, o pior caso é o MD5 ficar
   registrado sem a pendência marcada (risco residual estreito, só reabre
   se outra V do mesmo CNPJ aparecer depois) — a ordem inversa arriscaria
   **perder** a pendência de vez (agravando o item 2), porque o
   reprocessamento do V filtraria algo que nunca chegou a ser enviado.
2. **Lock assíncrono por CNPJ** (`AdquirirLockCnpjAsync`, via
   `ConcurrentDictionary<string, SemaphoreSlim>`) — fecha a condição de
   corrida entre duas V do mesmo CNPJ processadas em paralelo pelo
   `Parallel.ForEachAsync`. Precisa ficar seguro **desde a consulta de
   pendências até `RegistrarReportadas`**, atravessando as duas chamadas de
   conversão — se liberado antes disso, a segunda V ainda veria a
   pendência como "não reportada". CNPJs diferentes nunca disputam o mesmo
   semáforo, então o paralelismo entre clientes diferentes não é afetado.
   Adquirido em `ProcessadorArquivoRetornoService.ProcessarAsync` logo
   após extrair o CNPJ do header.

`ProcessadorClientesSemArquivoService` (laço pós-lote) usa só o filtro +
marcação, sem lock — roda sequencial (`foreach`) e só começa depois que o
loop paralelo principal já terminou, então não há concorrência
intra-execução ali (comentário no código deixa essa dependência explícita
caso o laço seja paralelizado no futuro).

**Risco residual aceito:** o mecanismo (lock em memória + arquivo local)
só protege dentro de **um processo**. Se o agendador (K8s CronJob ou
equivalente) permitir duas execuções sobrepostas
(`concurrencyPolicy: Allow`), a proteção não vale entre processos
diferentes — mesma limitação preexistente que já valia só pra idempotência
por MD5. Não há manifesto de deploy versionado neste repo pra ajustar;
fica como pré-condição operacional (equivalente a
`concurrencyPolicy: Forbid`).

Testes: `tests/CnabRetorno.Tests/RetornoCron/Origem/ControlePendenciasReportadasDiarioTests.cs`
— filtro, reset diário, concorrência real em `RegistrarReportadas`
(`Task.WhenAll`) e mutex real em `AdquirirLockCnpjAsync` (mesmo CNPJ nunca
sobrepõe, CNPJs diferentes não serializam).

---

## 2. Janela D-1 fixa — pendência pode se perder pra sempre

**Severidade:** Alta — perda silenciosa de dado bancário.

**Onde:**
- `Pipeline/ProcessadorArquivoRetornoService.cs:116`
  (`GerarLinhasExtrasAsync`) — `DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))`.
- `Pipeline/ProcessadorClientesSemArquivoService.cs:40` — mesmo cálculo,
  duplicado.

**Problema:** a janela de busca de pendências no CASH_COBRANCA é sempre
**"ontem exatamente"** (`[D-1 00:00, D-1 23:59]` sobre `DataAtualizacao`),
nunca "desde a última execução bem-sucedida". Não existe nenhum checkpoint
de até quando o robô já reportou pendências.

**Cenário concreto:** o robô não roda na terça-feira (deploy, incidente,
janela de manutenção). Um título fica negado com `DataAtualizacao` =
terça. Na quarta-feira, `D-1` = terça — se o robô também não rodar na
quarta (ainda em recuperação do incidente), na quinta-feira `D-1` = quarta,
e a pendência de terça **nunca mais é capturada por nenhuma consulta
futura**, porque a janela sempre olha só o dia imediatamente anterior, não
um intervalo acumulado. O título fica rejeitado no CASH_COBRANCA
indefinidamente, mas o cliente nunca é informado disso pelo arquivo de
retorno.

**Sugestão (não implementada):** trocar a janela fixa "D-1" por "desde a
última execução que reportou esse CNPJ com sucesso" — também exige algum
checkpoint persistente (mesma tensão do item 1 com a decisão de "sem
banco"). Uma alternativa mais simples e menos invasiva: ampliar a janela
pra "últimos N dias" em vez de exatamente um dia, aceitando redundância
(que por sua vez esbarra no item 1, duplicidade) em troca de não perder
nada — mas isso também precisa de uma forma de saber o que já foi
reportado, senão duplica sistematicamente.

---

## 3. Comparação de CNPJ/CPF por igualdade de string exata, sem normalização

**Severidade:** Alta — pode causar perda silenciosa de pendências pra uma
categoria inteira de clientes (CPF).

**Onde:**
- `Core/Cnab240/Cnab240Campos.cs` (`ExtrairCnpjHeaderArquivo`) — lê um
  campo **Num de 14 posições, sempre zero-preenchido à esquerda** (padrão
  FEBRABAN pra campos numéricos), independente do CNPJ real ter menos
  dígitos.
- `Persistencia/CobrancaPendenciasRepository.cs:37,56` — compara
  `t.ClienteDocumento == cnpj` (igualdade de string exata, sem qualquer
  normalização).
- `Persistencia/SequencialArquivoRepository.cs` — mesmo problema no
  `WHERE Documento = @documento` de `Cobranca.Parametro`.
- `docs/cash-cobranca-referencia.md` §1.2/§1.3 — `Titulo.ClienteDocumento`
  e `Instrucao.ClienteDocumento` são `varchar(20)`, "CPF ou CNPJ", sem
  especificar formato/padding. O schema também expõe
  `ClienteTipoDocumento` (`1 - CPF; 2 - CNPJ`), confirmando que **clientes
  pessoa física existem** nesse domínio.

**Problema:** o CNAB sempre entrega uma string de 14 caracteres
zero-preenchida (`Cnab240Campos.LerTrim` só remove espaço, não zero à
esquerda). Não há garantia de que `ClienteDocumento` no CASH_COBRANCA
esteja salvo no mesmo formato — principalmente pra CPF (11 dígitos), onde
é comum bancos de dados guardarem o valor "cru" sem padding.

**Cenário concreto:** um cliente pessoa física está cadastrado no
CASH_COBRANCA com `ClienteTipoDocumento = 1` e
`ClienteDocumento = "12345678901"` (11 dígitos, sem padding). O header do
arquivo V desse cliente traz `"00012345678901"` (14 caracteres, zero à
esquerda). A comparação `t.ClienteDocumento == cnpj` nunca é verdadeira —
a consulta de pendências desse cliente **sempre retorna vazia**, mesmo que
ele tenha títulos negados de verdade. Nenhum erro é lançado, nenhum log
indica o problema — o robô simplesmente conclui "sem pendências" pra esse
CNPJ, todos os dias.

Desde o controle de sequencial, o mesmo desencontro tem um **segundo
efeito, esse barulhento**: o `UPDATE` em `Cobranca.Parametro` não acha
linha, e o arquivo falha com `SequencialIndisponivelException` em vez de
ser enviado. Isso é melhor que a falha silenciosa das pendências (o
problema fica visível), mas confirma que a normalização precisa ser
resolvida — o mesmo cliente que hoje "não tem pendência" amanhã vai
simplesmente não conseguir gerar retorno.

**Sugestão (não implementada):** normalizar os dois lados antes de
comparar (remover zeros à esquerda de ambos, ou preencher ambos pra um
formato canônico — 14 dígitos, por exemplo) antes de decidir a estratégia,
**confirmar com o time dono do CASH_COBRANCA qual é o formato real
salvo** em `ClienteDocumento` (com ou sem padding, CPF de 11 ou 14
dígitos).

---

## 4. Falha de conversão é confirmada (delete) como se fosse sucesso — Robô 2

**Severidade:** Alta — perda silenciosa de evento de negócio, sem alerta.

**Onde:**
- `Mensageria/ProcessarConclusaoConversaoService.cs` (bloco
  `if (!message.Success)`) — loga erro e **retorna normalmente**
  (`return`), sem lançar exceção.
- `Common/Mensageria/SqsConsumerHostedService.cs` — só deixa de deletar a
  mensagem dentro do bloco `catch`; qualquer `ProcessAsync` que retorna
  sem lançar é seguido de `DeleteMessageAsync` incondicional.

**Problema:** quando a API externa de conversão publica uma mensagem de
conclusão com `success: false`, o handler trata isso como um caminho
"normal" de saída (não lança exceção) — e do ponto de vista do SQS, isso é
indistinguível de sucesso: a mensagem é deletada da fila permanentemente.

**Cenário concreto:** a API de conversão falha ao gerar o arquivo no
layout do cliente. A mensagem chega no Robô 2 com `success: false`, o
handler loga `logger.LogError(...)` e retorna. A mensagem é deletada e
desaparece da fila — não há retry, não há fila de erro (dead-letter
queue), não há publicação de alerta em nenhum outro canal. Se ninguém
estiver observando ativamente os logs do Robô 2 (dashboard, alerta
configurado), essa falha passa despercebida indefinidamente, e o arquivo
de retorno daquele cliente simplesmente **nunca existe**, sem que ninguém
saiba que deveria existir.

Agravante depois do registro em `Cobranca.Arquivo`: a linha criada pelo
Robô 1 fica **presa em `EmProcessamento`/`EnviadoParaConversao` pra
sempre** — o Robô 2 só avança o status no caminho de sucesso. Ou seja, o
sintoma fica visível na tabela (arquivo que nunca sai de "em
processamento"), o que é uma via de detecção que não existia antes — mas
ninguém é notificado ativamente.

**Sugestão (não implementada):** não tratar `success: false` como um
retorno "processado com sucesso" pro broker — seja lançando uma exceção
controlada (cai no "não deletar" já existente, com o cuidado de definir
uma política de DLQ depois de N tentativas pra não gerar loop infinito, já
que a causa aqui não é transitória), seja marcando a linha em
`Cobranca.Arquivo` como inválida (`ArquivoEtapa.ArquivoInvalido` existe
exatamente pra isso), seja publicando esse caso em algo que gere alerta
ativo — não só uma linha de log.

---

## 5. Valor dos títulos rejeitados não entra no total (`Totais.ValorTotalCobrancaSimples`)

**Severidade:** Média — decisão já documentada, mas não confirmada com
quem define o layout; risco de rejeição do arquivo por inconsistência.

> Atualizado: a mesclagem migrou de CNAB bruto pra JSON
> (`Json.MesclagemDadosConvertidos`, ver `docs/regras-de-negocio.md`) —
> o risco abaixo persiste no novo desenho, só mudou de "trailer CNAB" pra
> "campo `Totais` do JSON".

**Onde:**
- `Json/PendenciasParaTitulosConvertidosFactory.cs` (`ConverterTitulo`) —
  o `TituloConvertido` gerado carrega o `ValorNominal` real do título
  rejeitado.
- `Json/MesclagemDadosConvertidos.cs` (`RecalcularTotais`) —
  `ValorTotalCobrancaSimples` soma só `V.Totais + PV.Totais`; as
  pendências (títulos/instruções do CASH_COBRANCA) **nunca entram nessa
  soma**, só incrementam `Titulos`/`QuantidadeRegistros`.

**Problema:** o JSON final tem um `TituloConvertido` com um valor
financeiro real (ex.: R$ 1.500,50) cujo total **não é refletido** em
`Totais.ValorTotalCobrancaSimples` — que descreve só V+PV, como se a
pendência gerada não tivesse valor associado.

**Cenário concreto:** um título negado de R$ 1.500,50 vira um
`TituloConvertido` com esse valor em `valorNominal`. `Totais.valorTotalCobrancaSimples`
não inclui esse valor em nenhuma soma. Se o conversor assíncrono (ou o
sistema que recebe o CNAB final do lado do cliente) validar "soma dos
`valorNominal` bate com o total declarado", esse arquivo pode ser
**rejeitado por inconsistência** — ou, pior, aceito silenciosamente com um
total que não reflete os valores reais em jogo.

**Sugestão (não implementada):** confirmar com o time do CASH_COBRANCA ou
com quem define o layout do cliente se o total deveria incluir o valor de
itens rejeitados. Se sim, ajustar `MesclagemDadosConvertidos.RecalcularTotais`
pra somar também os valores das pendências.

---

## Bônus: client HTTP da API de conversão sem resiliência (Robô 1)

**Severidade:** Baixa/Média — afeta disponibilidade/operação, não corrompe
dado diretamente, mas amplifica o item 2 (janela D-1) em caso de
instabilidade.

**Onde:**
- `RetornoCron.Worker/Program.cs` (`AddHttpClient<ILayoutConversaoApiClient,
  LayoutConversaoApiClient>`) — configura só `BaseAddress`/`Timeout`/
  `ApiKey`, sem `AddStandardResilienceHandler`.
- Comparar com `RetornoSubscriber.Worker/Program.cs`, que tem retry +
  circuit breaker configurados pro client do Gestor de Arquivos.

**Problema:** o client HTTP mais crítico do sistema — é ele quem chama
`/v1/convert/sync` e `/v1/convert/async`, onde o dado bancário
efetivamente é processado — não tem nenhuma política de retry ou circuit
breaker, ao contrário do client equivalente no Robô 2.

**Cenário concreto:** uma instabilidade transitória na API de conversão
(timeout, HTTP 5xx passageiro) durante o processamento do lote do dia faz
cada arquivo falhar individualmente (sem retry), virando `Falha` no
resumo da execução. Como o arquivo falho não é movido pra Backup, ele
tenta de novo no próximo dia — mas essa nova tentativa já está sujeita ao
item 2 (a janela D-1 de pendências pode ter avançado, perdendo dados que
deveriam ter sido incluídos no arquivo original).

**Sugestão (não implementada):** aplicar a mesma política de resiliência
(ou uma equivalente, ajustada ao SLA real da API de conversão) ao client
`ILayoutConversaoApiClient` do Robô 1.

---

## 6. Valores numéricos de `ArquivoStatus`/`ArquivoEtapa` são suposição

**Severidade:** Alta — grava dado errado numa tabela compartilhada por
outros sistemas, não só por estes workers.

**Onde:**
- `Core/Dominio/Arquivo.cs` — enums `ArquivoStatus`/`ArquivoEtapa` com
  valores `1..N` atribuídos por ordem, marcados `TODO(a-confirmar)`.
- `RetornoCron.Worker/Persistencia/ArquivoRepository.cs` — grava
  `EmProcessamento`/`EnviadoParaConversao` ao criar a linha.
- `RetornoSubscriber.Worker/Persistencia/ArquivoRepository.cs` — grava
  `Processado`/`Registrado` ao concluir.

**Problema:** a entidade real de domínio da API de cobrança confirma os
**nomes** dos enums e quais etapas pertencem a cada status,
mas o material extraído não mostra os **valores numéricos** — e as colunas
são `smallint`. Os números aqui foram atribuídos por ordem de declaração,
o que só coincide com a realidade por sorte.

**Cenário concreto:** se o `ArquivoStatus.Processado` real for `4` (e não
`3`, como assumido), o Robô 2 grava `3` — que na tabela pode significar
outra coisa, ou nada. Como `Cobranca.Arquivo` é a fonte de verdade de
rastreamento pra todo o ecossistema CASH (a cash-cobranca-api lê essa
mesma tabela), um telão de acompanhamento ou uma query de suporte passa a
mostrar o arquivo num estado errado — e não há erro nem exceção, porque
`smallint` aceita qualquer número.

Agravante: a entidade real **valida** a transição
(`EtapasPermitidasPorStatus`, `AtualizarEtapa` lança se a etapa não
pertence ao status). Estes workers escrevem direto via EF, sem passar por
essa validação — então um par status/etapa inconsistente entra no banco
sem resistência.

**Sugestão (não implementada):** confirmar os valores com o time dono da
base **antes de qualquer execução em homologação**, ou expor um endpoint/
consulta que devolva o mapeamento. Alternativa mais segura a médio prazo:
chamar a cash-cobranca-api (dona da entidade, com as invariantes) em vez
de escrever direto na tabela.

---

## Resumo

| # | Risco | Severidade | Robô | Status |
|---|---|---|---|---|
| 1 | Pendência duplicada entre múltiplos V do mesmo cliente | Alta | 1 | **Corrigido** |
| 2 | Janela D-1 fixa perde pendência se a execução falhar | Alta | 1 | Aberto |
| 3 | Comparação de documento sem normalização (CPF sem padding) | Alta | 1 | Aberto |
| 4 | Falha de conversão é ACKed como sucesso, sem alerta | Alta | 2 | Aberto |
| 5 | Trailer não soma valor de títulos/instruções rejeitados | Média | 1 | Aberto |
| 6 | Valores de `ArquivoStatus`/`ArquivoEtapa` são suposição | Alta | 1 e 2 | Aberto |
| B | Client de conversão sem retry/circuit breaker | Baixa/Média | 1 | Aberto |

Ver `README.md` e `docs/regras-de-negocio.md` pra a lista separada de
regras de negócio ainda não confirmadas (`TODO(a-confirmar)`).
