using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CnabRetorno.RemessaVan.Worker.Persistencia;

public class RegistroArquivoOptions
{
    public const string Secao = "RegistroArquivo";

    /// <summary>AppID gravado na linha. A extração de 03/08/2026 mostra
    /// <c>cash-cobranca-arquivo-van</c> no fluxo de VAN — valor diferente
    /// do <c>cash-cobranca</c> usado no fluxo de cobrança, e por isso
    /// configuração e não constante.</summary>
    public string AppId { get; set; } = "cash-cobranca-arquivo-van";

    public string CriadoPor { get; set; } = "arquivo-remessa-van-worker";

    public string DescricaoProduto { get; set; } = "Cobrança";
}

/// <summary>
/// Cria a linha do arquivo de remessa em <c>Cobranca.Arquivo</c> — passo 9
/// do checklist: "salva na tabela de arquivos um registro sobre este
/// arquivo de remessa, com Conta Header, documento do cliente, nome do
/// arquivo retificado no padrão ASA".
///
/// O <c>ArquivoID</c> é gerado **antes** do upload (passo 1 do checklist,
/// "gera um GUID para este arquivo") e usado como id no Gestor de
/// Arquivos, e não o contrário: é o que faz o objeto no storage e a linha
/// no banco terem o mesmo identificador, e o que torna um reprocessamento
/// idempotente do lado do storage.
/// </summary>
public class ArquivoRepository(CobrancaDbContext db, IOptions<RegistroArquivoOptions> opcoes)
{
    private readonly RegistroArquivoOptions _opt = opcoes.Value;

    /// <summary>
    /// Estado inicial: <c>EmProcessamento</c> / <c>GeradoUrlBucket</c> — o
    /// arquivo está no bucket, mas ainda não foi conferido nem convertido
    /// (quem faz isso é outro worker do ecossistema).
    /// TODO(a-confirmar): valores numéricos dos enums são suposição, ver
    /// <see cref="Core.Dominio.ArquivoStatus"/>.
    /// </summary>
    public async Task RegistrarRemessaAsync(
        Guid arquivoId,
        string nomeArquivoAsa,
        string clienteDocumento,
        string? clienteContaHeader,
        CancellationToken ct)
    {
        db.Arquivos.Add(new Arquivo
        {
            ArquivoID = arquivoId,
            AppID = _opt.AppId,
            ArquivoNome = nomeArquivoAsa,
            ClienteDocumento = clienteDocumento,
            // 14 dígitos é CNPJ (2), qualquer outro tamanho é CPF (1) —
            // domínio G005 do layout FEBRABAN.
            ClienteTipoDocumento = clienteDocumento.Length == 14 ? (short)2 : (short)1,
            ClienteContaHeader = clienteContaHeader,
            CriadoPor = _opt.CriadoPor,
            DescricaoProduto = _opt.DescricaoProduto,
            DataCriacao = DateTime.UtcNow,
            ArquivoStatus = (short)ArquivoStatus.EmProcessamento,
            ArquivoEtapa = (short)ArquivoEtapa.GeradoUrlBucket,
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Já existe linha com este ArquivoID? Só acontece se o mesmo
    /// GUID for reaproveitado — checagem barata que evita um INSERT
    /// duplicado numa tabela compartilhada.</summary>
    public Task<bool> ExisteAsync(Guid arquivoId, CancellationToken ct)
        => db.Arquivos.AnyAsync(a => a.ArquivoID == arquivoId, ct);
}
