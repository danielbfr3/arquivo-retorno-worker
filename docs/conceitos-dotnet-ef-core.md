# Conceitos de .NET e EF Core usados neste projeto

Referência dos recursos de .NET/C#, EF Core e padrões de arquitetura que
aparecem no código, com o porquê de cada escolha e onde ver um exemplo
real. Não é um tutorial genérico — cada tópico aponta pro arquivo deste
repositório onde ele é usado, pra servir de material de apoio pra quem for
mexer no scaffold sem ter esbarrado nesses recursos antes.

## 1. C# — recursos de linguagem

### Primary constructors (C# 12)

Parâmetro do construtor direto na declaração da classe, sem precisar
escrever campo + construtor + atribuição à mão. Usado em praticamente todo
serviço/repositório do projeto:

```csharp
// src/CnabRetorno.PagamentoRetorno.Worker/Persistencia/ArquivoRepository.cs
public class ArquivoRepository(PagamentoDbContext db, IOptions<RegistroArquivoOptions> opcoes)
{
    public Task RemoverAsync(Guid arquivoId, CancellationToken ct)
        => ...; // "db" e "opcoes" viram campos privados sem nenhuma linha extra
    // "db" já está disponível aqui, sem precisar de "private readonly CobrancaDbContext _db"
}
```

Cuidado real (não só estilo): os parâmetros do primary constructor **só
viram campo de fato se forem usados fora do construtor** — em classes com
métodos que capturam o parâmetro (como acima), o compilador gera um campo
privado por trás dos panos. Isso é diferente de um parâmetro de método
comum; não dá pra reatribuir livremente sem cuidado com o tempo de vida.

### Records vs. classes

Regra usada no projeto: **record para dado imutável sem identidade
própria** (DTOs, mensagens, resultados de query), **classe para entidade
com ciclo de vida e regras de transição de estado**.

- Records: `ArquivoPendente`, `SegmentosRemessa`, `Ocorrencia`,
  `MovimentacoesDoCliente` e todos os DTOs em
  `CnabRetorno.Core/Aplicacao/Dtos/` (`RetornoPagamentoJson`,
  `LotePagamento` e afins) — inclusive usados com `with` pra criar uma
  cópia modificada sem mutação (ver
  `GerarRetornosPagamentoPipeline.ObterDeltaPorClienteAsync`, que faz
  `cliente with { Movimentacoes = novas }`).
- Classes: `Cnab240Campos`/`MontagemRetornoPagamento` (lógica pura sem
  identidade própria), `Arquivo` (entidade EF com
  identidade e campos mutáveis de status/etapa), repositórios e serviços
  de aplicação em geral. Note que `Arquivo` aqui é uma **projeção**, não a
  entidade rica: a máquina de estados de verdade (que valida transição
  status → etapa) mora na cash-cobranca-api, dona da tabela — replicá-la
  aqui criaria duas fontes de verdade. A regra geral continua valendo: um
  tipo que precise impor invariantes vira classe com métodos, não record
  com `init` público.

### Nullable reference types

`<Nullable>enable</Nullable>` em todos os `.csproj`. Isso é o que torna
`string?` (opcional) diferente de `string` (obrigatório) — o compilador
avisa em tempo de build se um `string?` for usado sem checagem de null.
Ver `ConvertCnabParaJsonResponse` (`Aplicacao/Dtos/`): quase todo campo do
contrato JSON é `string?` porque a API de conversão pode devolver `null`
pra campos vazios do CNAB (documentado no próprio exemplo do contrato).

### `throw` como expressão

Usado nos STUBs de propósito que ainda restam no projeto (ex.:
`GestorArquivosApiClient`, contrato do Gestor de Arquivos não confirmado):

```csharp
public Task RegistrarArquivoAsync(...)
    => throw new NotImplementedException("TODO(a-confirmar): ...");
```

`throw` é uma expressão em C#, não só uma instrução — por isso dá pra usar
como corpo de método expression-bodied (`=>`) mesmo com um tipo de retorno
declarado (`JsonElement`); o compilador aceita porque `throw` nunca
retorna, então é compatível com qualquer tipo esperado.

## 2. Injeção de dependência e hosting (Microsoft.Extensions.Hosting)

### Singleton, Scoped e Transient — e por que isso não é só estilo

O ciclo de vida errado quebra em produção de um jeito difícil de
reproduzir em dev (só aparece sob concorrência real). Regra usada:

| Ciclo de vida | Quando usar aqui | Exemplo |
|---|---|---|
| **Singleton** | Recurso caro de criar (ou imutável), seguro pra compartilhar entre tasks concorrentes | `CatalogoMascarasVan` (regex compilada uma vez), `IAmazonS3` no modo S3 do Robô 1 (client caro de abrir, deve viver o processo inteiro) |
| **Scoped** | Recurso barato de criar, **não thread-safe**, deve ter uma instância por "unidade de trabalho" | `CobrancaDbContext`, todos os serviços de aplicação |
| **Transient** | Sem estado nenhum entre chamadas — raro neste projeto especificamente | — |

O caso mais importante do projeto: **`DbContext` do EF Core não é
thread-safe**. Como `ProcessarArquivosVePvPipeline` processa vários
arquivos em paralelo (`Parallel.ForEachAsync`), cada arquivo precisa da
sua própria instância de `CobrancaDbContext` — não dá pra injetar um só
`CobrancaDbContext` Scoped no pipeline e reusar entre iterações paralelas,
porque **duas tasks concorrentes usando o mesmo `DbContext` corrompem o
estado interno dele silenciosamente** (não necessariamente lança exceção
— pode gerar dado errado sem aviso). A solução:

```csharp
// src/CnabRetorno.RemessaVan.Worker/Pipeline/IngerirRemessasVanPipeline.cs
using var escopo = escopos.CreateScope(); // um escopo de DI por arquivo
var processador = escopo.ServiceProvider
    .GetRequiredService<ProcessadorArquivoRemessaService>();
// "processador" resolve seu próprio CobrancaDbContext, isolado dos outros em voo
return (await processador.ProcessarAsync(pendente, ct)).Resultado;
```

Note que o mesmo cuidado **não** se aplica a `CatalogoMascarasVan` e
`NomeArquivoAsa` (registrados Singleton, ver Program.cs do Robô 1) — são
imutáveis depois de construídos (regex compilada uma vez, template lido
uma vez), então compartilhá-los entre todas as tasks é seguro e evita
recompilar a regex a cada arquivo.

Isso usa `IServiceScopeFactory.CreateScope()` — pedir um novo escopo de DI
manualmente, fora do escopo automático que o ASP.NET Core cria por
requisição (que não existe aqui, já que é um Worker, não uma API web).

### `IHostedService` / `BackgroundService`

Todo processo de longa duração do projeto (a varredura do Robô 1, a grade
de janelas do Robô 2) é um `BackgroundService` — classe base que só exige
implementar `ExecuteAsync(CancellationToken)`, chamada automaticamente
quando o host sobe e cancelada quando ele pede shutdown.

```csharp
// src/CnabRetorno.RemessaVan.Worker/RemessaVanWorker.cs
public class RemessaVanWorker(...) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct) { ... }
}
```

```csharp
// Program.cs de cada robô
builder.Services.AddHostedService<RemessaVanWorker>();
```

`PagamentoRetornoWorker` é a mesma ideia com outro gatilho: em vez de uma
expressão cron, um laço que pergunta à `CalculadoraJanelas` qual é a
próxima janela e dorme até lá.

### Options pattern

Três variantes usadas, cada uma pro cenário certo:

1. **`IOptions<T>`** — configuração que não muda em runtime, resolvida uma
   vez. A maioria dos casos: `GestorArquivoOptions`, `OrigemOptions`, `RetornoOptions`.
   ```csharp
   builder.Services.Configure<S3Options>(builder.Configuration.GetSection(S3Options.Secao));
   // consumida via IOptions<S3Options> no construtor
   ```
2. **Named options** — mais de uma configuração do mesmo tipo, distinguidas
   por nome. Usado pros clients HTTP: `ApiClientOptions` é o mesmo tipo
   pra "LayoutConversaoApi" e "GestorArquivosApi", cada um com sua seção:
   ```csharp
   // Program.cs do Robô 1
   builder.Services.Configure<ApiClientOptions>("LayoutConversaoApi", builder.Configuration.GetSection("LayoutConversaoApi"));
   // resolvido via IOptionsMonitor<ApiClientOptions>.Get("LayoutConversaoApi")
   ```
3. **`IOptionsMonitor<T>`** (em vez de `IOptions<T>`) — só necessário
   quando se precisa do `.Get(name)` pra named options, ou quando a config
   pode mudar em runtime (não é o caso aqui, mas o `.Get(name)` exige essa
   interface especificamente).

### `IHttpClientFactory` / `AddHttpClient<TClient, TImplementation>`

Em vez de `new HttpClient()` (que vaza conexões/sockets se usado
incorretamente — problema clássico e bem documentado do .NET), o projeto
usa o factory gerenciado pelo host, com **typed clients**: a interface e a
implementação concreta ficam registradas juntas, e o DI injeta o
`HttpClient` configurado direto no construtor da implementação.

```csharp
// Program.cs do Robô 1
builder.Services.AddHttpClient<ILayoutConversaoApiClient, LayoutConversaoApiClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptionsMonitor<ApiClientOptions>>().Get("LayoutConversaoApi");
    http.BaseAddress = new Uri(opt.BaseUrl);
    http.Timeout = opt.Timeout;
});
```

```csharp
// LayoutConversaoApiClient.cs
public class LayoutConversaoApiClient(HttpClient httpClient) : HttpApiClientBase(httpClient), ILayoutConversaoApiClient
```

O `HttpApiClientBase` (`CnabRetorno.Common/Http/`) é a base fina reusada
pelos dois clients HTTP do projeto (conversão e Gestor de Arquivos) — só
serialização JSON + tratamento de erro padronizado, sem conhecer nada do
domínio de CNAB.

## 3. Serialização JSON (`System.Text.Json`)

Convenção fixa no projeto inteiro: `JsonNamingPolicy.CamelCase` em todo
`JsonSerializerOptions` — o C# usa PascalCase (`NomeEmpresa`), o JSON
trafega em camelCase (`nomeEmpresa`), a policy faz a tradução automática
nos dois sentidos (serializar e desserializar).

`RetornoPagamentoJson` e os tipos aninhados (`Aplicacao/Dtos/`) são
tipados desde o início, não `JsonElement` genérico. O JSON montado é
serializado com `JsonSerializer.SerializeToUtf8Bytes(dados, JsonOpcoes)` e
enviado como o "arquivo" do upload multipart pro conversor síncrono — ver
`ProcessadorRetornoPagamentoService.ProcessarAsync`.

Uma diferença em relação ao envelope de resposta: `ConvertSyncUploadResponse`
decodifica o CNAB em **Latin1**, não UTF-8. O layout é posicional e conta
bytes — um caractere acentuado num nome de favorecido ocuparia duas
posições em UTF-8 e deslocaria a linha inteira.

## 4. EF Core

### Nenhum `DbContext` deste projeto é dono de schema

Diferente de um projeto EF Core típico (onde o `DbContext` controla o
schema via `OnModelCreating` + Migrations), `CobrancaDbContext` (Robô 1) e
`PagamentoDbContext` (Robô 2) apontam pra bases SQL Server de outros
times — **não são donos de nada**: mapeiam tabelas que já existem. Isso
muda o tratamento em relação ao uso "padrão" de EF Core:

- **Sem Migrations, nunca** — o schema é de outro sistema; rodar
  `dotnet ef migrations add` aqui não faz sentido.
- **Quase tudo é projeção sem chave** (`HasNoKey()`): o EF Core exige uma
  chave primária pra rastrear entidades entre leituras, mas como a maioria
  das entidades daqui só é lida, elas são tratadas como projeções puras de
  query, sem identidade. `MovimentacaoPagamento` usa `.ToSqlQuery()` com
  SQL escrito à mão — um `UNION ALL` das cinco duplas de meio de
  pagamento —, algo que navegação de EF não expressaria.
- **A exceção com chave é a tabela de arquivos**, que é escrita, e
  `ControleJanelaRetorno`, a única tabela que este projeto cria.
  `QueryTrackingBehavior.NoTracking` no `OnConfiguring` afeta só consultas:
  `Add`/`SaveChangesAsync` continuam funcionando normalmente.

```csharp
// Robô 1 — projeção de leitura, sem chave
mb.Entity<ParametroCliente>(e =>
{
    e.ToTable("Parametro", schema: "Cobranca");
    e.HasNoKey();
    ...
});
```

**A exceção é `Cobranca.Arquivo`** — a única entidade escrita, e por isso
a única com chave:

```csharp
mb.Entity<Arquivo>(e =>
{
    e.ToTable("Arquivo", schema: "Cobranca");
    e.HasKey(a => a.ArquivoID);   // precisa de identidade: é inserida e atualizada
    ...
});
```

Duas sutilezas que valem registrar:

- **`QueryTrackingBehavior.NoTracking` não impede escrita.** Os dois
  contextos têm `NoTracking` global (a maioria das operações é leitura), e
  ainda assim `db.Arquivos.Add(...)` + `SaveChangesAsync()` funciona — a
  configuração afeta só o resultado de *consultas*, não entidades que você
  adiciona explicitamente.
- **Onde é ler-e-atualizar, pede-se tracking pontual.** O Robô 2 usa
  `.AsTracking()` nas consultas de `MarcarRegistradoAsync`,
  `RemoverAsync` e no avanço da marca d'água: sem tracking o EF não teria
  o estado original pra saber o que mudou no `SaveChangesAsync`. É a
  exceção explícita, não o padrão do contexto.

Registrar um `DbContext` é igual a qualquer outro:

```csharp
// Program.cs de cada robô
builder.Services.AddDbContext<CobrancaDbContext>(opt => opt.UseSqlServer(...));
```

Isso escala pra múltiplos contextos sem problema — não tem limite de
quantos `DbContext` diferentes um processo pode registrar, cada um com seu
próprio provider e connection string, sem interferir entre si. Se algum
robô precisar de uma tabela **própria** algum dia (hoje nenhum tem, ver
`docs/regras-de-negocio.md`), o padrão certo seria um segundo `DbContext`,
dono do seu schema — não misturar isso dentro de `CobrancaDbContext`, que
existe justamente pra falar com um schema de terceiro.

## 5. Mensageria

Não há: nenhum dos dois robôs consome ou publica em fila — o Robô 1 é
ingestão pura e o Robô 2 usa o conversor síncrono. Se um consumidor
voltar a existir, ele nasce na `CnabRetorno.Common`, com o nome da fila
vindo de configuração (nunca literal em código) e o handler resolvido num
escopo de DI próprio por mensagem — mesmo raciocínio de thread-safety do
`DbContext` da seção 2.

## 6. Testes (xUnit)

- **`[Fact]`** — teste sem parâmetro, um cenário.
- **`[Theory]` + `[InlineData]`** — mesmo teste rodado com várias entradas
  (`MascaraVanTests.Deve_casar_as_formas_reais_de_mascara`, cinco formas
  de máscara diferentes, uma implementação só).
- **Testes de contrato JSON**: `ConvertSyncUploadResponseTests` trava o
  comportamento de que o robô depende no envelope do conversor —
  reconhecer texto e base64, decodificar em Latin1 (o layout conta bytes) e
  **falhar alto** quando não vem conteúdo, em vez de gravar um arquivo
  vazio como se fosse legítimo.
- **Fixture por objeto tipado, com um construtor de linha posicional só
  onde é o assunto**: `MontagemRetornoPagamentoTests` monta
  `MovimentacaoPagamento` via `new() { ... }`; os poucos testes que
  precisam de CNAB cru (o de `Linhas` prevalecendo sobre as colunas)
  constroem a linha de 240 posições por helper, porque ali o posicionamento
  **é** o que está sendo testado.
- **Modelo EF sem banco**: `ModeloEfTests` constrói os dois `DbContext` e
  inspeciona o modelo. O EF só abre conexão na primeira consulta, então dá
  pra validar chaves, schemas e propriedades sem coluna sem nenhum SQL
  Server por perto — a única rede de proteção possível num ambiente sem as
  bases.
- **Sem infraestrutura real nos testes**: o projeto de testes não usa
  mocks nem banco/broker real — tudo que depende de conexão viva fica de
  fora da suíte automatizada; a lógica pura (máscaras, nomenclatura, grade
  de janelas, montagem do JSON, parsing posicional) é isolada em
  classes/métodos testáveis com POCOs. Ver
  `docs/riscos-conhecidos.md` item 12 pro que isso deixa descoberto.

## 7. Padrões de arquitetura aplicados no projeto

- **YAGNI nas abstrações**: nenhuma interface criada "pra garantir
  flexibilidade futura" — só onde já existe (ou é modelo explícito de) uma
  segunda implementação real. `IArmazenamentoArquivo` existe porque há
  **duas** implementações de fato (Gestor de Arquivos e S3 direto);
  `PastaOrigemRemessa` não tem interface porque só existe uma origem
  possível hoje. Ver `docs/evoluindo-com-libs-externas.md` pro raciocínio
  completo.
- **Regra do adaptador único**: qualquer tipo de uma lib/API externa
  (`AWSSDK.S3`, o shape da API de conversão, o shape do Gestor de
  Arquivos) é conhecido por **uma única classe** do projeto — os DTOs
  ficam em `Core`, mas quem monta a chamada HTTP de verdade é só
  `LayoutConversaoApiClient`/`GestorArquivosApiClient`. Um breaking change
  na API externa vira um erro de compilação contido num arquivo, não
  espalhado pelo pipeline inteiro.
- **Invariantes moram com o dono da tabela**: `Arquivo` aqui é uma
  projeção deliberadamente burra, sem máquina de estados. A entidade rica
  (que valida transição de status/etapa) vive na API dona da tabela;
  replicá-la daria duas fontes de verdade divergindo com o tempo.
- **Falha isolada, retry natural**: erro em um arquivo não derruba o lote
  nem trava o processo — vira um contador de falha e quarentena (Robô 1)
  ou uma linha removida por compensação (Robô 2), e o item problemático
  volta na próxima execução.
