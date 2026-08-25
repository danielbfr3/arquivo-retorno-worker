# Conceitos de .NET e EF Core usados neste projeto

Referência dos recursos de .NET/C#, EF Core e padrões de arquitetura que
aparecem no código, com o porquê de cada escolha e onde ver um exemplo
real. Não é um tutorial genérico — cada tópico aponta pro arquivo deste
repositório onde ele é usado, pra servir de material de apoio pra quem for
mexer no worker sem ter esbarrado nesses recursos antes.

## 1. C# — recursos de linguagem

### Primary constructors (C# 12)

Parâmetro do construtor direto na declaração da classe, sem precisar
escrever campo + construtor + atribuição à mão. Usado em praticamente todo
serviço/repositório do projeto:

```csharp
// src/CnabRetorno.ExcelCnab.Worker/Persistencia/ArquivoRepository.cs
public class ArquivoRepository(CobrancaDbContext db, IOptions<RegistroArquivoOptions> opcoes)
{
    // "db" já está disponível aqui, sem "private readonly CobrancaDbContext _db"
    public Task MarcarInvalidoAsync(Guid arquivoId, CancellationToken ct)
        => db.Arquivos...;
}
```

Cuidado real (não só estilo): os parâmetros do primary constructor **só
viram campo de fato se forem usados fora do construtor** — em classes com
métodos que capturam o parâmetro (como acima), o compilador gera um campo
privado por trás dos panos. Repare que `opcoes` no exemplo é capturado uma
vez em `_opt` no corpo da classe: quando o que interessa é o `.Value`, é
mais honesto materializá-lo do que reavaliar a propriedade a cada uso.

### Records vs. classes

Regra usada no projeto: **record para dado imutável sem identidade
própria** (DTOs, mensagens, resultados de query), **classe para entidade
com ciclo de vida e regras de transição de estado**.

- Records: `ArquivoPendente`, `NomeReconhecido`, `PlanilhaProcessada`,
  `ResumoVarredura` e os DTOs em `CnabRetorno.Core/Aplicacao/Dtos/`
  (`ConvertAsyncUploadResponse`, `MetadadosCliente`).
- Classes: `Arquivo` e `EmpresaAdesao` (entidades EF, com identidade),
  repositórios e serviços de aplicação em geral. Note que `Arquivo` aqui é
  uma **projeção**, não a entidade rica: a máquina de estados de verdade
  (que valida transição status → etapa) mora na cash-cobranca-api, dona da
  tabela — replicá-la aqui criaria duas fontes de verdade. A regra geral
  continua valendo: um tipo que precise impor invariantes vira classe com
  métodos, não record com `init` público.

Um caso que mistura os dois de propósito: `MetadadosCliente` é um record
posicional **com um método** (`Serializar()`). Ele é dado imutável, mas o
formato do JSON é contrato com o conversor — deixar a serialização junto
do tipo impede que dois lugares serializem o mesmo payload com opções
diferentes.

### Nullable reference types

`<Nullable>enable</Nullable>` em todos os `.csproj`. Isso é o que torna
`string?` (opcional) diferente de `string` (obrigatório) — o compilador
avisa em tempo de build se um `string?` for usado sem checagem de null.
Ver `ConvertAsyncUploadResponse`: `JobId`, `Status` e `StatusUrl` são
anuláveis porque uma resposta 200 malformada é possível, e é justamente
por isso que `Aceito` checa o status em vez de assumir que ele veio.

O mesmo aparece em `EmpresaAdesao.RazaoSocial` (`string?`): a coluna pode
estar vazia na base de adesão, e o processador é obrigado pelo compilador
a tratar esse caso — que é exatamente o que manda o arquivo pra
quarentena em vez de enviar um JSON com razão social nula.

## 2. Injeção de dependência e hosting (Microsoft.Extensions.Hosting)

### Singleton, Scoped e Transient — e por que isso não é só estilo

O ciclo de vida errado quebra em produção de um jeito difícil de
reproduzir em dev (só aparece sob concorrência real). Regra usada:

| Ciclo de vida | Quando usar aqui | Exemplo |
|---|---|---|
| **Singleton** | Recurso caro de criar (ou imutável), seguro pra compartilhar entre tasks concorrentes | `NomeArquivoSimplificado` (regex compilada uma vez), `TimeProvider.System` |
| **Scoped** | Recurso barato de criar, **não thread-safe**, deve ter uma instância por "unidade de trabalho" | `CobrancaDbContext`, `AdesaoDbContext`, todos os serviços de aplicação |
| **Transient** | Sem estado nenhum entre chamadas — raro neste projeto especificamente | — |

O caso mais importante do projeto: **`DbContext` do EF Core não é
thread-safe**. Como `EnviarPlanilhasPipeline` processa vários arquivos em
paralelo, cada arquivo precisa das suas próprias instâncias de
`DbContext` — não dá pra injetar um `CobrancaDbContext` Scoped no pipeline
e reusá-lo entre iterações paralelas, porque **duas tasks concorrentes
usando o mesmo `DbContext` corrompem o estado interno dele
silenciosamente** (não necessariamente lança exceção — pode gerar dado
errado sem aviso). A solução:

```csharp
// src/CnabRetorno.ExcelCnab.Worker/Pipeline/EnviarPlanilhasPipeline.cs
using var escopo = escopos.CreateScope(); // um escopo de DI por arquivo
var processador = escopo.ServiceProvider
    .GetRequiredService<ProcessadorArquivoExcelService>();
// "processador" resolve seus próprios DbContext, isolados dos outros em voo
return (await processador.ProcessarAsync(pendente, ct)).Resultado;
```

Note que o mesmo cuidado **não** se aplica a `NomeArquivoSimplificado`
(registrado Singleton, ver `Program.cs`) — é imutável depois de construído
(regex compilada uma vez), então compartilhá-lo entre todas as tasks é
seguro e evita recompilar a regex a cada arquivo.

Isso usa `IServiceScopeFactory.CreateScope()` — pedir um novo escopo de DI
manualmente, fora do escopo automático que o ASP.NET Core cria por
requisição (que não existe aqui, já que é um Worker, não uma API web).

### `IHostedService` / `BackgroundService`

O processo de longa duração do projeto (a varredura da pasta) é um
`BackgroundService` — classe base que só exige implementar
`ExecuteAsync(CancellationToken)`, chamada automaticamente quando o host
sobe e cancelada quando ele pede shutdown.

```csharp
// src/CnabRetorno.ExcelCnab.Worker/ExcelCnabWorker.cs
public class ExcelCnabWorker(...) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct) { ... }
}
```

```csharp
// Program.cs
builder.Services.AddHostedService<ExcelCnabWorker>();
```

O mesmo `BackgroundService` atende os dois modos de operação
(`Worker:Modo`): em `CronJob` ele roda um ciclo e chama
`lifetime.StopApplication()`; em `Loop` ele dorme até a próxima ocorrência
da expressão cron. É `IHostApplicationLifetime` que permite ao serviço
encerrar o host inteiro — sem isso, o processo ficaria vivo depois de
terminar o único ciclo que tinha pra fazer.

### Options pattern

Três variantes usadas, cada uma pro cenário certo:

1. **`IOptions<T>`** — configuração que não muda em runtime, resolvida uma
   vez. A maioria dos casos: `OrigemOptions`, `NomenclaturaOptions`,
   `ConversaoOptions`, `RegistroArquivoOptions`.
   ```csharp
   builder.Services.Configure<OrigemOptions>(builder.Configuration.GetSection(OrigemOptions.Secao));
   // consumida via IOptions<OrigemOptions> no construtor
   ```
2. **Named options** — mais de uma configuração do mesmo tipo,
   distinguidas por nome. `ApiClientOptions` é um tipo genérico de client
   HTTP; hoje só a seção "LayoutConversaoApi" o usa, e o registro nomeado
   fica porque é o que permite uma segunda API entrar sem renomear nada:
   ```csharp
   // Program.cs
   builder.Services.Configure<ApiClientOptions>(
       "LayoutConversaoApi", builder.Configuration.GetSection("LayoutConversaoApi"));
   // resolvido via IOptionsMonitor<ApiClientOptions>.Get("LayoutConversaoApi")
   ```
3. **`IOptionsMonitor<T>`** (em vez de `IOptions<T>`) — só necessário
   quando se precisa do `.Get(name)` pra named options, ou quando a config
   pode mudar em runtime (não é o caso aqui, mas o `.Get(name)` exige essa
   interface especificamente).

A regra que atravessa o projeto: **nome de recurso de infra é
configuração**, nunca literal em código — pasta de origem, base URL,
AppID, nome do pipeline, máscara do nome do arquivo, nome do campo de
metadados. Ver a tabela de configuração no `README.md`.

### `IHttpClientFactory` / `AddHttpClient<TClient, TImplementation>`

Em vez de `new HttpClient()` (que vaza conexões/sockets se usado
incorretamente — problema clássico e bem documentado do .NET), o projeto
usa o factory gerenciado pelo host, com **typed clients**: a interface e a
implementação concreta ficam registradas juntas, e o DI injeta o
`HttpClient` configurado direto no construtor da implementação.

```csharp
// Program.cs
builder.Services.AddHttpClient<ILayoutConversaoApiClient, LayoutConversaoApiClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptionsMonitor<ApiClientOptions>>().Get("LayoutConversaoApi");
    http.BaseAddress = new Uri(opt.BaseUrl);
    http.Timeout = opt.Timeout;
}).AddStandardResilienceHandler();
```

```csharp
// Http/LayoutConversaoApiClient.cs
public class LayoutConversaoApiClient(HttpClient httpClient, IOptions<ConversaoOptions> opcoes)
    : HttpApiClientBase(httpClient), ILayoutConversaoApiClient
```

`AddStandardResilienceHandler()` (pacote
`Microsoft.Extensions.Http.Resilience`) embrulha o client com retry,
circuit breaker e timeout padrão — vale notar que **retry automático num
POST não é inofensivo**: aqui é aceitável porque o `id` da conversão é o
`ArquivoID`, o mesmo em toda tentativa, então o outro lado consegue
reconhecer a repetição.

O `HttpApiClientBase` (`CnabRetorno.Common/Http/`) é a base fina do client
HTTP — só o POST multipart + desserialização e tratamento de erro
padronizados, sem conhecer nada de planilha ou CNAB.

## 3. Serialização JSON (`System.Text.Json`)

Convenção fixa no projeto inteiro: `JsonNamingPolicy.CamelCase` em todo
`JsonSerializerOptions` — o C# usa PascalCase (`RazaoSocial`), o JSON
trafega em camelCase (`razaoSocial`), a policy faz a tradução automática
nos dois sentidos (serializar e desserializar).

Onde o nome do campo é contrato explícito, ele é escrito à mão com
`[JsonPropertyName]` em vez de depender da policy — é o caso de
`MetadadosCliente`, o JSON que vai no corpo da mensagem do conversor.
Depender da convenção ali significaria que renomear uma propriedade em C#
mudaria silenciosamente o payload que outro time consome.

`MetadadosCliente.Serializar()` usa `UnsafeRelaxedJsonEscaping`. O nome
assusta mais do que deveria: o escape agressivo padrão existe pra
contextos onde o JSON é embutido em HTML, e aqui o payload vai num campo
de multipart. Sem isso, "COMÉRCIO" viraria `COMÉRCIO` — válido, mas
ilegível pra quem inspeciona o payload do outro lado.

## 4. EF Core

### Nenhum `DbContext` deste projeto é dono de schema

Diferente de um projeto EF Core típico (onde o `DbContext` controla o
schema via `OnModelCreating` + Migrations), `CobrancaDbContext` e
`AdesaoDbContext` apontam pra bases SQL Server de outros times — **não são
donos de nada**: mapeiam tabelas que já existem. Isso muda o tratamento em
relação ao uso "padrão" de EF Core:

- **Sem Migrations, nunca** — o schema é de outro sistema; rodar
  `dotnet ef migrations add` aqui não faz sentido.
- **Só as colunas usadas são mapeadas.** `Cobranca.Arquivo` tem mais
  colunas do que as que aparecem no `OnModelCreating` (ver
  `docs/cash-cobranca-referencia.md` §1.1); as que o worker não lê nem
  preenche ficam de fora de propósito.
- **`QueryTrackingBehavior.NoTracking` no `OnConfiguring`** — a maioria
  das operações é leitura, e tracking custa memória e tempo à toa.

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
  contextos têm `NoTracking` global e ainda assim `db.Arquivos.Add(...)` +
  `SaveChangesAsync()` funciona — a configuração afeta só o resultado de
  *consultas*, não entidades que você adiciona explicitamente.
- **Update sem carregar a entidade.** `MarcarInvalidoAsync` usa
  `ExecuteUpdateAsync`, que traduz direto pra um `UPDATE ... WHERE` — sem
  SELECT antes, sem tracking, sem trazer a linha pra memória. É o caminho
  certo quando se sabe exatamente o que mudar e não se precisa do estado
  anterior; a alternativa (carregar com `.AsTracking()` e alterar a
  propriedade) só se justifica quando a decisão depende do valor atual.

Registrar um `DbContext` é igual a qualquer outro:

```csharp
// Program.cs
builder.Services.AddDbContext<CobrancaDbContext>(opt => opt.UseSqlServer(...));
builder.Services.AddDbContext<AdesaoDbContext>(opt => opt.UseSqlServer(...));
```

Isso escala pra múltiplos contextos sem problema — não tem limite de
quantos `DbContext` diferentes um processo pode registrar, cada um com seu
próprio provider e connection string, sem interferir entre si. Se o worker
precisar de uma tabela **própria** algum dia (hoje não tem nenhuma), o
padrão certo seria um terceiro `DbContext`, dono do seu schema — não
misturar isso dentro de `CobrancaDbContext`, que existe justamente pra
falar com um schema de terceiro.

### Lock entre réplicas usando o banco que já existe

`LockExecucaoExclusiva` usa `sp_getapplock`/`sp_releaseapplock` via ADO
puro (não `SqlQuery`: `EXEC` não sobrevive ao embrulho em subselect que o
EF faz). Duas coisas importam ali: o lock é **de sessão**, então a conexão
precisa ficar aberta enquanto ele viver — e é isso que garante liberação
automática se o pod morrer no meio —, e `@LockTimeout = 0` faz a réplica
perdedora desistir na hora em vez de enfileirar pra varrer tudo de novo
logo depois.

## 5. Mensageria

Não há: o worker não consome nem publica em fila. Ele entrega a planilha
ao conversor assíncrono e termina no aceite; a mensagem de conclusão é
tratada por outro worker do ecossistema. Se um consumidor passar a existir
aqui, ele nasce na `CnabRetorno.Common`, com o nome da fila vindo de
configuração (nunca literal em código) e o handler resolvido num escopo de
DI próprio por mensagem — mesmo raciocínio de thread-safety do
`DbContext` da seção 2.

## 6. Testes (xUnit)

- **`[Fact]`** — teste sem parâmetro, um cenário.
- **`[Theory]` + `[InlineData]`** — mesmo teste rodado com várias entradas
  (`NomeArquivoSimplificadoTests.Recusa_o_que_nao_esta_exatamente_no_padrao`,
  dez nomes que não podem ser aceitos, uma implementação só).
- **Testar o que não pode acontecer, não só o caminho feliz.** O CNPJ do
  nome do arquivo é a única identificação do cliente no fluxo inteiro — um
  falso positivo mandaria a planilha de um cliente com o documento de
  outro. Por isso a suíte cobre mais recusas do que aceites.
- **Testes de contrato JSON**: `ConvertAsyncUploadResponseTests` e
  `MetadadosClienteTests` travam os dois lados da conversa com o conversor
  — o que o robô manda e o que ele aceita como resposta. Incluem o caso de
  campo desconhecido no corpo: a API pode passar a mandar mais campos, e
  isso não pode derrubar um envio que foi aceito.
- **Modelo EF sem banco**: `ModeloEfTests` constrói os dois `DbContext` e
  inspeciona o modelo. O EF só abre conexão na primeira consulta, então dá
  pra validar chaves, schemas e propriedades sem coluna sem nenhum SQL
  Server por perto — a única rede de proteção possível num ambiente sem as
  bases.
- **Sem infraestrutura real nos testes**: o projeto de testes não usa
  mocks nem banco/HTTP real — tudo que depende de conexão viva fica de
  fora da suíte automatizada; a lógica pura (leitura do nome, serialização
  do payload, leitura do envelope de resposta) é isolada em classes
  testáveis com POCOs. Ver `docs/riscos-conhecidos.md` pro que isso deixa
  descoberto.

## 7. Padrões de arquitetura aplicados no projeto

- **YAGNI nas abstrações**: nenhuma interface criada "pra garantir
  flexibilidade futura" — só onde já existe (ou é modelo explícito de) uma
  segunda implementação real. `ILayoutConversaoApiClient` existe porque
  separa o contrato (em `Core`, sem dependência externa) da implementação
  HTTP concreta; `PastaOrigemExcel` **não** tem interface porque só existe
  uma origem possível hoje, e um diretório local e um SMB montado são o
  mesmo `Directory.EnumerateFiles` pro código. Ver
  `docs/evoluindo-com-libs-externas.md` pro raciocínio completo.
- **Regra do adaptador único**: o shape da API de conversão é conhecido
  por **uma única classe** do projeto — os DTOs ficam em `Core`, mas quem
  monta a chamada HTTP de verdade é só `LayoutConversaoApiClient`. Um
  breaking change na API externa vira um erro de compilação contido num
  arquivo, não espalhado pelo pipeline inteiro.
- **Invariantes moram com o dono da tabela**: `Arquivo` aqui é uma
  projeção deliberadamente burra, sem máquina de estados. A entidade rica
  (que valida transição de status/etapa) vive na API dona da tabela;
  replicá-la daria duas fontes de verdade divergindo com o tempo.
- **Falha isolada, retry natural**: erro em um arquivo não derruba a
  varredura nem trava o processo — vira um contador de falha e uma ida pra
  quarentena, e o resto dos arquivos segue.
- **Ordem das escritas é decisão de projeto**: a linha em
  `Cobranca.Arquivo` nasce **antes** do envio ao conversor, porque a
  conclusão assíncrona se ancora nela pelo `ArquivoID`. A ordem inversa
  seria mais simples de escrever e deixaria conclusões órfãs chegando.
