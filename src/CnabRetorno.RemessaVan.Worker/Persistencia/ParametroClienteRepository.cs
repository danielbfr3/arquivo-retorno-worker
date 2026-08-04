using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.RemessaVan.Worker.Persistencia;

/// <summary>
/// Passo 4 do checklist: "recuperar o ContaHeader do cliente através do
/// CNPJ". A fonte é <c>Cobranca.Parametro</c>, a mesma tabela por cliente
/// que guarda o <c>SequencialAtual</c>.
/// </summary>
public class ParametroClienteRepository(CobrancaDbContext db)
{
    /// <summary>
    /// Devolve <c>null</c> quando o cliente não tem linha de parâmetro.
    /// Não é erro: a coluna é anulável em <c>Cobranca.Arquivo</c>, e
    /// barrar a ingestão por causa dela faria a remessa ficar parada na
    /// pasta. Quem chama loga o aviso.
    /// </summary>
    public async Task<string?> ObterContaHeaderAsync(string documento, CancellationToken ct)
        => await db.Parametros
            .Where(p => p.Documento == documento)
            .Select(p => p.ContaHeader)
            .FirstOrDefaultAsync(ct);
}
