using CnabRetorno.Core.Aplicacao.Dtos;
using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.RetornoCron.Worker.Persistencia;

/// <summary>
/// Registra o arquivo de retorno em <c>Cobranca.Arquivo</c> antes de
/// mandá-lo pro conversor assíncrono — o <c>ArquivoID</c> gerado aqui vai
/// no campo <c>id</c> da chamada, e é por ele que o Robô 2 reencontra o
/// cliente quando a conclusão chega via SQS (ver docs/regras-de-negocio.md).
///
/// Mesmo fluxo que a cash-cobranca-api já usa na entrada (cria a linha,
/// manda <c>appId + Arquivo.Id</c> pro conversor) — aqui é o espelho disso
/// pro sentido de saída.
/// </summary>
public class ArquivoRepository(CobrancaDbContext db)
{
    private const string AppId = "cash-cobranca";
    private const string CriadoPor = "arquivo-retorno-worker";
    private const string DescricaoProduto = "Cobrança";

    /// <summary>
    /// Cria a linha do arquivo de retorno e devolve o ID gerado. Os dados
    /// do cliente saem do próprio JSON que está sendo enviado (é a mesma
    /// informação, sem precisar de outra consulta). Estado inicial:
    /// EmProcessamento / EnviadoParaConversao — é exatamente o momento em
    /// que a linha nasce (TODO(a-confirmar): valores numéricos dos enums
    /// são suposição, ver <see cref="Core.Dominio.ArquivoStatus"/>).
    /// </summary>
    public async Task<Guid> RegistrarEnvioParaConversaoAsync(
        string nomeArquivo, string clienteDocumento, DadosConvertidos dados, CancellationToken ct)
    {
        var arquivo = new Arquivo
        {
            ArquivoID = Guid.NewGuid(),
            AppID = AppId,
            ArquivoNome = nomeArquivo,
            ClienteDocumento = clienteDocumento,
            ClienteTipoDocumento = short.TryParse(dados.Arquivo.Empresa.TipoInscricao, out var tipo) ? tipo : null,
            // Conta do cliente não existe no header do arquivo convertido —
            // vem do primeiro título (todos são do mesmo cliente).
            ClienteContaHeader = dados.Titulos.FirstOrDefault()?.Cliente.ContaHeader,
            CriadoPor = CriadoPor,
            DescricaoProduto = DescricaoProduto,
            DataCriacao = DateTime.UtcNow,
            ArquivoStatus = (short)Core.Dominio.ArquivoStatus.EmProcessamento,
            ArquivoEtapa = (short)Core.Dominio.ArquivoEtapa.EnviadoParaConversao,
        };

        db.Arquivos.Add(arquivo);
        await db.SaveChangesAsync(ct);

        return arquivo.ArquivoID;
    }

    /// <summary>Compensação: se o envio pro conversor falhar depois do
    /// INSERT, a linha é removida pra não ficar um arquivo "enviado pra
    /// conversão" que nunca foi. Mesmo padrão do handler de presign da
    /// cash-cobranca-api.</summary>
    public async Task RemoverAsync(Guid arquivoId, CancellationToken ct)
    {
        var arquivo = await db.Arquivos.FirstOrDefaultAsync(a => a.ArquivoID == arquivoId, ct);
        if (arquivo is null) return;

        db.Arquivos.Remove(arquivo);
        await db.SaveChangesAsync(ct);
    }
}
