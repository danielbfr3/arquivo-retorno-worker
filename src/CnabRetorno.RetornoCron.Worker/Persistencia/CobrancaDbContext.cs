using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.RetornoCron.Worker.Persistencia;

/// <summary>
/// Acesso à base CASH_COBRANCA (SQL Server, existente, outro sistema) —
/// mesmo padrão de docs/segunda-fonte-de-dados-sql-server.md: sem
/// migrations daqui, tracking desligado por padrão.
///
/// Quase tudo aqui é **somente leitura** (projeções sem chave). A exceção
/// é <see cref="Arquivos"/>: o Robô 1 cria a linha do arquivo de retorno
/// antes de mandar pro conversor assíncrono, e é o ID dela que amarra o
/// Robô 2 ao cliente quando a conclusão chega (ver docs/regras-de-negocio.md).
/// <c>QueryTrackingBehavior.NoTracking</c> só afeta consultas — <c>Add</c>
/// + <c>SaveChangesAsync</c> continuam funcionando normalmente.
///
/// Schema mapeado a partir de docs/cash-cobranca-referencia.md §1 (extraído
/// do ambiente real do time dono da base) — nomes de tabela/coluna são
/// fiéis ao documento, não placeholder.
/// </summary>
public class CobrancaDbContext(DbContextOptions<CobrancaDbContext> options) : DbContext(options)
{
    public DbSet<Arquivo> Arquivos => Set<Arquivo>();
    public DbSet<Titulo> Titulos => Set<Titulo>();
    public DbSet<TituloErro> TitulosErro => Set<TituloErro>();
    public DbSet<Instrucao> Instrucoes => Set<Instrucao>();
    public DbSet<InstrucaoErro> InstrucoesErro => Set<InstrucaoErro>();
    public DbSet<InstrucaoComTitulo> InstrucoesComTitulo => Set<InstrucaoComTitulo>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Única entidade **com chave** do contexto — é a única escrita
        // (ver doc-comment da classe). Só as colunas que estes workers
        // usam são mapeadas; a tabela real tem mais (LayoutBanco,
        // LayoutTipoArquivo, ArquivoCnabID — ver
        // docs/cash-cobranca-referencia.md §1.1), deixadas de fora porque
        // nenhum dos dois robôs preenche ou lê.
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

        // Titulo.Titulo + Titulo.TituloInfo — projetadas juntas num único
        // POCO (join 1:1 pelo TituloID) porque é assim que o de-para do
        // Segmento T/U (docs/cash-cobranca-referencia.md §2.1) as consome.
        // Mapeada via .ToSqlQuery em vez de duas tabelas + navegação porque
        // HasNoKey() não suporta Include entre entidades sem chave.
        mb.Entity<Titulo>(e =>
        {
            e.HasNoKey();
            e.ToSqlQuery("""
                SELECT t.TituloID, t.ClienteDocumento, t.CodigoStatus, t.DataAtualizacao,
                       t.ClienteTipoDocumento, t.ClienteContaHeader,
                       t.CodigoOcorrencia, t.DescricaoOcorrencia,
                       ti.NumeroCarteira, ti.CodigoBanco, ti.CodigoModalidade,
                       ti.NossoNumero, ti.NossoNumeroCorrespondente, ti.SeuNumero,
                       ti.ValorNominal, ti.DataVencimento, ti.CampoLivre, ti.CodigoIndice,
                       ti.SacadorAvalistaTipoDocumento, ti.SacadorAvalistaDocumento, ti.SacadorAvalistaNome,
                       trr.CodBanco AS RegistroRetornoCodBanco, trr.CodAgenciaCob AS RegistroRetornoCodAgenciaCob
                FROM Titulo.Titulo t
                INNER JOIN Titulo.TituloInfo ti ON ti.TituloID = t.TituloID
                LEFT JOIN Titulo.TituloRegistroRetorno trr ON trr.TituloID = t.TituloID
                """);
        });

        // Instrução casada (OUTER APPLY TOP 1) com o título correspondente
        // — ver docs/cash-cobranca-referencia.md §1.3/§2.4. TOP 1 + ORDER
        // BY DataCriacao DESC blinda contra NossoNumero não ser único por
        // (ClienteContaHeader, ClienteDocumento) — não documentado/garantido
        // (ver docs/riscos-conhecidos.md sobre duplicidade silenciosa).
        mb.Entity<InstrucaoComTitulo>(e =>
        {
            e.HasNoKey();
            e.ToSqlQuery("""
                SELECT i.InstrucaoID, i.ClienteDocumento, i.CodigoStatus, i.DataAtualizacao,
                       i.ClienteTipoDocumento, i.ClienteContaHeader, i.Agencia, i.NumeroCarteira, i.NossoNumero,
                       i.CodigoOcorrencia, i.DescricaoOcorrencia,
                       tc.TituloID, tc.NumeroCarteira AS TituloNumeroCarteira, tc.CodigoBanco, tc.CodigoModalidade,
                       tc.SeuNumero, tc.ValorNominal, tc.DataVencimento, tc.CampoLivre, tc.CodigoIndice,
                       tc.SacadorAvalistaTipoDocumento, tc.SacadorAvalistaDocumento, tc.SacadorAvalistaNome,
                       trr.CodBanco AS RegistroRetornoCodBanco, trr.CodAgenciaCob AS RegistroRetornoCodAgenciaCob
                FROM Instrucao.Instrucao i
                OUTER APPLY (
                    SELECT TOP 1
                        t2.TituloID, t2.DataCriacao,
                        ti2.NumeroCarteira, ti2.CodigoBanco, ti2.CodigoModalidade,
                        ti2.SeuNumero, ti2.ValorNominal, ti2.DataVencimento, ti2.CampoLivre, ti2.CodigoIndice,
                        ti2.SacadorAvalistaTipoDocumento, ti2.SacadorAvalistaDocumento, ti2.SacadorAvalistaNome
                    FROM Titulo.Titulo t2
                    INNER JOIN Titulo.TituloInfo ti2 ON ti2.TituloID = t2.TituloID
                    WHERE t2.ClienteContaHeader = i.ClienteContaHeader
                      AND t2.ClienteDocumento = i.ClienteDocumento
                      AND ti2.NossoNumero = i.NossoNumero
                    ORDER BY t2.DataCriacao DESC
                ) tc
                LEFT JOIN Titulo.TituloRegistroRetorno trr ON trr.TituloID = tc.TituloID
                """);
        });

        mb.Entity<TituloErro>(e =>
        {
            e.ToTable("TituloErro", schema: "Titulo");
            e.HasNoKey();
            e.Property(t => t.TituloID).HasColumnName("TituloID");
            e.Property(t => t.CodigoOcorrenciaErro).HasColumnName("CodigoOcorrenciaErro").HasMaxLength(10);
            e.Property(t => t.DescricaoOcorrenciaErro).HasColumnName("DescricaoOcorrenciaErro").HasMaxLength(500);
        });

        mb.Entity<Instrucao>(e =>
        {
            e.ToTable("Instrucao", schema: "Instrucao");
            e.HasNoKey();
            e.Property(i => i.InstrucaoID).HasColumnName("InstrucaoID");
            e.Property(i => i.ClienteDocumento).HasColumnName("ClienteDocumento").HasMaxLength(20);
            e.Property(i => i.CodigoStatus).HasColumnName("CodigoStatus");
            e.Property(i => i.DataAtualizacao).HasColumnName("DataAtualizacao");
            e.Property(i => i.Agencia).HasColumnName("Agencia").HasMaxLength(10);
            e.Property(i => i.NumeroCarteira).HasColumnName("NumeroCarteira").HasMaxLength(50);
            e.Property(i => i.NossoNumero).HasColumnName("NossoNumero").HasMaxLength(50);
        });

        mb.Entity<InstrucaoErro>(e =>
        {
            e.ToTable("InstrucaoErro", schema: "Instrucao");
            e.HasNoKey();
            e.Property(i => i.InstrucaoID).HasColumnName("InstrucaoID");
            e.Property(i => i.CodigoOcorrenciaErro).HasColumnName("CodigoOcorrenciaErro").HasMaxLength(10);
            e.Property(i => i.DescricaoOcorrenciaErro).HasColumnName("DescricaoOcorrenciaErro").HasMaxLength(500);
        });
    }
}
