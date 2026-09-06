# Evoluindo o pipeline com libs externas (git submodules)

> **Nota:** o fluxo esboçado abaixo (remessa V/PV, D-1, consolidação,
> conversão final) era o escopo de **retorno de cobrança**, que já não
> existe neste repositório — o worker atual preenche planilhas com dados
> de uma tabela e entrega o resultado ao conversor (ver `README.md`
> raiz). Classes citadas abaixo como `ArquivoRetornoWorkerPreview.cs`
> foram removidas.
>
> O guia continua válido como referência **conceitual**: a regra de
> adaptador único por integração externa segue em vigor, hoje aplicada à
> API HTTP de conversão (`ILayoutConversaoApiClient`) e à biblioteca de
> planilha (`Planilha/PreenchedorPlanilhaExcel.cs`, único lugar do worker
> com `using ClosedXML.Excel`) em vez de libs por submodule.

Guia para quando o worker precisar crescer na direção do fluxo esboçado em
`ArquivoRetornoWorkerPreview.cs` — remessa + retorno pareados, consulta de
títulos negados D-1, layout por cliente, consolidação e conversão final —
trazendo para dentro do projeto libs que já existem prontas (conversão de
arquivo e, em alguns casos, persistência própria), baixadas via git
submodule.

O objetivo deste documento é dar uma sequência de decisões pra seguir
**quando a necessidade aparecer de verdade**, não fazer isso tudo de uma vez
agora. O projeto foi simplificado recentemente (ver `README.md` — projeto
único, sem portas/adaptadores pra implementação única) exatamente pra evitar
estrutura especulativa; este guia existe pra essa simplificação não virar
desculpa pra empurrar tudo pra dentro de `ProcessadorArquivoRetorno` sem
critério quando a complexidade chegar.

## 1. Trazer a lib pro projeto via git submodule

```bash
git submodule add git@github.com:sua-org/cnab-conversor.git libs/cnab-conversor
git submodule update --init --recursive
```

Isso cria/atualiza um `.gitmodules` na raiz apontando pra URL e pro commit
fixado. Alguns pontos que custam caro descobrir depois:

- **O submodule é fixado num commit, não numa branch.** Atualizar a lib é um
  ato deliberado: `cd libs/cnab-conversor && git pull origin main`, testar,
  e então `git add libs/cnab-conversor && git commit` no projeto principal
  pra mover o ponteiro. Nunca rodar `git submodule update --remote` direto
  em CI/produção — isso puxa o HEAD da branch sem revisão, o equivalente a
  um `npm install` sem lockfile.
- **`docker compose`/`docker build` não trazem o submodule sozinhos.** O
  `Dockerfile` atual faz `COPY . .`, mas isso só copia o que já está
  *checked out* no contexto de build. Se o clone que gera a imagem (CI, ou
  a máquina local) não rodou `git submodule update --init --recursive`
  antes do `docker build`, a pasta `libs/cnab-conversor` entra vazia e o
  build quebra silenciosamente com "arquivo não encontrado" em vez de erro
  claro. Adicionar ao pipeline de CI/deploy, antes do build da imagem:
  ```bash
  git submodule update --init --recursive
  ```
  Se o submodule for um repositório privado, o runner de CI também precisa
  de credencial (deploy key ou token) com acesso a ele — separado da
  credencial do repositório principal.
- **`.gitmodules` e a pasta `libs/` entram no controle de versão**; o
  conteúdo do submodule em si, não (ele vive no próprio histórico dele).

## 2. Colocar a lib na solution

Se a lib for um projeto .NET normal:

```bash
dotnet sln CnabRetorno.slnx add libs/cnab-conversor/src/CnabConversor.csproj
```

E referenciar a partir do Worker:

```xml
<!-- src/CnabRetorno.Worker/CnabRetorno.Worker.csproj -->
<ItemGroup>
  <ProjectReference Include="..\..\libs\cnab-conversor\src\CnabConversor.csproj" />
</ItemGroup>
```

`ProjectReference` direto no `.csproj` da lib (em vez de empacotar como
NuGet local) é o caminho mais simples pra uma lib interna que muda com
frequência: builda junto, debuga junto (dá pra colocar breakpoint dentro da
lib), sem o atrito de publicar/versionar um pacote a cada ajuste. Só vale a
pena migrar pra NuGet local (ou artifact feed interno) se a lib passar a ser
usada por *outros* projetos além deste worker e a duplicação de
`ProjectReference` apontando pra caminhos relativos entre repositórios
diferentes começar a doer.

## 3. Onde a lib entra no pipeline — a regra que evita a bagunça

**O worker nunca deve espalhar tipos da lib externa pelo pipeline inteiro.**
A regra prática: qualquer classe de `Pipeline/` ou `Persistencia/` só
conhece tipos definidos *neste* projeto (`Entidades/`, `Conversao/`, etc.).
Quem conhece a lib externa é uma única classe adaptadora, e só ela.

Isso não é reintroduzir a camada de portas/adaptadores que acabamos de
remover — é o inverso do que foi removido. As interfaces que tiravam do
projeto (`IOrigemArquivos`, `IStorageArquivos`, repositórios) existiam pra
uma implementação só seguindo um padrão engessado sem necessidade real de
troca. Aqui a motivação é outra e concreta: isolar um `using` de um
namespace de fora do seu controle (versão, breaking changes, dependências
transitivas) pra um único arquivo, de forma que quando a lib mudar de
assinatura o *diff* fica contido ali.

Exemplo de encaixe pro conversor CNAB→JSON existente:

```
src/CnabRetorno.Worker/
  Conversao/
    IConversorRetorno.cs        (já existe)
    ConversorPassthrough.cs     (já existe)
    Externo/
      ConversorCnabJsonAdapter.cs   <- só esse arquivo faz "using CnabConversor.*"
```

```csharp
namespace CnabRetorno.Worker.Conversao.Externo;

/// <summary>
/// Ponte para a lib externa (libs/cnab-conversor, trazida via git
/// submodule). Nenhuma outra classe do worker referencia tipos de
/// CnabConversor.* diretamente — só esta.
/// </summary>
public class ConversorCnabJsonAdapter(CnabConversor.IConversor conversorExterno)
{
    public JsonRemessa ConverterParaJson(byte[] conteudoCnab)
    {
        var resultadoExterno = conversorExterno.Converter(conteudoCnab);
        // Mapeia o tipo da lib pro tipo nosso (JsonRemessa, definido em
        // Entidades/ ou Conversao/) — é aqui que um breaking change na lib
        // vira um erro de compilação isolado, em vez de se espalhar.
        return new JsonRemessa(resultadoExterno.Cnpj, resultadoExterno.Titulos);
    }
}
```

Se a lib também *persiste* (não só converte), o adaptador ainda é o único
lugar que a conhece — ele só passa a receber também o que for necessário
pra ela gravar (uma connection string, um `DbContext` dela, etc.), conforme
o item 5 abaixo.

## 4. Crescendo o pipeline sem virar espaguete

O fluxo do `ArquivoRetornoWorkerPreview.cs` tem 9 passos, contra os 7 atuais
de `ProcessadorArquivoRetorno`. Migrar pra esse formato "linha reta com
comentário numerado" já é a estrutura certa até um certo tamanho — é
exatamente o que existe hoje e é fácil de ler de cima a baixo. Sinais de que
passou do ponto:

- Mais de ~10 passos sequenciais, ou
- Um passo específico (ex.: "monta JSON consolidado") ganha lógica de
  decisão própria (vários `if`/layout diferentes) que merece teste isolado
  sem instanciar o pipeline inteiro.

Quando isso acontecer, extrair esse passo pra uma classe própria (ex.:
`Pipeline/ConsolidadorRemessaRetorno.cs`) com um método público e testes
diretos — **sem** criar uma interface pra ele a menos que exista de fato
mais de uma forma de consolidar (ex.: um formato por tipo de layout). Uma
classe concreta chamada direto pelo pipeline resolve 90% dos casos; a
interface só compensa quando o `RegistroConversores` (que já existe) puder
escolher entre implementações — o mesmo padrão que já resolve o de/para por
layout hoje serve de modelo pra qualquer outro passo que precisar de
múltiplas variantes.

Mapeamento aproximado dos passos do Preview pra pastas deste projeto:

| Passo do Preview | Pasta/arquivo sugerido |
|---|---|
| Buscar arquivos V/PV + retorno pareado | `Origem/` — estender `PastaLocalOrigem` ou nova classe irmã. Desenho concreto (lista de clientes vinda da base de cobrança, casando pelo código no nome do arquivo) em [`segunda-fonte-de-dados-sql-server.md`](segunda-fonte-de-dados-sql-server.md#5-onde-encaixar-a-consulta-no-pipeline) |
| Converter CNAB→JSON (lib externa) | `Conversao/Externo/ConversorCnabJsonAdapter.cs` (item 3) |
| Consultar títulos negados D-1 | `Cobranca/` (nova pasta) — ver `segunda-fonte-de-dados-sql-server.md` |
| Layout configurado do cliente | mesma fonte de `Cobranca/`, ou continua em `PerfilConversao`/`RetornoDbContext` se já é o mesmo dado |
| Montar JSON consolidado | `Pipeline/ConsolidadorRemessaRetorno.cs` quando justificar (ver acima) |
| Converter JSON→CNAB no layout | `Conversao/` — nova implementação de `IConversorRetorno` ou adaptador equivalente |
| Persistir (MD5) | `Pipeline/ProcessadorArquivoRetorno.cs`, igual ao fluxo atual |
| Mover arquivos processados | `Origem/` |

## 5. Quem é dono da idempotência quando a lib também persiste

Isso varia por lib — trate caso a caso, mas com uma pergunta guia: **quem é
a fonte de verdade sobre "esse arquivo já foi processado"?**

**Lib só converte, devolve bytes/objeto.** Nada muda na arquitetura atual:
o adaptador (item 3) chama a lib, pega o resultado, e o resto do pipeline
segue exatamente como hoje — checagem de MD5, `db.ArquivosRetorno.Add`,
`SaveChangesAsync`. A tabela `arquivo_retorno` continua sendo a única fonte
de verdade sobre idempotência.

**Lib também grava direto num banco (dela).** Aqui existe risco real de dado
duplicado se o worker chamar a lib duas vezes pro mesmo arquivo (reprocesso
após falha, por exemplo) e a lib não tiver sua própria proteção contra isso.
Duas situações:

- *A lib tem sua própria idempotência* (unique constraint, upsert por
  algum identificador natural do título/arquivo): então é seguro deixar
  ela persistir os dados de negócio (títulos, valores) e o worker continua
  gravando `arquivo_retorno` só como *registro de auditoria/controle de
  fila* — "o worker viu e processou este MD5", sem duplicar o dado de
  negócio que já mora na tabela da lib.
- *A lib não tem idempotência própria*: **não deixe ela ser chamada mais de
  uma vez pro mesmo arquivo.** A checagem de MD5 do worker (que já roda
  *antes* de qualquer chamada de conversão/persistência, olhe o passo 2 de
  `ProcessadorArquivoRetorno.ProcessarAsync`) é o guarda que impede isso —
  desde que a gravação em `arquivo_retorno` só aconteça **depois** que a
  lib confirmar sucesso. Isto é, a ordem importa: chamar a lib primeiro,
  gravar o registro de idempotência do worker por último — na falha no meio
  do caminho, o arquivo não sai da origem e é retentado; se a lib não é
  idempotente, uma falha *depois* dela persistir mas *antes* do worker
  gravar seu próprio registro gera duplicidade no reprocesso. Se esse risco
  for inaceitável pro caso de uso, a lib precisa ganhar sua própria
  idempotência antes de entrar no pipeline — não dá pra compensar isso só
  do lado do worker quando os bancos são diferentes (sem transação
  distribuída).

- Se a lib grava no **mesmo banco físico** que este worker usa (mesmo
  Postgres do `RetornoDbContext`), dá pra envolver a chamada da lib e o
  `SaveChangesAsync` do worker na mesma transação (compartilhando a
  connection/transaction do EF Core) pra garantir atomicidade de verdade.
  Se for um banco **diferente**, não tem transação distribuída de graça —
  aceite o modelo "falha isolada não derruba o lote, retry natural no
  próximo cron" que já é a filosofia deste worker (ver `README.md`, seção
  Decisões), e garanta que o retry seja seguro (ponto anterior).

## 6. Checklist prático

Quando for implementar de verdade:

1. `git submodule add` + `git submodule update --init --recursive`.
2. Adicionar o passo de submodule ao pipeline de CI/build da imagem Docker.
3. `dotnet sln add` do csproj da lib + `ProjectReference` no Worker.
4. Criar **um único** adaptador em `Conversao/Externo/` (ou pasta análoga)
   que seja o único lugar do projeto com `using` de namespace da lib.
5. Decidir dono da idempotência (item 5) — documentar a decisão como
   comentário no adaptador, porque não é óbvio de fora.
6. Só depois disso, se o pipeline passar de ~10 passos ou algum passo
   precisar de teste isolado, extrair esse passo pra sua própria classe
   (item 4) — não antes.
7. Testes: o adaptador pode ser testado direto (é código puro, sem
   infraestrutura) se a lib for só conversão; se a lib toca banco, siga o
   mesmo padrão de skip suave usado hoje pros testes de S3/pipeline
   (`tests/CnabRetorno.Tests/Integracao/` e `Pipeline/`) — teste real contra
   infraestrutura real, auto-desabilitado quando ela não estiver de pé.
