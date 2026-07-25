using System.Text.Json.Serialization;

namespace CnabRetorno.Core.Aplicacao.Dtos;

/// <summary>
/// O campo <c>data</c> da resposta do conversor CNAB → JSON
/// (POST /v1/convert/sync/upload, pipeline "conversao-cobranca-retorno-para-json")
/// — modelado 1:1 a partir de exemplo real. Reutilizado também pro JSON
/// sintético do laço "cliente sem arquivo" (ver
/// <c>Json.MesclagemDadosConvertidos.MontarSintetico</c>) e como resultado
/// de <c>Json.MesclagemDadosConvertidos.Mesclar</c> (V+PV+pendências
/// combinados) — desserializar/serializar com
/// <c>JsonNamingPolicy.CamelCase</c>.
/// </summary>
public sealed record DadosConvertidos
{
    public required ArquivoConvertido Arquivo { get; init; }
    public required LoteConvertido Lote { get; init; }
    public required IReadOnlyList<TituloConvertido> Titulos { get; init; }
    public required TotaisConvertidos Totais { get; init; }

    /// <summary>Erros de conversão não-fatais — mesmo espírito do
    /// error-collecting já usado no parser CNAB240 local anterior.</summary>
    public IReadOnlyList<string>? Erros { get; init; }
}

public sealed record ArquivoConvertido
{
    public string? Banco { get; init; }
    public string? CodigoRemessaRetorno { get; init; }
    public string? DataGeracao { get; init; }
    public string? HoraGeracao { get; init; }
    public int NumeroSequencialArquivo { get; init; }
    public string? VersaoLayout { get; init; }
    public int? Densidade { get; init; }
    public string? CodigoConvenio { get; init; }
    public string? NomeBanco { get; init; }
    public string? ReservadoBanco { get; init; }
    public string? ReservadoEmpresa { get; init; }
    public required EmpresaConvertida Empresa { get; init; }
    public required ContaConvertida Conta { get; init; }
}

public sealed record LoteConvertido
{
    public string? Numero { get; init; }
    public string? TipoOperacao { get; init; }
    public string? TipoServico { get; init; }
    public string? VersaoLayout { get; init; }
    public string? CodigoConvenio { get; init; }
    public string? Mensagem1 { get; init; }
    public string? Mensagem2 { get; init; }
    public int NumeroRemessaRetorno { get; init; }
    public string? DataGravacao { get; init; }
    public string? DataCredito { get; init; }
    public required EmpresaConvertida Empresa { get; init; }
    public required ContaConvertida Conta { get; init; }
}

public sealed record EmpresaConvertida
{
    public string? TipoInscricao { get; init; }
    public string? NumeroInscricao { get; init; }
    public string? Nome { get; init; }
}

public sealed record ContaConvertida
{
    public string? Agencia { get; init; }
    public string? DvAgencia { get; init; }
    public string? Conta { get; init; }
    public string? DvConta { get; init; }
    public string? DvAgenciaConta { get; init; }
}

public sealed record TituloConvertido
{
    public required ClienteConvertido Cliente { get; init; }
    public required SacadoConvertido Sacado { get; init; }
    public string? NumeroCarteira { get; init; }
    public string? NossoNumero { get; init; }
    public string? CodigoBanco { get; init; }
    public CorrespondenteConvertido? Correspondente { get; init; }
    public string? SeuNumero { get; init; }
    public decimal ValorNominal { get; init; }
    public string? DataVencimento { get; init; }
    public string? CampoLivre { get; init; }
    public string? CodigoIndice { get; init; }
    public string? Contrato { get; init; }
    public OcorrenciaConvertida? Ocorrencia { get; init; }
    public string? Motivos { get; init; }
    public CobradorConvertido? Cobrador { get; init; }
    public decimal ValorPago { get; init; }
    public decimal ValorLiquido { get; init; }
    public decimal ValorDesconto { get; init; }
    public decimal ValorAbatimento { get; init; }
    public decimal ValorJurosMultaEncargos { get; init; }
    public decimal ValorIof { get; init; }
    public decimal ValorOutrasDespesas { get; init; }
    public decimal ValorOutrosCreditos { get; init; }
    public decimal ValorTarifaCustas { get; init; }
    public string? DataOcorrencia { get; init; }
    public string? DataCredito { get; init; }
    public AlegacaoSacadoConvertida? AlegacaoSacado { get; init; }
    public int NumeroRegistro { get; init; }
    public string? DirecionamentoCobranca { get; init; }
    /// <summary>Campo "uso exclusivo" do banco no layout — o nome de rede
    /// é ditado pela API de conversão e por isso não pode ser trocado sem
    /// quebrar a integração; o nome em C# é neutro.</summary>
    [JsonPropertyName("usoExclusivoAsa")]
    public string? UsoExclusivoBanco { get; init; }
    public string? ModalidadeComBancoCedente { get; init; }
}

public sealed record ClienteConvertido
{
    public string? ContaHeader { get; init; }
    public required DocumentoConvertido Documento { get; init; }
}

public sealed record SacadoConvertido
{
    public required DocumentoConvertido Documento { get; init; }
    public string? Nome { get; init; }
}

public sealed record DocumentoConvertido
{
    public string? Tipo { get; init; }
    public string? Codigo { get; init; }
    public string? Inscricao { get; init; }
}

public sealed record CorrespondenteConvertido
{
    public string? CodigoModalidade { get; init; }
    public string? Banco { get; init; }
    public string? NossoNumero { get; init; }
}

public sealed record OcorrenciaConvertida
{
    public string? Codigo { get; init; }
    public string? Descricao { get; init; }
}

public sealed record CobradorConvertido
{
    public string? Banco { get; init; }
    public string? Agencia { get; init; }
    public string? DvAgencia { get; init; }
}

public sealed record AlegacaoSacadoConvertida
{
    public string? Codigo { get; init; }
    public string? Data { get; init; }
    public decimal Valor { get; init; }
    public string? Complemento { get; init; }
}

public sealed record TotaisConvertidos
{
    public int Titulos { get; init; }
    public int QuantidadeRegistros { get; init; }
    public decimal ValorTotalCobrancaSimples { get; init; }
}
