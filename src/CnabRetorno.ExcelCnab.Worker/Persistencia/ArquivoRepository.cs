using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CnabRetorno.ExcelCnab.Worker.Persistencia;

public class RegistroArquivoOptions
{
    public const string Secao = "RegistroArquivo";

    /// <summary>AppID gravado na linha — o mesmo <c>cash-cobranca</c>
    /// usado na chamada do conversor, pra que os dois lados falem do mesmo
    /// aplicativo. Configuração e não constante: o ecossistema tem mais de
    /// um AppID em uso.</summary>
    public string AppId { get; set; } = "cash-cobranca";

    public string CriadoPor { get; set; } = "arquivo-excel-cnab-worker";

    public string DescricaoProduto { get; set; } = "Cobrança";
}

/// <summary>
/// Cria e atualiza a linha da planilha em <c>Cobranca.Arquivo</c>.
///
/// O <c>ArquivoID</c> é gerado **antes** da chamada do conversor e vai
/// como <c>id</c> da conversão: é ele que a mensagem de conclusão devolve,
/// e é por ele que quem consome essa conclusão recupera cliente e nome do
/// arquivo (docs/cash-cobranca-referencia.md §2.4). Por isso a linha nasce
/// antes do envio — a ordem inversa deixaria a conclusão chegar sem ter
/// onde se ancorar.
/// </summary>
public class ArquivoRepository(CobrancaDbContext db, IOptions<RegistroArquivoOptions> opcoes)
{
    private readonly RegistroArquivoOptions _opt = opcoes.Value;

    /// <summary>
    /// Estado inicial: <c>EmProcessamento</c> / <c>EnviadoParaConversao</c>
    /// — o par que a máquina de estados da API dona da tabela permite
    /// (§1.1). A etapa descreve o que vem imediatamente a seguir nesta
    /// mesma execução; se o envio falhar, <see cref="MarcarInvalidoAsync"/>
    /// corrige.
    /// TODO(a-confirmar): valores numéricos dos enums são suposição, ver
    /// <see cref="Core.Dominio.ArquivoStatus"/>.
    /// </summary>
    public async Task RegistrarAsync(
        Guid arquivoId,
        string nomeArquivo,
        string clienteDocumento,
        CancellationToken ct)
    {
        db.Arquivos.Add(new Arquivo
        {
            ArquivoID = arquivoId,
            AppID = _opt.AppId,
            ArquivoNome = nomeArquivo,
            ClienteDocumento = clienteDocumento,
            // 14 dígitos é CNPJ (2), qualquer outro tamanho é CPF (1) —
            // domínio G005 do layout FEBRABAN.
            ClienteTipoDocumento = clienteDocumento.Length == 14 ? (short)2 : (short)1,
            CriadoPor = _opt.CriadoPor,
            DescricaoProduto = _opt.DescricaoProduto,
            DataCriacao = DateTime.UtcNow,
            ArquivoStatus = (short)ArquivoStatus.EmProcessamento,
            ArquivoEtapa = (short)ArquivoEtapa.EnviadoParaConversao,
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// O conversor recusou o arquivo. A linha fica como
    /// <c>EmProcessamento</c> / <c>ArquivoInvalido</c> em vez de continuar
    /// dizendo "enviado pra conversão" — uma conclusão que nunca vai
    /// chegar deixaria a linha pendurada e invisível para quem
    /// monitora.
    ///
    /// Best-effort de propósito: quem chama já está tratando um erro, e
    /// falhar aqui não pode mascarar o erro original (ver
    /// <c>ProcessadorArquivoExcelService</c>).
    /// </summary>
    public async Task MarcarInvalidoAsync(Guid arquivoId, CancellationToken ct)
    {
        await db.Arquivos
            .Where(a => a.ArquivoID == arquivoId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(a => a.ArquivoEtapa, (short)ArquivoEtapa.ArquivoInvalido)
                    .SetProperty(a => a.DataAtualizacao, DateTime.UtcNow),
                ct);
    }
}
