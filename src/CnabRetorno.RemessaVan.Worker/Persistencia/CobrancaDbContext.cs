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

/// <summary>Controle de idempotência por conteúdo — o MD5 de cada remessa
/// já ingerida. Tabela **nova**, exclusiva deste worker (DDL em
/// deploy/cobranca-controle-ingestao-van.sql).
///
/// Em banco, e não em memória: a VAN pode retransmitir o mesmo arquivo
/// dias depois (e o worker pode ter reiniciado no meio) — sem isto, a
/// retransmissão ganharia GUID novo, segundo objeto no storage e segunda
/// linha em <c>Cobranca.Arquivo</c>, que o worker de conversão
/// processaria de novo. Remessa duplicada lá na frente pode significar
/// boleto duplicado.</summary>
public class RemessaIngerida
{
    /// <summary>MD5 do conteúdo em hexadecimal (32 caracteres).</summary>
    public string Md5 { get; set; } = default!;

    public Guid ArquivoID { get; set; }
    public string NomeOriginal { get; set; } = default!;
    public DateTime DataCriacao { get; set; }
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
    public DbSet<RemessaIngerida> RemessasIngeridas => Set<RemessaIngerida>();

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

        mb.Entity<RemessaIngerida>(e =>
        {
            e.ToTable("ControleIngestaoVan", schema: "Cobranca");
            e.HasKey(r => r.Md5); // a PK é a própria garantia de unicidade por conteúdo
            e.Property(r => r.Md5).HasColumnName("Md5").HasMaxLength(32);
            e.Property(r => r.ArquivoID).HasColumnName("ArquivoID");
            e.Property(r => r.NomeOriginal).HasColumnName("NomeOriginal").HasMaxLength(250);
            e.Property(r => r.DataCriacao).HasColumnName("DataCriacao");
        });

        mb.Entity<ParametroCliente>(e =>
        {
            e.ToTable("Parametro", schema: "Cobranca");
            e.HasNoKey();
            e.Property(p => p.Documento).HasColumnName("Documento").HasMaxLength(20);
            // Nome real da coluna é CodigoConta, não ContaHeader — ver
            // docs/cash-cobranca-referencia.md §1.1. O nome em C# fica
            // ContaHeader por consistência com Core.Dominio.Arquivo
            // (ClienteContaHeader), que é como o valor é usado depois.
            e.Property(p => p.ContaHeader).HasColumnName("CodigoConta").HasMaxLength(10);
        });
    }
}
