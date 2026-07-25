# Segunda fonte de dados: código do cliente no core bancário via EF Core (SQL Server)

> **Nota:** escrito para a arquitetura de projeto único (`CnabRetorno.Worker`)
> anterior aos dois robôs — inclusive motivou a criação deles. O padrão
> `CobrancaDbContext` somente-leitura descrito abaixo já está implementado
> de verdade em `src/CnabRetorno.RetornoCron.Worker/Persistencia/CobrancaDbContext.cs`
> e na cópia própria em `CnabRetorno.RetornoSubscriber.Worker`, agora com
> `ParametroRetorno`/`RecusaErroProcessamento` (ver `README.md` raiz).
> Mantido como referência conceitual; caminhos de arquivo abaixo
> (`src/CnabRetorno.Worker/...`) são do design anterior.

Tutorial para adicionar uma leitura à "base de cobrança" (SQL Server,
existente, fora do controle deste projeto) e obter o código do cliente no core bancário do
cliente **antes** de listar/buscar os arquivos pendentes.

Resposta curta pra pergunta que motivou este doc: sim, dá pra fazer com EF
Core. Um mesmo processo .NET pode ter quantos `DbContext` fizer sentido,
cada um com seu próprio provider e connection string — não precisa ser tudo
o mesmo banco nem o mesmo motor. Hoje este projeto só tem o
`RetornoDbContext` (Postgres, schema que o próprio worker é dono e versiona
via EF migrations). O que muda aqui é o *tipo* de relação com o banco: a
base de cobrança é uma base **existente, de outro sistema**, e este worker
só vai *ler* dela — nunca migrar, nunca escrever.

## 1. Pacote

```xml
<!-- src/CnabRetorno.Worker/CnabRetorno.Worker.csproj -->
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.0" />
```

(Se um dia descobrir que na verdade é outro motor, troca só esse pacote e o
`UseSqlServer(...)` do passo 4 — nada mais no projeto depende do motor
específico.)

## 2. Connection string

```json
// src/CnabRetorno.Worker/appsettings.json
{
  "ConnectionStrings": {
    "Retorno": "...",
    "Cobranca": "Server=SEU_SERVIDOR;Database=Cobranca;User Id=worker_readonly;Password=...;TrustServerCertificate=True"
  }
}
```

Recomendação forte: o usuário de banco usado aqui deve ter permissão só de
`SELECT` nas tabelas/views necessárias. Isso não é o EF Core impondo nada —
é uma segunda camada de proteção (além do código) contra o worker escrever
sem querer numa base que não é dele.

## 3. O DbContext — mapeando uma base que você não versiona

Diferença chave em relação ao `RetornoDbContext`: lá, `OnModelCreating`
define o schema porque o worker é dono dele (migrations criam as tabelas).
Aqui, o schema já existe e pertence a outro sistema — o `DbContext` só
descreve o suficiente pra ler o que precisa, e nunca deve rodar
`Database.Migrate()` nem `EnsureCreated()`.

```csharp
// src/CnabRetorno.Worker/Cobranca/CobrancaDbContext.cs
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.Worker.Cobranca;

/// <summary>
/// Leitura da base de cobrança existente (SQL Server, outro sistema).
/// Somente leitura por design: sem DbSet de escrita, sem migrations daqui,
/// tracking desligado por padrão (não há por que rastrear mudanças em
/// entidades que este worker nunca vai salvar de volta).
/// </summary>
public class CobrancaDbContext(DbContextOptions<CobrancaDbContext> options) : DbContext(options)
{
    public DbSet<ClienteCobranca> ClientesCobranca => Set<ClienteCobranca>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<ClienteCobranca>(e =>
        {
            // AJUSTE nome de tabela/schema/colunas para os reais da base
            // de cobrança — os nomes abaixo são placeholder.
            e.ToTable("cliente", schema: "cobranca");
            e.HasKey(c => c.ClienteId);
            e.Property(c => c.ClienteId).HasColumnName("cnpj").HasMaxLength(14);
            e.Property(c => c.CodigoCoreBancario).HasColumnName("codigo_core_bancario").HasMaxLength(20);

            // Nunca chame `mb.Entity<ClienteCobranca>().ToTable(...).ExcludeFromMigrations()`
            // como se isso bastasse — o certo é este DbContext nunca ser
            // alvo de `dotnet ef migrations add`. Se algum dia rodar
            // migrations no projeto pra outro DbContext, confirme que o
            // comando aponta pro RetornoDbContext, não pro CobrancaDbContext.
        });
    }
}

/// <summary>Projeção mínima da base de cobrança — só os campos que este
/// worker realmente usa, não a tabela inteira.</summary>
public sealed class ClienteCobranca
{
    public required string ClienteId { get; init; }     // mesma chave usada em ArquivoPendente.ClienteId
    public required string CodigoCoreBancario { get; init; }
}
```

Se a fonte real for uma **view** (comum quando você não quer depender
diretamente de tabelas internas de outro sistema — pede pro time dono uma
view estável), o mapeamento é o mesmo, só troca `schema`/nome, e pode fazer
sentido usar `.HasNoKey()` em vez de `.HasKey(...)` se a view não tiver uma
coluna que sirva de identificador único.

## 4. Registrar no host

```csharp
// src/CnabRetorno.Worker/Program.cs
using CnabRetorno.Worker.Cobranca;

builder.Services.AddDbContext<CobrancaDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Cobranca")));
```

Igual ao `RetornoDbContext`, é registrado como `Scoped` por padrão (é o que
`AddDbContext` faz) — então, seguindo o mesmo motivo já documentado no
`README.md` (EF `DbContext` não é thread-safe), uma instância de
`CobrancaDbContext` só deve ser usada dentro de um único escopo de DI por
vez, do mesmo jeito que `RetornoDbContext` já é usado hoje dentro do escopo
por arquivo em `ProcessarRetornoPipeline`.

## 5. Onde encaixar a consulta no pipeline

Como o nome do arquivo carrega o código do cliente (não a estrutura de
pastas — diferente do `PastaLocalOrigem` atual, que infere `ClienteId` do
nome da subpasta), a descoberta dos arquivos pendentes passa a ser guiada
pela lista de clientes, não pelo disco. O passo novo entra em
`ProcessarRetornoPipeline.ExecutarAsync`, **antes** da listagem de
pendentes — `ProcessadorArquivoRetorno` (passos 2-7, idempotência →
conversão → persistência) não muda nada, porque continua recebendo a mesma
`ArquivoPendente` de sempre, só que agora com o `ClienteId` resolvido por
outro caminho.

```csharp
// src/CnabRetorno.Worker/Cobranca/CobrancaLookup.cs
namespace CnabRetorno.Worker.Cobranca;

public class CobrancaLookup(CobrancaDbContext db)
{
    /// <summary>Lista completa de clientes ativos e seus códigos —
    /// carregada uma vez por execução do pipeline, não por arquivo.</summary>
    public Task<List<ClienteCobranca>> ObterClientesAsync(CancellationToken ct)
        => db.ClientesCobranca.ToListAsync(ct);
}
```

A busca dos arquivos passa a iterar essa lista, cliente por cliente,
casando o código contra o nome do arquivo:

```csharp
// src/CnabRetorno.Worker/Origem/PastaLocalOrigem.cs
public Task<IReadOnlyList<ArquivoPendente>> ListarPendentesAsync(
    IReadOnlyList<ClienteCobranca> clientes, CancellationToken ct)
{
    if (!Directory.Exists(_opt.Raiz))
        return Task.FromResult<IReadOnlyList<ArquivoPendente>>([]);

    var arquivosNaPasta = Directory
        .EnumerateFiles(_opt.Raiz, _opt.Filtro, SearchOption.TopDirectoryOnly)
        .ToList();

    var pendentes = new List<ArquivoPendente>();
    foreach (var cliente in clientes)
    {
        var doCliente = arquivosNaPasta
            .Where(caminho => NomeContemCodigo(Path.GetFileName(caminho), cliente.CodigoCoreBancario))
            .Select(caminho => new ArquivoPendente(
                caminho, Path.GetFileName(caminho), cliente.ClienteId));
        pendentes.AddRange(doCliente);
    }

    return Task.FromResult<IReadOnlyList<ArquivoPendente>>(
        pendentes.OrderBy(a => a.Caminho, StringComparer.Ordinal).ToList());
}

// Placeholder — ver observação abaixo sobre por que `Contains` puro é
// arriscado. Ajuste pro formato real do nome do arquivo.
private static bool NomeContemCodigo(string nomeArquivo, string codigoCliente)
    => nomeArquivo.Contains(codigoCliente, StringComparison.OrdinalIgnoreCase);
```

```csharp
// ProcessarRetornoPipeline.ExecutarAsync — passo novo antes da listagem
using var escopoClientes = scopeFactory.CreateScope();
var lookup = escopoClientes.ServiceProvider.GetRequiredService<CobrancaLookup>();
var clientes = await lookup.ObterClientesAsync(ct);
logger.LogInformation("Encontrados {Qtd} cliente(s) na base de cobrança", clientes.Count);

var pendentes = await origem.ListarPendentesAsync(clientes, ct);
// resto do método (Parallel.ForEachAsync sobre `pendentes`) fica igual
```

Dois cuidados que valem a pena resolver antes de colocar em produção:

- **Casar por substring é arriscado se os códigos não forem de tamanho
  fixo.** `NomeContemCodigo` acima é um placeholder de propósito — se o
  código do cliente for, por exemplo, `"123"`, um `Contains` ingênuo
  também bate em um arquivo do cliente `"1234"`. Se os códigos forem
  largura fixa (o `HasMaxLength(20)` do mapeamento em `ClienteCobranca`
  sugere isso), prefira extrair o trecho do nome do arquivo na posição
  esperada e comparar por igualdade, ou usar um separador conhecido no
  nome do arquivo (ex.: `RET_00123_20260715.RET` → `nome.Split('_')[1] ==
  cliente.CodigoCoreBancario`). O formato real do nome do arquivo é o que decide
  qual das duas abordagens usar.
- **Arquivo cujo nome não bate com nenhum cliente da lista** hoje
  simplesmente não entra em `pendentes` — some silenciosamente. Vale logar
  os arquivos da pasta que não casaram com nenhum código conhecido (um
  `Except` entre `arquivosNaPasta` e os que entraram em `pendentes`), do
  mesmo jeito que o pipeline já loga o resumo de processados/duplicados/
  falhas — um arquivo órfão geralmente é sinal de cliente cadastrado errado
  na base de cobrança, não de arquivo pra ignorar.

## 6. Testes

Sem acesso real a essa base de cobrança em dev/CI, duas opções, seguindo
convenções já usadas neste projeto:

- **EF Core InMemory** (mesmo pacote já usado nos testes de
  `ProcessarRetornoPipelineTests`) — funciona bem aqui porque
  `CobrancaDbContext` é simples (uma projeção read-only, sem relações
  complexas de SQL Server pra emular). Dá pra popular um
  `CobrancaDbContext` InMemory com alguns `ClienteCobranca` de teste e testar
  `CobrancaLookup` isoladamente, rápido e sem infraestrutura.
- **Skip suave contra SQL Server real**, só se precisar validar algo
  específico do provider (uma view complexa, uma stored procedure) que o
  InMemory não reproduz fielmente — mesmo padrão de
  `tests/CnabRetorno.Tests/Integracao/S3StorageArquivosIntegracaoTests.cs`:
  tenta conectar, se não conseguir, o teste retorna sem falhar.

## 7. Resumo do que muda no projeto

| Item | Novo |
|---|---|
| Pacote | `Microsoft.EntityFrameworkCore.SqlServer` |
| Config | `ConnectionStrings:Cobranca` em `appsettings.json`/`appsettings.Local.json` |
| Pasta | `src/CnabRetorno.Worker/Cobranca/` — `CobrancaDbContext.cs`, `ClienteCobranca.cs`, `CobrancaLookup.cs` |
| DI | `AddDbContext<CobrancaDbContext>` em `Program.cs` |
| `Origem/PastaLocalOrigem.cs` | `ListarPendentesAsync` passa a receber a lista de clientes e casar pelo nome do arquivo, em vez de inferir `ClienteId` da subpasta |
| Pipeline | `ProcessarRetornoPipeline.ExecutarAsync` busca a lista de clientes antes de listar pendentes; `ProcessadorArquivoRetorno` não muda |

Nenhuma interface nova aqui — mesma lógica do resto do projeto: uma classe
concreta (`CobrancaLookup`) por trás de um `DbContext` concreto, sem porta
intermediária, porque não existe hoje uma segunda "base de cobrança" pra
trocar.
