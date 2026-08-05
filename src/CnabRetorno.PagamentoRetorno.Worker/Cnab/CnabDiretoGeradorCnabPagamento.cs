using CnabRetorno.Core.Aplicacao.Dtos;
using CnabRetorno.Core.Cnab240;
using CnabRetorno.PagamentoRetorno.Worker.Persistencia;

namespace CnabRetorno.PagamentoRetorno.Worker.Cnab;

/// <summary>Cliente sem linha em <c>ASA_CASH_ADESAO</c> — sem dados
/// institucionais não há header válido pra escrever. Falha isolada do
/// arquivo (o pipeline já trata isso: falha de um cliente não derruba a
/// janela), nunca um header incompleto.</summary>
public sealed class EmpresaAdesaoNaoEncontradaException(string documento)
    : Exception($"Cliente {documento} sem linha em ASA_CASH_ADESAO — Geracao:Modo=CnabDireto exige os dados institucionais pra montar o header.");

/// <summary>
/// Modo <c>Geracao:Modo = CnabDireto</c>: o próprio worker escreve o
/// CNAB240, sem chamar o conversor externo. Ver
/// <see cref="EscritorCnab240Pagamento"/> pra formatação posicional e
/// <c>GeracaoOptions</c> pro trade-off de usar este modo.
/// </summary>
public class CnabDiretoGeradorCnabPagamento(EmpresaAdesaoRepository empresas) : IGeradorCnabPagamento
{
    public async Task<byte[]> GerarAsync(
        RetornoPagamentoJson dados, string documento, string nomeArquivo, string id, CancellationToken ct)
    {
        var empresa = await empresas.ObterPorDocumentoAsync(documento, ct)
            ?? throw new EmpresaAdesaoNaoEncontradaException(documento);

        return EscritorCnab240Pagamento.Escrever(dados, empresa);
    }
}
