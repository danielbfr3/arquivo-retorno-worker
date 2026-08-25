using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.ExcelCnab.Worker.Persistencia;

/// <summary>
/// Acesso à base de adesão (<c>ASA_CASH_ADESAO</c>) — leitura pura, só pra
/// resolver a razão social do cliente a partir do CNPJ do nome do arquivo.
///
/// TODO(a-confirmar) **em bloco**: nome de schema, de tabela e de cada
/// coluna são placeholder — ninguém inspecionou esta base ainda. Este é o
/// único lugar a ajustar quando o schema real chegar, e é caminho crítico
/// de todo arquivo (não mais de um modo opcional, como era antes): com o
/// mapeamento errado, nenhuma planilha é enviada. Mesmo padrão de
/// docs/segunda-fonte-de-dados-sql-server.md: sem migrations, tracking
/// desligado.
/// </summary>
public class AdesaoDbContext(DbContextOptions<AdesaoDbContext> options) : DbContext(options)
{
    public DbSet<EmpresaAdesao> Empresas => Set<EmpresaAdesao>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<EmpresaAdesao>(e =>
        {
            // TODO(a-confirmar): schema/tabela/colunas — chute razoável,
            // não observado.
            e.ToTable("Empresa", schema: "Adesao");
            e.HasKey(a => a.Documento);
            e.Property(a => a.Documento).HasColumnName("Documento").HasMaxLength(20);
            e.Property(a => a.RazaoSocial).HasColumnName("RazaoSocial").HasMaxLength(100);
        });
    }
}
