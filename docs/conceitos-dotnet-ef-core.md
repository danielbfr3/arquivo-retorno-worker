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
// src/CnabRetorno.RetornoSubscriber.Worker/Persistencia/ArquivoRepository.cs
public class ArquivoRepository(CobrancaDbContext db)
{
    public Task<Arquivo?> ObterPorIdAsync(Guid arquivoId, CancellationToken ct)
        => db.Arquivos.FirstOrDefaultAsync(a => a.ArquivoID == arquivoId, ct);
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

- Records: `ArquivoVPendente`, `ConversaoConcluidaMessage`, todos os DTOs
  em `CnabRetorno.Core/Aplicacao/Dtos/` (`DadosConvertidos`/`TituloConvertido`
  e afins) — inclusive usados com `with` pra criar uma cópia modificada sem
  mutação (ver `MesclagemDadosConvertidos.Mesclar`, que faz
  `v with { Titulos = ..., Totais = ... }`).
- Classes: `Cnab240Campos`/`MesclagemDadosConvertidos` (lógica com estado
  intermediário durante a mesclagem), `Arquivo` (entidade EF com
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
| **Singleton** | Recurso caro de criar, seguro pra compartilhar entre requisições/tasks concorrentes | `IAmazonSQS` (`ServiceCollectionExtensions.AddCnabSqsConnection` — client caro de abrir, deve viver o processo inteiro) |
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
// src/CnabRetorno.RetornoCron.Worker/Pipeline/ProcessarArquivosVePvPipeline.cs
await Parallel.ForEachAsync(pendentes, opcoesParalelo, async (pendente, tokenItem) =>
{
    using var escopo = scopeFactory.CreateScope(); // um escopo de DI por arquivo
    var processador = escopo.ServiceProvider.GetRequiredService<ProcessadorArquivoRetornoService>();
    // "processador" resolve seu próprio CobrancaDbContext, isolado dos outros em voo
    ...
});
```

Note que o mesmo cuidado **não** se aplica a `ControleIdempotenciaDiario`
(registrado Singleton, ver Program.cs do Robô 1) — ele precisa ser
compartilhado entre todas as tasks paralelas de propósito (é o mesmo
estado "quais MD5 já processei hoje" que todo arquivo do lote consulta),
por isso usa lock interno em vez de escopo por unidade de trabalho.

Isso usa `IServiceScopeFactory.CreateScope()` — pedir um novo escopo de DI
manualmente, fora do escopo automático que o ASP.NET Core cria por
requisição (que não existe aqui, já que é um Worker, não uma API web).

### `IHostedService` / `BackgroundService`

Todo processo de longa duração do projeto (o loop do Robô 1, o consumidor
SQS do Robô 2) é um `BackgroundService` — classe base que só exige
implementar `ExecuteAsync(CancellationToken)`, chamada automaticamente
quando o host sobe e cancelada quando ele pede shutdown.

```csharp
// src/CnabRetorno.RetornoCron.Worker/RetornoCronWorker.cs
public class RetornoCronWorker(...) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct) { ... }
}
```

```csharp
// Program.cs de cada robô
builder.Services.AddHostedService<RetornoCronWorker>();
```

`SqsConsumerHostedService<TMessage>` (`CnabRetorno.Common`) é a mesma
ideia, só que genérica por tipo de mensagem — um `BackgroundService` com um
laço `while (!ct.IsCancellationRequested)` fazendo long-polling
(`ReceiveMessageAsync`) na fila, porque o trabalho real acontece por
mensagem recebida, não numa sequência linear de passos.

### Options pattern

Três variantes usadas, cada uma pro cenário certo:

1. **`IOptions<T>`** — configuração que não muda em runtime, resolvida uma
   vez. A maioria dos casos: `SqsOptions`, `GestorArquivoOptions`, `OrigemOptions`.
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

`DadosConvertidos`/`TituloConvertido` (`Aplicacao/Dtos/`) são tipados desde
o início, não `JsonElement` genérico — o contrato real da API de conversão
foi confirmado por exemplo real (ver `docs/cash-cobranca-referencia.md`
§2.4), então não há mais motivo pra evitar um tipo forte. O JSON combinado
(V+PV+pendências) é serializado com
`JsonSerializer.SerializeToUtf8Bytes(dados, JsonOpcoesSaida)` e enviado
como o "arquivo" do upload multipart pro conversor assíncrono — ver
`ProcessadorArquivoRetornoService.ProcessarAsync`.

## 4. EF Core

### Nenhum `DbContext` deste projeto é dono de schema

Diferente de um projeto EF Core típico (onde o `DbContext` controla o
schema via `OnModelCreating` + Migrations), `CobrancaDbContext` — presente
nos dois robôs, cada um com sua própria cópia apontando pra mesma base SQL
Server — **não é dono de nada**: mapeia tabelas que já existem, de outro
sistema. Isso muda o tratamento em relação ao uso "padrão" de EF Core:

- **Sem Migrations, nunca** — o schema é de outro sistema; rodar
  `dotnet ef migrations add` aqui não faz sentido.
- **Quase tudo é projeção sem chave** (`HasNoKey()`): o EF Core exige uma
  chave primária pra rastrear entidades entre leituras, mas como a maioria
  das entidades daqui só é lida, elas são tratadas como projeções puras de
  query, sem identidade. Algumas usam `.ToSqlQuery()` com SQL escrito à
  mão (`Titulo`, `InstrucaoComTitulo`), porque `HasNoKey()` não suporta
  `Include`/navegação.

```csharp
// Robô 1 — projeção de leitura, sem chave
mb.Entity<InstrucaoErro>(e =>
{
    e.ToTable("InstrucaoErro", schema: "Instrucao");
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

- **`QueryTrackingBehavior.NoTracking` não impede escrita.** No Robô 1 o
  contexto tem `NoTracking` global (a maioria das operações é leitura), e
  ainda assim `db.Arquivos.Add(...)` + `SaveChangesAsync()` funciona — a
  configuração afeta só o resultado de *consultas*, não entidades que você
  adiciona explicitamente. O Robô 2 **não** usa `NoTracking`, porque lá a
  operação é ler-e-atualizar a mesma entidade: sem tracking, o EF não
  saberia o que mudou no `SaveChangesAsync`.
- **`Remove` funciona em entidade não rastreada.** A compensação do Robô 1
  (`ArquivoRepository.RemoverAsync`) busca com `NoTracking` e chama
  `Remove` — o EF anexa a entidade no estado `Deleted` e gera o DELETE
  normalmente.

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

## 5. Mensageria (AWS SQS — `AWSSDK.SQS`)

Implementado em `CnabRetorno.Common/Mensageria/`: `IMessageService<T>`
como abstração de handler (o contrato que sobrevive a uma eventual troca
de broker), `IAmazonSQS` singleton (client caro de abrir, ver seção 2),
`SqsConsumerHostedService<T>` genérico fazendo long-polling
(`ReceiveMessageAsync`) e o papel de "roteador" entre a fila e o
`IMessageService<T>` handler certo — resolvido num escopo de DI próprio
por mensagem (mesmo raciocínio de thread-safety do `DbContext`, seção 2).
Confirmação (delete da mensagem) só acontece depois do handler retornar
sem lançar exceção; sem delete, a mensagem reaparece sozinha na fila
depois do `VisibilityTimeout`.

## 6. Testes (xUnit)

- **`[Fact]`** — teste sem parâmetro, um cenário.
- **`[Theory]` + `[InlineData]`** — mesmo teste rodado com várias entradas
  (`NomeArquivoRetornoTests.Deve_retornar_falso_para_nome_fora_do_padrao`,
  três nomes de arquivo inválidos diferentes, uma implementação só).
- **Testes de contrato JSON**: `ConvertSyncUploadResponseTests`
  desserializa o exemplo *real* de resposta de `/v1/convert/sync/upload`
  (`docs/cash-cobranca-referencia.md` §2.4) — não um JSON inventado —
  garantindo que os DTOs continuam fiéis ao contrato se alguém mexer neles
  no futuro.
- **Fixture por objeto tipado, não texto posicional**: `MesclagemDadosConvertidosTests`/
  `PendenciasParaTitulosConvertidosFactoryTests` constroem `Titulo`/
  `TituloConvertido`/`DadosConvertidos` direto via `new() { ... }` — como a
  mesclagem hoje é a nível de JSON (DTOs tipados), não sobra a necessidade
  de um builder de linha CNAB posicional pros testes de mesclagem.
- **Sem infraestrutura real nos testes**: o projeto de testes não usa
  mocks nem banco/broker real — tudo que depende de `CobrancaDbContext`/
  `IAmazonSQS` fica de fora da suíte automatizada; a lógica pura (mapeamento,
  mesclagem, parsing) é isolada em classes/métodos testáveis com POCOs.

## 7. Padrões de arquitetura aplicados no projeto

- **YAGNI nas abstrações**: nenhuma interface criada "pra garantir
  flexibilidade futura" — só onde já existe (ou é modelo explícito de) uma
  segunda implementação real. `IArquivoRepository` só existe porque
  poderia ter um fake de teste; `PastaOrigemArquivosRetorno` não tem
  interface porque só existe uma origem possível hoje. Ver
  `docs/evoluindo-com-libs-externas.md` pro raciocínio completo.
- **Regra do adaptador único**: qualquer tipo de uma lib/API externa
  (`AWSSDK.SQS`, o shape da API de conversão, o shape do Gestor de
  Arquivos) é conhecido por **uma única classe** do projeto — os DTOs
  ficam em `Core`, mas quem monta a chamada HTTP de verdade é só
  `LayoutConversaoApiClient`/`GestorArquivosApiClient`. Um breaking change
  na API externa vira um erro de compilação contido num arquivo, não
  espalhado pelo pipeline inteiro.
- **State machine explícita**: `ArquivoRetorno` não expõe setters
  públicos pros seus campos de estado — só métodos com nome de intenção
  (`RegistrarJobConversao`, `Falhar`, `MarcarSemDadoSuficiente`) que
  impõem a transição correta (`AtualizarEtapa` lança exceção se alguém
  tentar regredir). Ver `docs/regras-de-negocio.md` pro diagrama completo.
- **Falha isolada, retry natural**: erro em um arquivo/mensagem não
  derruba o lote inteiro nem trava o processo — vira um contador de falha
  (Robô 1) ou a mensagem simplesmente não é deletada da fila SQS (Robô 2),
  e o item problemático é retentado na próxima execução/redelivery.
