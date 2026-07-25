namespace CnabRetorno.RetornoCron.Worker.Pipeline;

public class PipelineOptions
{
    public const string Secao = "Pipeline";

    /// <summary>Quantos arquivos V são processados em paralelo por
    /// execução — mesmo raciocínio do pipeline anterior (I/O-bound, limite
    /// pra não esgotar o pool de conexões do Postgres/SQL Server).</summary>
    public int MaxArquivosConcorrentes { get; set; } = 8;

    /// <summary>Código de banco usado no header sintético do laço pós-lote
    /// (cliente com pendência no CASH_COBRANCA mas sem V/PV no dia — ver
    /// ProcessadorClientesSemArquivoService). TODO(a-confirmar): não há um
    /// banco "dono" óbvio pra um arquivo que não veio de nenhum V real.</summary>
    public string BancoPadrao { get; set; } = "000";
}
