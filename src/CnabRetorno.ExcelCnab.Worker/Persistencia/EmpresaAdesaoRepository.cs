using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.ExcelCnab.Worker.Persistencia;

/// <summary>
/// "Pegar os dados daquele cliente na base de adesão" — a busca é pelo
/// CNPJ extraído do nome do arquivo.
/// </summary>
public class EmpresaAdesaoRepository(AdesaoDbContext db)
{
    /// <summary>Devolve <c>null</c> quando não existe cliente com aquele
    /// documento. Diferente do <c>ContaHeader</c> do fluxo antigo, isto
    /// **barra** o envio: a razão social é parte do payload que o
    /// conversor recebe, e mandar a planilha de um cliente que não está
    /// cadastrado é enviar um arquivo sem dono.</summary>
    public Task<EmpresaAdesao?> ObterPorDocumentoAsync(string documento, CancellationToken ct)
        => db.Empresas.FirstOrDefaultAsync(e => e.Documento == documento, ct);
}
