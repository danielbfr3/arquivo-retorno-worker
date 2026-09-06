using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.ExcelCnab.Worker.Persistencia;

/// <summary>
/// "Pegar os dados que devem preencher a planilha daquele documento" — a
/// busca é pelo CNPJ extraído do nome do arquivo. Também é a única fonte
/// da razão social do cliente (chave <see cref="DocumentoDados.ChaveRazaoSocial"/>
/// dentro do JSON) — não existe mais uma base de adesão separada.
///
/// Só leitura: quem escreve nesta tabela é outro sistema, não este worker.
/// </summary>
public class DocumentoDadosRepository(CobrancaDbContext db)
{
    /// <summary>Devolve <c>null</c> quando não existe linha pra aquele
    /// documento — nesse caso não há o que preencher na planilha e o
    /// arquivo vai pra quarentena.</summary>
    public Task<DocumentoDados?> ObterPorDocumentoAsync(string numeroDocumento, CancellationToken ct)
        => db.DocumentosDados.FirstOrDefaultAsync(d => d.NumeroDocumento == numeroDocumento, ct);
}
