using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.PagamentoRetorno.Worker.Persistencia;

public class EmpresaAdesaoRepository(AdesaoDbContext db)
{
    public Task<Core.Dominio.EmpresaAdesao?> ObterPorDocumentoAsync(string documento, CancellationToken ct)
        => db.Empresas.FirstOrDefaultAsync(e => e.Documento == documento, ct);
}
