using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.PagamentoRetorno.Worker.Persistencia;

/// <summary>
/// Acesso à base <c>ASA_CASH_ADESAO</c> — usada **só** no modo
/// <c>Geracao:Modo = CnabDireto</c> (ver <c>Json/GeracaoOptions.cs</c>).
/// No modo padrão (<c>Conversor</c>) este contexto nem é registrado no
/// DI, então nunca abre conexão.
///
/// TODO(a-confirmar) **em bloco**: nome de schema, nome de tabela e nomes
/// de coluna são todos placeholder — ninguém inspecionou esta base ainda.
/// Único ponto de ajuste quando o schema real chegar; ver
/// <see cref="Core.Dominio.EmpresaAdesao"/> pro porquê de cada campo.
/// Mesmo padrão de docs/segunda-fonte-de-dados-sql-server.md: sem
/// migrations, tracking desligado (é leitura pura).
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
            e.Property(a => a.CodigoConvenio).HasColumnName("CodigoConvenio").HasMaxLength(20);
            e.Property(a => a.Agencia).HasColumnName("Agencia").HasMaxLength(5);
            e.Property(a => a.DvAgencia).HasColumnName("DvAgencia").HasMaxLength(1);
            e.Property(a => a.Conta).HasColumnName("Conta").HasMaxLength(12);
            e.Property(a => a.DvConta).HasColumnName("DvConta").HasMaxLength(1);
            e.Property(a => a.DvAgenciaConta).HasColumnName("DvAgenciaConta").HasMaxLength(1);
            e.Property(a => a.NomeEmpresa).HasColumnName("NomeEmpresa").HasMaxLength(30);
            e.Property(a => a.Logradouro).HasColumnName("Logradouro").HasMaxLength(30);
            e.Property(a => a.NumeroEndereco).HasColumnName("NumeroEndereco").HasMaxLength(5);
            e.Property(a => a.ComplementoEndereco).HasColumnName("ComplementoEndereco").HasMaxLength(15);
            e.Property(a => a.Cidade).HasColumnName("Cidade").HasMaxLength(20);
            e.Property(a => a.Cep).HasColumnName("Cep").HasMaxLength(5);
            e.Property(a => a.ComplementoCep).HasColumnName("ComplementoCep").HasMaxLength(3);
            e.Property(a => a.Estado).HasColumnName("Estado").HasMaxLength(2);
        });
    }
}
