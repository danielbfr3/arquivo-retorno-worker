using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CnabRetorno.PagamentoRetorno.Worker.Persistencia;

public class RegistroArquivoOptions
{
    public const string Secao = "RegistroArquivo";

    /// <summary>TODO(a-confirmar): o AppID do fluxo de pagamentos não foi
    /// informado. O de cobrança é <c>cash-cobranca</c> e o de VAN é
    /// <c>cash-cobranca-arquivo-van</c>; este é o palpite análogo.</summary>
    public string AppId { get; set; } = "cash-pagamento";

    public string CriadoPor { get; set; } = "arquivo-retorno-pagamento-worker";

    public string DescricaoProduto { get; set; } = "Pagamento";
}

/// <summary>
/// Registra o arquivo de retorno de pagamentos em <c>Pagamento.Arquivo</c>.
///
/// A linha é criada **antes** da conversão, porque o <c>ArquivoID</c> é o
/// id que vai na chamada ao conversor e no storage — um identificador só
/// na cadeia inteira. Se a conversão falhar, a linha é removida
/// (<see cref="RemoverAsync"/>): melhor não ter registro do que ter um
/// arquivo "gerado" que não existe.
/// </summary>
public class ArquivoRepository(PagamentoDbContext db, IOptions<RegistroArquivoOptions> opcoes)
{
    private readonly RegistroArquivoOptions _opt = opcoes.Value;

    public async Task<Guid> RegistrarGeracaoAsync(
        string nomeArquivo,
        string clienteDocumento,
        short clienteTipoDocumento,
        string? clienteContaHeader,
        CancellationToken ct)
    {
        var arquivo = new Arquivo
        {
            ArquivoID = Guid.NewGuid(),
            AppID = _opt.AppId,
            ArquivoNome = nomeArquivo,
            ClienteDocumento = clienteDocumento,
            ClienteTipoDocumento = clienteTipoDocumento,
            ClienteContaHeader = clienteContaHeader,
            CriadoPor = _opt.CriadoPor,
            DescricaoProduto = _opt.DescricaoProduto,
            DataCriacao = DateTime.UtcNow,
            ArquivoStatus = (short)ArquivoStatus.EmProcessamento,
            ArquivoEtapa = (short)ArquivoEtapa.EnviadoParaConversao,
        };

        db.Arquivos.Add(arquivo);
        await db.SaveChangesAsync(ct);

        return arquivo.ArquivoID;
    }

    /// <summary>Arquivo convertido e guardado no Gestor — fim do fluxo.
    /// TODO(a-confirmar): valores numéricos dos enums são suposição, ver
    /// <see cref="Core.Dominio.ArquivoStatus"/>.</summary>
    public async Task MarcarRegistradoAsync(Guid arquivoId, CancellationToken ct)
    {
        var arquivo = await db.Arquivos.AsTracking().FirstOrDefaultAsync(a => a.ArquivoID == arquivoId, ct);
        if (arquivo is null) return;

        arquivo.ArquivoStatus = (short)ArquivoStatus.Processado;
        arquivo.ArquivoEtapa = (short)ArquivoEtapa.Registrado;
        arquivo.DataAtualizacao = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Compensação: a conversão ou o upload falharam depois do
    /// INSERT.</summary>
    public async Task RemoverAsync(Guid arquivoId, CancellationToken ct)
    {
        var arquivo = await db.Arquivos.AsTracking().FirstOrDefaultAsync(a => a.ArquivoID == arquivoId, ct);
        if (arquivo is null) return;

        db.Arquivos.Remove(arquivo);
        await db.SaveChangesAsync(ct);
    }
}
