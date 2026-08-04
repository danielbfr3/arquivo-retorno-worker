using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.RemessaVan.Worker.Persistencia;

/// <summary>Projeção só-leitura de <c>Cobranca.Parametro</c> — a linha de
/// parâmetros do cliente, chaveada pelo documento. Aqui só interessa a
/// conta do header.</summary>
public class ParametroCliente
{
    public string Documento { get; init; } = default!;
    public string? ContaHeader { get; init; }
}

/// <summary>
/// Acesso à base CASH_COBRANCA (SQL Server, existente, de outro time) —
/// mesmo padrão de docs/segunda-fonte-de-dados-sql-server.md: sem
/// migrations daqui, tracking desligado por padrão.
///
/// Duas entidades só: <see cref="Arquivos"/>, a única **escrita** (é a
/// linha que o robô cria pra cada remessa ingerida), e <see
/// cref="Parametros"/>, leitura pura pra resolver o <c>ContaHeader</c> do
/// cliente a partir do CNPJ extraído do nome do arquivo.
/// <c>QueryTrackingBehavior.NoTracking</c> só afeta consultas — <c>Add</c>
/// + <c>SaveChangesAsync</c> continuam funcionando normalmente.
/// </summary>
public class CobrancaDbContext(DbContextOptions<CobrancaDbContext> options) : DbContext(options)
{
    public DbSet<Arquivo> Arquivos => Set<Arquivo>();
    public DbSet<ParametroCliente> Parametros => Set<ParametroCliente>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Única entidade com chave do contexto — é a única escrita. Só as
        // colunas que este worker usa estão mapeadas; a tabela real tem
        // mais (LayoutBanco, LayoutTipoArquivo, ArquivoCnabID — ver
        // docs/cash-cobranca-referencia.md §1.1), deixadas de fora porque
        // o robô não preenche nem lê.
        mb.Entity<Arquivo>(e =>
        {
            e.ToTable("Arquivo", schema: "Cobranca");
            e.HasKey(a => a.ArquivoID);
            e.Property(a => a.ArquivoID).HasColumnName("ArquivoID");
            e.Property(a => a.AppID).HasColumnName("AppID").HasMaxLength(100);
            e.Property(a => a.ArquivoNome).HasColumnName("ArquivoNome").HasMaxLength(250);
            e.Property(a => a.ClienteContaHeader).HasColumnName("ClienteContaHeader").HasMaxLength(10);
            e.Property(a => a.ClienteTipoDocumento).HasColumnName("ClienteTipoDocumento");
            e.Property(a => a.ClienteDocumento).HasColumnName("ClienteDocumento").HasMaxLength(20);
            e.Property(a => a.CriadoPor).HasColumnName("CriadoPor").HasMaxLength(50);
            e.Property(a => a.DescricaoProduto).HasColumnName("DescricaoProduto");
            e.Property(a => a.DataCriacao).HasColumnName("DataCriacao");
            e.Property(a => a.DataAtualizacao).HasColumnName("DataAtualizacao");
            e.Property(a => a.ArquivoStatus).HasColumnName("ArquivoStatus");
            e.Property(a => a.ArquivoEtapa).HasColumnName("ArquivoEtapa");
        });

        mb.Entity<ParametroCliente>(e =>
        {
            e.ToTable("Parametro", schema: "Cobranca");
            e.HasNoKey();
            e.Property(p => p.Documento).HasColumnName("Documento").HasMaxLength(20);
            // TODO(a-confirmar): o nome real da coluna de conta em
            // Cobranca.Parametro não foi capturado — só SequencialAtual e
            // Documento aparecem no material. Se for outro nome, é aqui
            // que se corrige (uma linha), não no repositório.
            e.Property(p => p.ContaHeader).HasColumnName("ContaHeader").HasMaxLength(10);
        });
    }
}
