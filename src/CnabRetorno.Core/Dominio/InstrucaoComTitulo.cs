namespace CnabRetorno.Core.Dominio;

/// <summary>
/// Projeção de <c>Instrucao.Instrucao</c> casada (OUTER APPLY TOP 1) com o
/// <c>Titulo.Titulo</c> + <c>Titulo.TituloInfo</c> +
/// <c>Titulo.TituloRegistroRetorno</c> correspondente, via
/// <c>ClienteContaHeader + ClienteDocumento + NossoNumero</c> — necessário
/// porque o <c>TituloConvertido</c> de uma instrução carrega campos
/// (sacado, valorNominal, carteira...) que só existem no título, não na
/// instrução (docs/cash-cobranca-referencia.md §1.3/§2.4).
///
/// Campos do título vêm nulos quando não se acha nenhum correspondente
/// (instrução "órfã") — quem consome decide o que fazer.
///
/// Tipo dedicado (não reaproveita <see cref="Instrucao"/>) de propósito:
/// o join é 1:0..1 e teria que tornar campos do título opcionais em todo
/// canto que já usa <see cref="Instrucao"/> como projeção pura.
/// </summary>
public sealed class InstrucaoComTitulo
{
    // Instrucao.Instrucao
    public required Guid InstrucaoID { get; init; }
    public required string ClienteDocumento { get; init; }
    public required short CodigoStatus { get; init; }
    public required DateTime DataAtualizacao { get; init; } // filtro D-1
    public short? ClienteTipoDocumento { get; init; } // 1-CPF, 2-CNPJ
    public string? ClienteContaHeader { get; init; }
    public string? Agencia { get; init; }
    public string? NumeroCarteira { get; init; }
    public string? NossoNumero { get; init; }
    public string? CodigoOcorrencia { get; init; }
    public string? DescricaoOcorrencia { get; init; }

    // Titulo.Titulo + Titulo.TituloInfo (nulos se não achar título). O
    // exemplo real de retorno mapeia "numeroCarteira" da instrução a
    // partir do NumeroCarteira do TÍTULO casado, não da própria instrução
    // — por isso o nome distinto de <see cref="NumeroCarteira"/> acima.
    public Guid? TituloID { get; init; }
    public string? TituloNumeroCarteira { get; init; }
    public string? CodigoBanco { get; init; }
    public string? CodigoModalidade { get; init; }
    public string? SeuNumero { get; init; }
    public decimal? ValorNominal { get; init; }
    public DateOnly? DataVencimento { get; init; }
    public string? CampoLivre { get; init; }
    public string? CodigoIndice { get; init; }
    public short? SacadorAvalistaTipoDocumento { get; init; } // TODO(a-confirmar): ver Titulo.cs
    public string? SacadorAvalistaDocumento { get; init; }
    public string? SacadorAvalistaNome { get; init; }

    // Titulo.TituloRegistroRetorno (1:0..1 por TituloID)
    public string? RegistroRetornoCodBanco { get; init; }
    public string? RegistroRetornoCodAgenciaCob { get; init; }
}
