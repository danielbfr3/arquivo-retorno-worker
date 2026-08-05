using CnabRetorno.Core.Aplicacao.Dtos;

namespace CnabRetorno.PagamentoRetorno.Worker.Cnab;

/// <summary>
/// Produz o CNAB240 final a partir do <see cref="RetornoPagamentoJson"/>
/// já montado — as duas estratégias de <c>Geracao:Modo</c> implementam
/// isto, e <c>ProcessadorRetornoPagamentoService</c> não sabe qual das
/// duas está em uso.
/// </summary>
public interface IGeradorCnabPagamento
{
    /// <param name="documento">CNPJ/CPF do cliente — só usado pela
    /// estratégia <c>CnabDireto</c>, pra buscar os dados institucionais em
    /// <c>ASA_CASH_ADESAO</c>. A estratégia via conversor ignora.</param>
    /// <param name="id">O <c>ArquivoID</c> — mesmo identificador usado no
    /// storage; a estratégia via conversor o envia como correlação da
    /// chamada HTTP.</param>
    Task<byte[]> GerarAsync(
        RetornoPagamentoJson dados, string documento, string nomeArquivo, string id, CancellationToken ct);
}
