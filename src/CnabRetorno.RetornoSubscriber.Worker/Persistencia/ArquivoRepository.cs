using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.RetornoSubscriber.Worker.Persistencia;

/// <summary>
/// Ponte entre a mensagem SQS e o cliente: o <c>id</c> que chega na
/// conclusão da conversão é o <c>ArquivoID</c> da linha que o Robô 1
/// criou em <c>Cobranca.Arquivo</c> antes de enviar (ver
/// docs/regras-de-negocio.md) — é daqui que saem documento/nome do
/// arquivo, sem precisar inferir nada do conteúdo baixado.
/// </summary>
public class ArquivoRepository(CobrancaDbContext db)
{
    public Task<Arquivo?> ObterPorIdAsync(Guid arquivoId, CancellationToken ct)
        => db.Arquivos.FirstOrDefaultAsync(a => a.ArquivoID == arquivoId, ct);

    /// <summary>Fecha o ciclo da máquina de estados: o arquivo final foi
    /// armazenado no Gestor de Arquivos, então a linha sai de
    /// "em processamento" (TODO(a-confirmar): valores numéricos dos enums
    /// são suposição, ver <see cref="Core.Dominio.ArquivoStatus"/>).</summary>
    public async Task MarcarRegistradoAsync(Arquivo arquivo, CancellationToken ct)
    {
        arquivo.ArquivoStatus = (short)Core.Dominio.ArquivoStatus.Processado;
        arquivo.ArquivoEtapa = (short)Core.Dominio.ArquivoEtapa.Registrado;
        arquivo.DataAtualizacao = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
