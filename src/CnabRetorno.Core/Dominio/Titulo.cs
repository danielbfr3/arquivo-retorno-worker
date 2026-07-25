namespace CnabRetorno.Core.Dominio;

/// <summary>
/// Projeção de <c>Titulo.Titulo</c> + <c>Titulo.TituloInfo</c> +
/// <c>Titulo.TituloRegistroRetorno</c> (base CASH_COBRANCA, SQL Server —
/// schema em docs/cash-cobranca-referencia.md §1.2) com os campos usados
/// pra montar um <c>TituloConvertido</c> de pendência (mesmo documento,
/// §2.1/§2.4).
///
/// POCO puro de propósito — nenhuma dependência de EF Core aqui. Mapeado
/// pro schema real em <c>CobrancaDbContext</c>.
/// </summary>
public sealed class Titulo
{
    public required Guid TituloID { get; init; }
    public required string ClienteDocumento { get; init; } // CPF ou CNPJ
    public required short CodigoStatus { get; init; }
    public required DateTime DataAtualizacao { get; init; } // filtro D-1

    // Titulo.Titulo
    public short? ClienteTipoDocumento { get; init; } // 1-CPF, 2-CNPJ
    public string? ClienteContaHeader { get; init; }
    public string? CodigoOcorrencia { get; init; }
    public string? DescricaoOcorrencia { get; init; }

    // Titulo.TituloInfo
    public string? NumeroCarteira { get; init; }
    public string? CodigoBanco { get; init; }
    public string? CodigoModalidade { get; init; }
    public string? NossoNumero { get; init; }
    public string? NossoNumeroCorrespondente { get; init; }
    public string? SeuNumero { get; init; }
    public decimal? ValorNominal { get; init; }
    public DateOnly? DataVencimento { get; init; }
    public string? CampoLivre { get; init; }
    public string? CodigoIndice { get; init; }

    // TODO(a-confirmar): docs/cash-cobranca-referencia.md §2.3 alerta pra
    // uma possível inversão semântica — no CNAB o "pagador" costuma ser o
    // Sacado (quem paga o boleto), não o Sacador/Avalista, mas o de-para
    // do documento mapeia o "sacado" do JSON de retorno a partir destas
    // colunas (renomeadas SacadorAvalista* — nome novo só deixa mais
    // explícito que descrevem o avalista, não dissolve a dúvida). Mantido
    // fiel ao mapeamento literal fornecido — conferir antes de fechar em
    // produção.
    public short? SacadorAvalistaTipoDocumento { get; init; }
    public string? SacadorAvalistaDocumento { get; init; }
    public string? SacadorAvalistaNome { get; init; }

    // Titulo.TituloRegistroRetorno (1:0..1 — pode não existir)
    public string? RegistroRetornoCodBanco { get; init; }
    public string? RegistroRetornoCodAgenciaCob { get; init; }
}

/// <summary>Projeção de <c>Titulo.TituloErro</c>.</summary>
public sealed class TituloErro
{
    public required Guid TituloID { get; init; }
    public required string CodigoOcorrenciaErro { get; init; }
    public string? DescricaoOcorrenciaErro { get; init; }
}
