using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.RemessaVan.Worker.Persistencia;

/// <summary>
/// Idempotência por conteúdo (MD5) das remessas de VAN — ver
/// <see cref="RemessaIngerida"/> pro porquê de ser em banco.
/// </summary>
public class IngestaoIdempotenciaRepository(
    CobrancaDbContext db, ILogger<IngestaoIdempotenciaRepository> logger)
{
    public Task<bool> JaIngeridaAsync(string md5, CancellationToken ct)
        => db.RemessasIngeridas.AnyAsync(r => r.Md5 == md5, ct);

    /// <summary>
    /// Grava o hash **depois** da ingestão completa (upload + registro em
    /// <c>Cobranca.Arquivo</c>) — nessa ordem, um crash no meio reprocessa
    /// o arquivo (recuperável, o ArquivoID novo sobrescreve/duplica de
    /// forma visível) em vez de marcá-lo como ingerido sem ter ingerido
    /// (perda silenciosa).
    ///
    /// Violação de PK aqui significa corrida entre duas instâncias que o
    /// lock de execução deveria ter impedido — o arquivo já foi
    /// totalmente processado pelas duas, então degrada pra aviso em vez
    /// de falhar a ingestão que já deu certo.
    /// </summary>
    public async Task RegistrarAsync(
        string md5, Guid arquivoId, string nomeOriginal, CancellationToken ct)
    {
        var registro = new RemessaIngerida
        {
            Md5 = md5,
            ArquivoID = arquivoId,
            NomeOriginal = nomeOriginal,
            DataCriacao = DateTime.UtcNow,
        };
        db.RemessasIngeridas.Add(registro);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Solta a entidade do change tracker — sem isso, um
            // SaveChanges posterior no mesmo escopo tentaria o INSERT de
            // novo.
            db.Entry(registro).State = EntityState.Detached;

            logger.LogWarning(ex,
                "MD5 {Md5} já registrado por outra instância — arquivo {Nome} (ArquivoID {ArquivoId}) foi ingerido em duplicata; conferir Cobranca.Arquivo",
                md5, nomeOriginal, arquivoId);
        }
    }
}
