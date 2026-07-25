using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.RetornoSubscriber.Worker.Persistencia;

/// <summary>
/// Acesso à base CASH_COBRANCA (SQL Server, existente, outro sistema) —
/// mesmo padrão de docs/segunda-fonte-de-dados-sql-server.md: sem
/// migrations daqui.
///
/// Cópia deliberadamente **mínima** da do Robô 1: este robô só precisa de
/// <c>Cobranca.Arquivo</c> — busca a linha pelo ID que veio na mensagem
/// SQS (é o mesmo ID que o Robô 1 mandou pro conversor) e avança
/// status/etapa quando termina de registrar o arquivo final. Cada robô tem
/// sua própria cópia do contexto porque eles vão virar repositórios
/// separados (ver docs/evoluindo-com-libs-externas.md).
///
/// Sem <c>NoTracking</c> global aqui, ao contrário do Robô 1: a única
/// operação é ler-e-atualizar a mesma entidade, que precisa de tracking.
/// </summary>
public class CobrancaDbContext(DbContextOptions<CobrancaDbContext> options) : DbContext(options)
{
    public DbSet<Arquivo> Arquivos => Set<Arquivo>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
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
    }
}
