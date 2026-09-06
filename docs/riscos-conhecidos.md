# Riscos conhecidos (auditoria)

Diferente da seção [Em aberto](regras-de-negocio.md#em-aberto), que lista
**dados de integração faltando**. Aqui estão riscos de **comportamento**:
lugares onde o código roda sem erro e mesmo assim produz o resultado
errado.

É processamento bancário — um CNAB gerado a partir da planilha errada não
tem desfazer. Cada item diz o cenário concreto, o impacto e a decisão
tomada.

---

## 1. Valores numéricos de `ArquivoStatus` / `ArquivoEtapa` são suposição

**Bloqueante pra produção.**

Os **nomes** dos estados vêm da entidade real da cash-cobranca-api; os
**valores numéricos** (smallint no banco) nunca foram fornecidos. O enum
em `Core/Dominio/Arquivo.cs` assume `1..N` na ordem em que aparecem.

**Cenário:** o robô grava `ArquivoStatus = 2` querendo dizer
"EmProcessamento", mas na tabela `2` significa "Processado". A planilha é
dada como concluída sem que a conversão tenha terminado.

**Impacto:** `Cobranca.Arquivo` é compartilhada com o ecossistema CASH
inteiro. Gravar o número errado corrompe o rastreamento de arquivo dos
outros sistemas, não só deste worker.

**Decisão:** implementado com os valores supostos e `TODO(a-confirmar)`.
Confirmar antes de qualquer deploy.

---

## 2. Schema de `Cobranca.DocumentoDados` é placeholder

**Bloqueante pra produção.**

A tabela é nova e nunca foi inspecionada num ambiente real. O mapeamento
em `CobrancaDbContext` (`NumeroDocumento` varchar(20), `Dados`
nvarchar(max)) e o script de referência em
`deploy/criar-tabela-documento-dados.sql` são o que este worker espera —
não necessariamente o que o time dono da tabela vai criar. Isso inclui a
chave `"Razão Social"` (`Core/Dominio/DocumentoDados.ChaveRazaoSocial`),
que só existe como convenção deste worker, não como coluna própria da
tabela — depende de quem popula `Dados` incluir essa chave no JSON.

**Cenário:** o formato de `NumeroDocumento` gravado por quem popula a
tabela não bate com o CNPJ de 14 dígitos extraído do nome do arquivo (por
exemplo, vem pontuado, ou com outro identificador). Toda busca falha, e
todo arquivo vai pra quarentena por "documento sem dados" — silenciosamente
correto do ponto de vista do código, mas nenhuma planilha é processada.
Do mesmo jeito, se `Dados` existir mas sem a chave `"Razão Social"` (ou com
ela vazia), o arquivo vai pra quarentena por "cliente não encontrado" — é
**caminho crítico de todo arquivo**: sem razão social nenhuma planilha é
enviada.

**Decisão:** um único ponto de ajuste
(`CobrancaDbContext.OnModelCreating` + `Core/Dominio/DocumentoDados.cs`).
Confirmar com o time dono antes de homologar — schema da tabela e a
convenção da chave `"Razão Social"`.

---

## 3. Cliente sem razão social barra o envio

**Decisão consciente, com efeito colateral conhecido.**

Se `Cobranca.DocumentoDados.Dados` não tem a chave `"Razão Social"` — ou
ela vem vazia — a planilha vai pra quarentena e **não** é processada.

**Cenário:** cliente novo, planilha depositada antes de quem popula
`Cobranca.DocumentoDados` incluir a razão social daquele CNPJ. O arquivo
fica parado na quarentena até alguém notar.

**Alternativa descartada:** enviar com razão social vazia. O JSON é
contrato com o conversor, e um arquivo identificado pela metade é pior que
um arquivo não enviado — a quarentena é visível, um CNAB com dado faltando
não é.

**Mitigação:** o log sai como **erro** (não aviso), com CNPJ e nome do
arquivo. Reprocessar é mover o arquivo de volta pra pasta de entrada.

---

## 4. Nome do campo de metadados não foi confirmado

O contrato registrado em `cash-cobranca-referencia.md` §2.4 lista só
`file`, `appId`, `pipeline` e `id`. O campo que carrega o JSON com CNPJ e
razão social é `metadata` **por suposição**.

**Cenário:** o conversor ignora um campo que não conhece. O upload é
aceito (`status: pending`), o robô registra sucesso, e o CNAB sai sem os
dados do cliente — ou o pipeline falha lá adiante, longe daqui.

**Impacto:** falha silenciosa do ponto de vista deste worker: o aceite não
diz nada sobre o que o pipeline fez com os campos.

**Decisão:** `Conversao:CampoMetadados` é configuração — corrigir é mudar
uma chave, não código. Confirmar com o time do conversor antes de
homologar.

---

## 5. Retry automático reenvia o POST

`AddStandardResilienceHandler()` faz retry de falhas transitórias. Num
POST que cria trabalho do outro lado, isso é reenvio.

**Cenário:** o conversor recebe o upload, processa, e a resposta se perde
na rede. O handler repete o POST; o conversor recebe a mesma planilha duas
vezes.

**Mitigação:** o `id` é sempre o mesmo `ArquivoID` em todas as tentativas
— é o que permite ao outro lado reconhecer a repetição. Não é garantia de
idempotência do conversor, que não foi confirmada.

---

## 6. Duas planilhas do mesmo cliente em ciclos diferentes

Não há deduplicação por conteúdo. O que evita reprocessar o mesmo arquivo
é ele sair da pasta (Backup) ao fim do ciclo.

**Cenário:** o mesmo arquivo é depositado de novo, com o mesmo nome e o
mesmo conteúdo. Vira um segundo envio, com `ArquivoID` novo.

**Decisão consciente:** quem deposita é uma pessoa, e um depósito repetido
é mais provavelmente uma correção do que uma retransmissão automática —
deduplicar por conteúdo faria o robô **ignorar** um reenvio intencional.

---

## 7. Crash entre o preenchimento e o envio

A linha em `Cobranca.Arquivo` é criada **depois** que a planilha foi
preenchida com sucesso e **antes** da chamada ao conversor. Se o pod
morrer entre o registro e o envio, a linha fica como
`EnviadoParaConversao` sem que nada tenha sido enviado.

**Cenário:** linha pendurada esperando uma conclusão que nunca chega, e o
arquivo continua na pasta de entrada — o próximo ciclo cria uma **segunda**
linha, com `ArquivoID` novo, reprocessa o preenchimento do zero (a partir
do original) e envia.

**Impacto:** uma linha órfã no banco. O envio em si acontece uma vez só, o
que é o que importa.

**Decisão:** aceito. A ordem inversa (enviar e depois registrar) trocaria
a linha órfã por uma conclusão órfã — pior, porque a conclusão carrega o
CNAB pronto e não teria onde se ancorar.

---

## 8. `ClienteTipoDocumento` derivado do tamanho

`ClienteTipoDocumento` é `2` (CNPJ) quando o documento tem 14 dígitos e
`1` (CPF) caso contrário — domínio G005 do FEBRABAN.

Na prática o valor é sempre `2`: a máscara só reconhece 14 dígitos. A
regra fica porque a coluna é do ecossistema e uma máscara futura pode
aceitar CPF; hoje é código sem caminho vivo, não um risco ativo.

---

## 9. Testes não cobrem o caminho de I/O

A suíte cobre a lógica pura: leitura do nome do arquivo, preenchimento da
planilha em memória (`PreenchedorPlanilhaExcelTests`, com casos de sucesso
e de erro), serialização do payload, leitura do envelope de resposta,
construção do modelo EF.

**O que fica descoberto:** a varredura da pasta (incluindo o
comportamento sobre SMB), a leitura/gravação de bytes em Backup/Quarentena,
a chamada HTTP real, o `sp_getapplock` e a conexão de banco.

**Por quê:** não há SQL Server nem as APIs externas neste ambiente. Um
teste com mock de `HttpClient` verificaria o mock, não o contrato.

**Mitigação parcial:** `ModeloEfTests` valida o mapeamento sem conexão —
pega erro de modelo, não erro de schema.

---

## 10. Comparação de cabeçalho tolerante pode mascarar erro de digitação

`Preenchimento:ComparacaoCabecalho` padrão (`IgnorarCaixaEEspacos`) só
remove espaço nas **pontas** e ignora caixa — não normaliza espaço
duplicado no meio do texto nem acentuação.

**Cenário:** o cabeçalho da planilha é `"Nome  Cliente"` (dois espaços) e
a chave do JSON é `"Nome Cliente"` (um espaço). Não bate, e o arquivo
inteiro vai pra quarentena por `ColunaNaoEncontrada` — comportamento
correto (fail-fast), mas a causa raiz (espaço duplicado) não aparece na
mensagem de erro além da lista de chaves não encontradas.

**Mitigação:** o log de quarentena lista a(s) chave(s) que não bateram —
suficiente pra alguém abrir a planilha e comparar visualmente o cabeçalho.
Se isso incomodar na prática, a normalização pode evoluir pra colapsar
espaços internos — mudança isolada em `PreenchedorPlanilhaExcel.Normalizar`.

---

## 11. Licença transitiva do ClosedXML (`SixLabors.Fonts`)

O ClosedXML traz `SixLabors.Fonts` como dependência transitiva, que tem
licença própria (Six Labors Split License — gratuita para a maioria dos
usos, paga acima de um teto de faturamento).

**Cenário:** o worker é usado por uma organização que ultrapassa o teto de
uso gratuito da Six Labors sem que ninguém tenha verificado.

**Mitigação:** confirmar o enquadramento da licença antes de homologar; o
worker não usa nenhuma API do ClosedXML que dependa de renderização de
fonte (evitar `Columns().AdjustToContents()`/autofit em produção — usado
só no gerador do arquivo de exemplo em `deploy/exemplos/`, fora do worker).

---

## 12. SMB não se comporta como disco local

A pasta de origem é um compartilhamento montado no pod. Coisas que num
diretório local não acontecem e ali acontecem: `File.GetLastWriteTimeUtc`
com granularidade e relógio do servidor de arquivos (a janela de
`SegundosEstabilidade` depende disso), gravação/`File.Delete` que não são
atômicos entre montagens diferentes, e falhas de I/O transitórias por
queda de sessão.

**Mitigação:** Backup e Quarentena são subpastas **da própria pasta de
origem**, então a gravação acontece dentro da mesma montagem. Uma falha de
I/O vira falha daquele arquivo e o resto da varredura segue.

**Não coberto:** relógio do servidor SMB adiantado em relação ao pod faz
todo arquivo parecer recém-gravado, e a varredura não pega nada. Se isso
aparecer, é `SegundosEstabilidade` que precisa subir.
