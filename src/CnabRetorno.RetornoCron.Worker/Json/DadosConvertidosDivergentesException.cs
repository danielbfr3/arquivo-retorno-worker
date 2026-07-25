namespace CnabRetorno.RetornoCron.Worker.Json;

/// <summary>
/// Lançada quando os dados convertidos de V e PV não batem em Banco,
/// Empresa (tipo+número de inscrição) ou Conta (agência+dv+conta+dv) —
/// sinal de que os dois arquivos não pertencem à mesma remessa/beneficiário
/// e não devem ser mesclados. Tratada como falha isolada em <see
/// cref="Pipeline.ProcessadorArquivoRetornoService"/> (não derruba o lote).
/// Substitui <c>Cnab240HeaderDivergenteException</c> — mesmo papel, agora
/// comparando campos de <see cref="Core.Aplicacao.Dtos.DadosConvertidos"/>
/// em vez de posições de CNAB.
/// </summary>
public sealed class DadosConvertidosDivergentesException(string campo, string? valorV, string? valorPv)
    : Exception($"Dados convertidos divergentes entre V e PV no campo '{campo}': V='{valorV}', PV='{valorPv}'.")
{
    public string Campo { get; } = campo;
    public string? ValorV { get; } = valorV;
    public string? ValorPv { get; } = valorPv;
}
