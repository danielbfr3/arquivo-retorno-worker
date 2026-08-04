# Riscos conhecidos (auditoria)

Diferente da seção [Em aberto](regras-de-negocio.md#em-aberto), que lista
**dados de integração faltando**. Aqui estão riscos de **comportamento**:
lugares onde o código roda sem erro e mesmo assim produz o resultado
errado.

É processamento bancário — um arquivo entregue ao cliente não tem
desfazer. Cada item diz o cenário concreto, o impacto e a decisão tomada.

**Corrigidos na rodada de 03/08/2026** (mantidos fora da lista): buraco
pós-18h (dia útil virou consolidado→consolidado), PIX QR-Code sem chave
no detalhe, título rejeitado com valor de pagamento preenchido,
re-report por UPDATE sem mudança de status (pares reportados), ausência
de idempotência por conteúdo no Robô 1 (MD5 em banco), réplicas
concorrentes (lock `sp_getapplock`), sobrescrita na quarentena (sufixo de
timestamp), compensação mascarando a exceção original, e `SqlQuery` com
`UPDATE...OUTPUT` (trocado por ADO puro — o embrulho em subselect do EF
estouraria em runtime).

---

## 1. Valores numéricos de `ArquivoStatus` / `ArquivoEtapa` são suposição

**Bloqueante pra produção.**

Os **nomes** dos estados vêm da entidade real da cash-cobranca-api; os
**valores numéricos** (smallint no banco) nunca foram fornecidos. O enum
em `Core/Dominio/Arquivo.cs` assume `1..N` na ordem em que aparecem.

**Cenário:** o Robô 1 grava `ArquivoStatus = 2` querendo dizer
"EmProcessamento", mas na tabela `2` significa "Processado". O arquivo de
remessa é dado como concluído sem nunca ter sido convertido, e o worker de
conversão nunca o pega.

**Impacto:** `Cobranca.Arquivo` e `Pagamento.Arquivo` são compartilhadas
com o ecossistema CASH inteiro. Gravar o número errado corrompe o
rastreamento de arquivo dos outros sistemas, não só destes workers.

**Decisão:** implementado com os valores supostos e `TODO(a-confirmar)` em
três lugares. Confirmar antes de qualquer deploy.

---

## 2. `CodigoOcorrencia` pode não ser FEBRABAN

`CodigoOcorrencia varchar(10)` tem exatamente a largura do campo G059, o
que é forte indício de que já é gravado no formato de destino. O robô
confia nisso e o copia direto pro arquivo.

**Cenário:** o campo é um código interno de mesma largura. O cliente
recebe um retorno com ocorrências que o parser dele não reconhece — ou,
pior, que ele reconhece como outra coisa.

**Decisão:** o código gravado prevalece; o mapeamento por status é
fallback. Se for interno, falta uma tabela de-para em
`Core/Dominio/StatusPagamento.cs` — um único ponto de mudança.

---

## 3. `Rejeitado` e `Erro` sem código gravado saem em branco

Não existe ocorrência genérica de "rejeitado" no G059 — os códigos são
todos específicos do motivo (`AE` inscrição inválida, `AG` conta inválida,
`CD` valor inválido…).

**Cenário:** um pagamento rejeitado cujo `CodigoOcorrencia` veio vazio
entra no arquivo com as 10 posições em branco. O cliente vê o registro,
não vê o motivo.

**Decisão:** brancos, conscientemente. Inventar um código erraria o
motivo, o que é pior que não informá-lo. As tabelas `<Tipo>Erro` têm
`CodigoOcorrenciaErro` e poderiam preencher essa lacuna — não foram
integradas porque não se sabe se seguem o domínio FEBRABAN (ver risco 2).

---

## 4. Objeto órfão no storage quando o registro falha (Robô 1)

O checklist manda guardar (passos 6-7) antes de registrar (passo 9). Se o
`INSERT` falhar, o objeto já está no bucket sem linha no banco.

**Cenário:** SQL Server indisponível por 30 segundos. O arquivo subiu, a
linha não existe, e nenhum worker de conversão vai pegá-lo.

**Decisão:** o arquivo vai pra **quarentena** (não backup, não fica na
origem) e o log carrega `ArquivoID` + referência do objeto, pra que dê pra
limpá-lo ou completar o registro à mão. Backup faria o arquivo sumir da
vista com o registro faltando; deixar na origem faria o próximo ciclo
gravar um **segundo** objeto com GUID novo.

**Alternativa não adotada:** registrar antes e compensar com `DELETE`
(padrão do Robô 2). Foge da ordem do checklist, que descreve o fluxo já
acordado com o time.

---

## 5. NSA consumido por arquivo que falha (Robô 2)

O sequencial é reservado antes da conversão. Se a conversão ou o upload
falharem, o número já foi consumido e a série do cliente fica com um
buraco.

**Cenário:** conversor fora do ar numa janela. O cliente recebe os
arquivos com NSA 41 e 43; o 42 nunca existiu.

**Decisão:** aceito. Repetir um NSA é pior que pular um — o cliente usa
esse número justamente pra detectar arquivo repetido.

---

## 6. Marca d'água avançada com o arquivo já entregue

A marca d'água e os pares reportados são atualizados **depois** de o
arquivo existir. Se o processo morrer entre "marcar Registrado" e o
registro do controle, o próximo parcial reenvia as mesmas movimentações.

**Cenário:** pod reiniciado no instante exato. O cliente recebe as mesmas
movimentações em dois arquivos parciais consecutivos.

**Decisão:** deliberado, nessa ordem. Duplicar é recuperável (o cliente
concilia por `SeuNumero`); perder não. A ordem inversa (controle antes do
arquivo) faria uma falha de conversão descartar movimentações pra sempre.
A janela dessa corrida é de milissegundos — e os pares reportados
reduziram o caso mais provável de reenvio (UPDATE sem mudança de status)
a não-evento.

---

## 7. Atribuição de VAN é ambígua

Todas as máscaras de remessa compartilham o prefixo `CB<cnpj>`. Duas VANs
diferentes podem casar o mesmo nome de arquivo; vence a primeira da lista.

**Impacto:** baixo. A VAN só alimenta o token `{van}` do nome ASA, que o
template padrão não usa, e não afeta cliente, storage nem registro.

**Decisão:** máscaras ordenadas da mais específica pra mais genérica no
`appsettings`, com o raciocínio registrado ali.

---

## 8. `Linhas` como fonte de verdade depende de ele estar preenchido

A montagem do retorno prefere os segmentos gravados da remessa. Quando
`Linhas` vem vazio, cai nas colunas — e o resultado pode divergir do que o
cliente enviou (nome truncado de outro jeito, conta sem zeros à esquerda).

**Cenário:** pagamentos criados por API (não por arquivo) não têm
`Linhas`. O retorno desses registros sai normalizado do jeito do nosso
banco.

**Decisão:** aceito. Não há de onde tirar o original quando ele não
existe. Coberto por teste (`Sem_linhas_deve_cair_nas_colunas`).

---

## 9. Sem recuperação de janela perdida (Robô 2)

Se o worker estiver fora do ar às 9h, o parcial das 9h não acontece e não
é gerado depois.

**Impacto:** baixo por desenho — a marca d'água é contínua por cliente,
então o parcial seguinte leva tudo que ficou pra trás, inclusive através
da virada do dia útil (18h→18h).

**Exceção:** se o worker ficar fora do ar **das 17h às 18h01**, o
consolidado do dia não sai — e o consolidado do dia seguinte **não** o
substitui (cobre outro dia útil). As movimentações não se perdem (vão nas
parciais seguintes), mas o arquivo de fechamento daquele dia precisa de
reexecução manual.

---

## 10. `PutArquivoAsync` do client oficial usa `new HttpClient()`

Observado no `GestorArquivosClient.cs` da Common da empresa (extração de
03/08/2026): o método de PUT instancia `HttpClient` direto, sem
`IHttpClientFactory` — risco clássico de esgotamento de sockets em alto
volume.

**Não afeta este repositório:** os dois robôs usam `AddHttpClient<>` com
`AddStandardResilienceHandler()`. Fica registrado como alerta pra quem for
reutilizar aquele client.

---

## 11. Cliente com mais de uma conta na mesma janela

A granularidade decidida é **um arquivo por cliente**, e o header do CNAB
só carrega uma conta. Um cliente com movimentações em duas
`ClienteContaHeader` no mesmo dia sai num arquivo só, debaixo da primeira
conta encontrada.

**Cenário:** cliente com conta em duas agências paga por TEF numa e por
boleto na outra. As duas movimentações saem sob a mesma conta no header.

**Decisão:** `MovimentacoesRepository.ResolverContaHeader` **loga aviso**
com as contas envolvidas em vez de escolher em silêncio. Se o caso
aparecer de verdade em produção, é sinal de que a granularidade precisa
virar cliente+conta — mudança de agrupamento, não de montagem.

---

## 12. Testes não cobrem o caminho de I/O

Os testes cobrem lógica pura e o modelo EF. Nada exercita banco real,
Gestor de Arquivos, conversor ou pasta SMB — não há esses recursos neste
ambiente.

**O que fica descoberto:** a consulta `UNION ALL` das cinco duplas de
tabelas nunca rodou contra o schema real. Um nome de coluna errado só
aparece em runtime. O teste de modelo EF pega mapeamento inconsistente,
mas não valida que as colunas existem no banco.

**Mitigação:** primeiro teste em homologação deve ser uma janela com
`Janela:IntervaloParcial` curto e a base real, olhando o log. Os pontos
de maior atenção nesse primeiro contato: o `UNION ALL` das cinco duplas,
a reserva de NSA e o `sp_getapplock` (os três em SQL/ADO cru, sem teste
de integração).
