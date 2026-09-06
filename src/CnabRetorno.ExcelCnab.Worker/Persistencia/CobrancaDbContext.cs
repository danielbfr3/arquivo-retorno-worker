using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.ExcelCnab.Worker.Persistencia;

/// <summary>
/// Acesso à base CASH_COBRANCA (SQL Server, existente, de outro time) —
/// mesmo padrão de docs/segunda-fonte-de-dados-sql-server.md: sem
/// migrations daqui, tracking desligado por padrão.
///
/// Duas entidades: <c>Cobranca.Arquivo</c>, que o robô escreve (a linha que
/// cria pra cada planilha antes de mandá-la ao conversor), e
/// <c>Cobranca.DocumentoDados</c>, que o robô só lê (os dados usados pra
/// preencher a planilha antes do envio — ver
/// <see cref="Persistencia.DocumentoDadosRepository"/>).
/// <c>QueryTrackingBehavior.NoTracking</c> só afeta consultas —
/// <c>Add</c> + <c>SaveChangesAsync</c> continuam funcionando normalmente
/// pra <c>Arquivo</c>.
/// </summary>
public class CobrancaDbContext(DbContextOptions<CobrancaDbContext> options) : DbContext(options)
{
    public DbSet<Arquivo> Arquivos => Set<Arquivo>();

    public DbSet<DocumentoDados> DocumentosDados => Set<DocumentoDados>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Só as colunas que este worker usa estão mapeadas; a tabela real
        // tem mais (LayoutBanco, LayoutTipoArquivo, ArquivoCnabID — ver
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

        // Tabela nova (ver deploy/criar-tabela-documento-dados.sql) — schema
        // ainda TODO(a-confirmar), mesma ressalva de Core.Dominio.DocumentoDados.
        mb.Entity<DocumentoDados>(e =>
        {
            e.ToTable("DocumentoDados", schema: "Cobranca");
            e.HasKey(d => d.NumeroDocumento);
            e.Property(d => d.NumeroDocumento).HasColumnName("NumeroDocumento").HasMaxLength(20);
            e.Property(d => d.Dados).HasColumnName("Dados").HasColumnType("nvarchar(max)");
        });
    }
}
