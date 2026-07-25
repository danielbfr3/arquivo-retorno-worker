namespace CnabRetorno.Core.Dominio;

/// <summary>
/// Projeção de <c>Instrucao.Instrucao</c> (base CASH_COBRANCA, SQL Server
/// — schema em docs/cash-cobranca-referencia.md §1.3) com os campos usados
/// no de-para pro Segmento T/U do CNAB de retorno.
/// </summary>
public sealed class Instrucao
{
    public required Guid InstrucaoID { get; init; }
    public required string ClienteDocumento { get; init; } // CPF ou CNPJ
    public required short CodigoStatus { get; init; }
    public required DateTime DataAtualizacao { get; init; } // filtro D-1

    public string? Agencia { get; init; }
    public string? NumeroCarteira { get; init; }
    public string? NossoNumero { get; init; }
}

/// <summary>Projeção de <c>Instrucao.InstrucaoErro</c>.</summary>
public sealed class InstrucaoErro
{
    public required Guid InstrucaoID { get; init; }
    public required string CodigoOcorrenciaErro { get; init; }
    public string? DescricaoOcorrenciaErro { get; init; }
}
