using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.PagamentoRetorno.Worker.Persistencia;

/// <summary>Marca d'água por cliente — até onde o último arquivo parcial
/// já reportou. Tabela **nova**, exclusiva deste worker (ver
/// deploy/pagamento-controle-janela.sql).
///
/// **Contínua, sem dimensão de dia** — de propósito: o "dia útil" deste
/// robô vai de consolidado a consolidado (18h→18h), e uma marca por dia
/// de calendário recriava o buraco pós-18h (desfecho às 18h30 não
/// pertencia a dia nenhum).</summary>
public class ControleJanelaRetorno
{
    public string ClienteDocumento { get; set; } = default!;

    /// <summary>Maior instante de desfecho já incluído num arquivo deste
    /// cliente. O próximo parcial pega o que for estritamente
    /// posterior.</summary>
    public DateTime UltimoInstanteReportado { get; set; }

    public DateTime DataAtualizacao { get; set; }
}

/// <summary>Par (pagamento, status) já reportado num arquivo **parcial**.
/// Segunda camada de idempotência, complementar à marca d'água: qualquer
/// UPDATE na linha do pagamento avança <c>DataAtualizacao</c> e o faria
/// reaparecer no delta seguinte com cara de movimentação nova — este
/// registro barra o reenvio quando o status não mudou, e deixa passar
/// quando mudou (aí é desfecho novo de verdade, e reportar é correto).
///
/// O consolidado ignora esta tabela: ele repete o dia útil inteiro por
/// design.</summary>
public class PagamentoReportado
{
    public Guid PagamentoID { get; set; }
    public short CodigoStatus { get; set; }
    public DateTime DataCriacao { get; set; }
}

/// <summary>Projeção só-leitura de <c>Pagamento.Parametro</c> — a linha por
/// cliente que guarda o NSA.</summary>
public class ParametroPagamento
{
    public string Documento { get; init; } = default!;
    public long SequencialAtual { get; init; }
}

/// <summary>
/// Acesso à base ASA_CASH_PAGAMENTO (SQL Server, existente, de outro
/// time) — mesmo padrão de docs/segunda-fonte-de-dados-sql-server.md: sem
/// migrations daqui, tracking desligado por padrão.
///
/// A entidade central é <see cref="Movimentacoes"/>: uma projeção sem
/// chave sobre um <c>UNION ALL</c> das cinco duplas
/// <c>&lt;Tipo&gt;</c>/<c>&lt;Tipo&gt;Info</c>. Um POCO por meio daria
/// cinco classes quase idênticas e cinco caminhos de montagem; o UNION
/// deixa o resto do robô lidar com "uma movimentação", e só a montagem do
/// segmento (A ou J) olha o meio.
/// </summary>
public class PagamentoDbContext(DbContextOptions<PagamentoDbContext> options) : DbContext(options)
{
    public DbSet<MovimentacaoPagamento> Movimentacoes => Set<MovimentacaoPagamento>();
    public DbSet<Arquivo> Arquivos => Set<Arquivo>();
    public DbSet<ParametroPagamento> Parametros => Set<ParametroPagamento>();
    public DbSet<ControleJanelaRetorno> ControleJanelas => Set<ControleJanelaRetorno>();
    public DbSet<PagamentoReportado> Reportados => Set<PagamentoReportado>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<MovimentacaoPagamento>(e =>
        {
            e.HasNoKey();
            e.ToSqlQuery(ConsultaMovimentacoes);
        });

        // TODO(a-confirmar): o schema de Pagamento.Arquivo não foi
        // capturado na extração de 03/08/2026 — está mapeado como espelho
        // de Cobranca.Arquivo (ver Core.Dominio.Arquivo). Se as colunas
        // divergirem, é aqui que se corrige.
        mb.Entity<Arquivo>(e =>
        {
            e.ToTable("Arquivo", schema: "Pagamento");
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

        // TODO(a-confirmar): idem — Pagamento.Parametro não foi capturado.
        // Mapeada como espelho de Cobranca.Parametro, que é onde o
        // SequencialAtual de cobrança já vive.
        mb.Entity<ParametroPagamento>(e =>
        {
            e.ToTable("Parametro", schema: "Pagamento");
            e.HasNoKey();
            e.Property(p => p.Documento).HasColumnName("Documento").HasMaxLength(20);
            e.Property(p => p.SequencialAtual).HasColumnName("SequencialAtual");
        });

        mb.Entity<ControleJanelaRetorno>(e =>
        {
            e.ToTable("ControleJanelaRetorno", schema: "Pagamento");
            e.HasKey(c => c.ClienteDocumento);
            e.Property(c => c.ClienteDocumento).HasColumnName("ClienteDocumento").HasMaxLength(20);
            e.Property(c => c.UltimoInstanteReportado).HasColumnName("UltimoInstanteReportado");
            e.Property(c => c.DataAtualizacao).HasColumnName("DataAtualizacao");
        });

        mb.Entity<PagamentoReportado>(e =>
        {
            e.ToTable("ControlePagamentoReportado", schema: "Pagamento");
            e.HasKey(r => new { r.PagamentoID, r.CodigoStatus });
            e.Property(r => r.PagamentoID).HasColumnName("PagamentoID");
            e.Property(r => r.CodigoStatus).HasColumnName("CodigoStatus");
            e.Property(r => r.DataCriacao).HasColumnName("DataCriacao");
        });
    }

    /// <summary>
    /// União das cinco duplas cabeçalho/Info. As cinco tabelas de
    /// cabeçalho têm exatamente a mesma estrutura de 15 campos (extração
    /// de 03/08/2026), então essa metade é literal; as tabelas
    /// <c>Info</c> divergem, e cada ramo preenche com <c>NULL</c> tipado o
    /// que não se aplica.
    ///
    /// Os <c>CAST(NULL AS ...)</c> não são decoração: num <c>UNION ALL</c>
    /// o SQL Server infere o tipo da coluna pelo primeiro ramo, e um
    /// <c>NULL</c> sem tipo faria a coluna inteira virar <c>int</c> e
    /// estourar na leitura de um <c>varchar</c> do ramo seguinte.
    ///
    /// O valor de <c>Meio</c> é literal por ramo e casa com
    /// <c>Pagamento.TipoTransacao</c> (2 TEF, 3 PIX, 4 BOLETO, 5 TRICON,
    /// 6 TED).
    /// </summary>
    private const string ConsultaMovimentacoes = """
        SELECT
            CAST(3 AS smallint) AS Meio, p.PixID AS PagamentoID, p.CodigoStatus,
            p.ClienteContaHeader, p.ClienteTipoDocumento, p.ClienteDocumento,
            p.DataCriacao, p.DataAtualizacao,
            p.CodigoOcorrencia, p.DescricaoOcorrencia, p.CodigoAutenticacao,
            i.IdentificadorExterno, i.Linhas,
            i.ValorPagamento, i.DataTransacao, i.Observacao,
            i.FavorecidoBancoIspb AS FavorecidoBanco, i.FavorecidoAgencia, i.FavorecidoConta,
            i.FavorecidoTipoConta, i.FavorecidoNome, i.FavorecidoDocumento, i.FavorecidoTipoDocumento,
            i.DebitoAgencia, i.DebitoConta, i.DebitoNome, i.ChavePixUrl,
            CAST(NULL AS varchar(10)) AS CodigoBanco,
            CAST(NULL AS varchar(60)) AS CodigoBarra,
            CAST(NULL AS varchar(60)) AS LinhaDigitavel,
            CAST(NULL AS varchar(50)) AS NossoNumero,
            CAST(NULL AS date) AS DataVencimento,
            CAST(NULL AS decimal(18,2)) AS ValorNominal,
            CAST(NULL AS decimal(18,2)) AS ValorAbatimento,
            CAST(NULL AS varchar(200)) AS BeneficiarioNome,
            CAST(NULL AS varchar(20)) AS BeneficiarioDocumento,
            CAST(NULL AS smallint) AS BeneficiarioTipoDocumento
        FROM Pagamento.Pix p
        INNER JOIN Pagamento.PixInfo i ON i.PixID = p.PixID

        UNION ALL

        SELECT
            CAST(6 AS smallint), p.TedID, p.CodigoStatus,
            p.ClienteContaHeader, p.ClienteTipoDocumento, p.ClienteDocumento,
            p.DataCriacao, p.DataAtualizacao,
            p.CodigoOcorrencia, p.DescricaoOcorrencia, p.CodigoAutenticacao,
            i.IdentificadorExterno, i.Linhas,
            i.ValorTransacao, i.DataTransacao, i.Observacao,
            i.FavorecidoBanco, i.FavorecidoAgencia, i.FavorecidoConta,
            i.FavorecidoTipoConta, i.FavorecidoNome, i.FavorecidoDocumento, i.FavorecidoTipoDocumento,
            i.DebitoAgencia, i.DebitoConta, i.DebitoNome, CAST(NULL AS varchar(500)),
            CAST(NULL AS varchar(10)), CAST(NULL AS varchar(60)), CAST(NULL AS varchar(60)),
            CAST(NULL AS varchar(50)), CAST(NULL AS date),
            CAST(NULL AS decimal(18,2)), CAST(NULL AS decimal(18,2)),
            CAST(NULL AS varchar(200)), CAST(NULL AS varchar(20)), CAST(NULL AS smallint)
        FROM Pagamento.Ted p
        INNER JOIN Pagamento.TedInfo i ON i.TedID = p.TedID

        UNION ALL

        SELECT
            CAST(2 AS smallint), p.TefID, p.CodigoStatus,
            p.ClienteContaHeader, p.ClienteTipoDocumento, p.ClienteDocumento,
            p.DataCriacao, p.DataAtualizacao,
            p.CodigoOcorrencia, p.DescricaoOcorrencia, p.CodigoAutenticacao,
            i.IdentificadorExterno, i.Linhas,
            i.ValorTransacao, i.DataTransacao, i.Observacao,
            CAST(NULL AS varchar(20)), i.FavorecidoAgencia, i.FavorecidoConta,
            i.FavorecidoTipoConta, i.FavorecidoNome, i.FavorecidoDocumento, i.FavorecidoTipoDocumento,
            i.DebitoAgencia, i.DebitoConta, i.DebitoNome, CAST(NULL AS varchar(500)),
            CAST(NULL AS varchar(10)), CAST(NULL AS varchar(60)), CAST(NULL AS varchar(60)),
            CAST(NULL AS varchar(50)), CAST(NULL AS date),
            CAST(NULL AS decimal(18,2)), CAST(NULL AS decimal(18,2)),
            CAST(NULL AS varchar(200)), CAST(NULL AS varchar(20)), CAST(NULL AS smallint)
        FROM Pagamento.Tef p
        INNER JOIN Pagamento.TefInfo i ON i.TefID = p.TefID

        UNION ALL

        SELECT
            CAST(4 AS smallint), p.BoletoID, p.CodigoStatus,
            p.ClienteContaHeader, p.ClienteTipoDocumento, p.ClienteDocumento,
            p.DataCriacao, p.DataAtualizacao,
            p.CodigoOcorrencia, p.DescricaoOcorrencia, p.CodigoAutenticacao,
            i.IdentificadorExterno, i.Linhas,
            i.ValorPagamento, CAST(NULL AS datetime2), i.Observacao,
            CAST(NULL AS varchar(20)), CAST(NULL AS varchar(20)), CAST(NULL AS varchar(20)),
            CAST(NULL AS varchar(20)), CAST(NULL AS varchar(100)), CAST(NULL AS varchar(20)), CAST(NULL AS smallint),
            CAST(NULL AS varchar(20)), CAST(NULL AS varchar(20)), CAST(NULL AS varchar(100)), CAST(NULL AS varchar(500)),
            i.CodigoBanco, i.CodigoBarra, i.LinhaDigitavel,
            i.NossoNumero, i.DataVencimento,
            i.ValorNominal, i.ValorAbatimento,
            i.SacadorNome, i.SacadorDocumento, i.SacadorTipoDocumento
        FROM Pagamento.Boleto p
        INNER JOIN Pagamento.BoletoInfo i ON i.BoletoID = p.BoletoID

        UNION ALL

        SELECT
            CAST(5 AS smallint), p.TriconID, p.CodigoStatus,
            p.ClienteContaHeader, p.ClienteTipoDocumento, p.ClienteDocumento,
            p.DataCriacao, p.DataAtualizacao,
            p.CodigoOcorrencia, p.DescricaoOcorrencia, p.CodigoAutenticacao,
            i.IdentificadorExterno, i.Linhas,
            i.ValorPagamento, CAST(NULL AS datetime2), i.Observacao,
            CAST(NULL AS varchar(20)), CAST(NULL AS varchar(20)), CAST(NULL AS varchar(20)),
            CAST(NULL AS varchar(20)), CAST(NULL AS varchar(100)), CAST(NULL AS varchar(20)), CAST(NULL AS smallint),
            CAST(NULL AS varchar(20)), CAST(NULL AS varchar(20)), CAST(NULL AS varchar(100)), CAST(NULL AS varchar(500)),
            i.CodigoBanco, i.CodigoBarra, i.LinhaDigitavel,
            i.NossoNumero, i.DataVencimento,
            i.ValorNominal, i.ValorAbatimento,
            i.SacadorNome, i.SacadorDocumento, CAST(NULL AS smallint)
        FROM Pagamento.Tricon p
        INNER JOIN Pagamento.TriconInfo i ON i.TriconID = p.TriconID
        """;
}
