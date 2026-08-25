using CnabRetorno.Core.Aplicacao;
using CnabRetorno.Core.Aplicacao.Dtos;
using CnabRetorno.ExcelCnab.Worker.Origem;
using CnabRetorno.ExcelCnab.Worker.Persistencia;

namespace CnabRetorno.ExcelCnab.Worker.Pipeline;

public enum ResultadoEnvio
{
    Enviado,
    NaoReconhecido,
    ClienteNaoEncontrado,
    Falhou,
}

public sealed record PlanilhaProcessada(ResultadoEnvio Resultado, Guid? ArquivoId = null, string? Cnpj = null);

/// <summary>
/// O fluxo de uma planilha, na ordem em que ele acontece:
///
/// <list type="number">
///   <item>lê o CNPJ do nome do arquivo (<c>Simplificado_{cnpj}.xlsx</c>);</item>
///   <item>busca o cliente na base de adesão pra saber a razão social;</item>
///   <item>cria a linha em <c>Cobranca.Arquivo</c> com um ArquivoID novo;</item>
///   <item>envia a planilha ao conversor assíncrono (pipeline
///   <c>excel-cnab</c>), com CNPJ e razão social em JSON no corpo da
///   mensagem;</item>
///   <item>move o arquivo pra Backup.</item>
/// </list>
///
/// A ordem "registra, depois envia" não é estética: o conversor é
/// assíncrono e a conclusão chega depois, correlacionada pelo ArquivoID —
/// se a linha não existir, a conclusão chega sem ter onde se ancorar
/// (docs/cash-cobranca-referencia.md §2.4).
///
/// Não há leitura da planilha em lugar nenhum: o conteúdo é repassado como
/// bytes opacos. Quem entende o formato é o pipeline do conversor.
/// </summary>
public class ProcessadorArquivoExcelService(
    PastaOrigemExcel origem,
    NomeArquivoSimplificado nomes,
    EmpresaAdesaoRepository adesao,
    ArquivoRepository arquivos,
    ILayoutConversaoApiClient conversor,
    ILogger<ProcessadorArquivoExcelService> logger)
{
    public async Task<PlanilhaProcessada> ProcessarAsync(ArquivoPendente pendente, CancellationToken ct)
    {
        // 1. CNPJ do nome do arquivo — a única identificação do cliente
        //    que existe, já que a planilha não é aberta.
        if (!nomes.TentarReconhecer(pendente.Nome, out var reconhecido))
        {
            logger.LogWarning(
                "Arquivo {Nome} fora do padrão esperado (Simplificado_<cnpj>.xlsx|.xls) — movendo pra quarentena",
                pendente.Nome);
            origem.MoverParaQuarentena(pendente.Caminho);
            return new PlanilhaProcessada(ResultadoEnvio.NaoReconhecido);
        }

        // 2. Dados do cliente na base de adesão. Sem razão social não há o
        //    que mandar no corpo da mensagem — o arquivo espera na
        //    quarentena até o cadastro existir, em vez de ir ao conversor
        //    identificado pela metade.
        var empresa = await adesao.ObterPorDocumentoAsync(reconhecido.Cnpj, ct);
        if (empresa is null || string.IsNullOrWhiteSpace(empresa.RazaoSocial))
        {
            logger.LogError(
                "Cliente {Cnpj} {Motivo} na base de adesão — arquivo {Nome} movido pra quarentena, sem envio",
                reconhecido.Cnpj,
                empresa is null ? "não encontrado" : "sem razão social",
                pendente.Nome);
            origem.MoverParaQuarentena(pendente.Caminho);
            return new PlanilhaProcessada(ResultadoEnvio.ClienteNaoEncontrado, Cnpj: reconhecido.Cnpj);
        }

        var conteudo = await origem.LerAsync(pendente.Caminho, ct);

        // 3. O ArquivoID nasce aqui e vale pro registro e pra conversão —
        //    um id só na cadeia inteira, nunca um GUID novo por chamada.
        var arquivoId = Guid.NewGuid();
        await arquivos.RegistrarAsync(arquivoId, pendente.Nome, reconhecido.Cnpj, ct);

        // 4. Envio ao conversor assíncrono.
        var metadados = new MetadadosCliente(reconhecido.Cnpj, empresa.RazaoSocial.Trim()).Serializar();

        ConvertAsyncUploadResponse aceite;
        try
        {
            aceite = await conversor.EnviarParaConversaoAsync(
                conteudo, pendente.Nome, arquivoId, metadados, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A linha já existe e a conversão não vai concluir. Marcar
            // como inválida impede que ela fique pendurada esperando uma
            // conclusão que nunca chega; o arquivo vai pra quarentena
            // porque deixá-lo na origem faria o próximo ciclo criar uma
            // segunda linha, com ArquivoID novo, pro mesmo arquivo.
            logger.LogError(ex,
                "Falha ao enviar {Nome} (ArquivoID {ArquivoId}, cliente {Cnpj}) ao conversor — arquivo movido pra quarentena",
                pendente.Nome, arquivoId, reconhecido.Cnpj);

            await MarcarInvalidoSemMascararErroAsync(arquivoId, ct);
            origem.MoverParaQuarentena(pendente.Caminho);
            return new PlanilhaProcessada(ResultadoEnvio.Falhou, arquivoId, reconhecido.Cnpj);
        }

        // 5. Só depois do aceite o arquivo sai da pasta de entrada.
        origem.MoverParaBackup(pendente.Caminho);

        logger.LogInformation(
            "Planilha {Nome} do cliente {Cnpj} ({RazaoSocial}) enviada pra conversão — ArquivoID {ArquivoId}, job {JobId}",
            pendente.Nome, reconhecido.Cnpj, empresa.RazaoSocial, arquivoId, aceite.JobId ?? "<sem jobId>");

        return new PlanilhaProcessada(ResultadoEnvio.Enviado, arquivoId, reconhecido.Cnpj);
    }

    private async Task MarcarInvalidoSemMascararErroAsync(Guid arquivoId, CancellationToken ct)
    {
        try
        {
            await arquivos.MarcarInvalidoAsync(arquivoId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // O erro que importa é o do envio, já logado acima. Se o banco
            // também estiver fora, deixar esta exceção subir trocaria a
            // causa raiz por um erro de consequência.
            logger.LogWarning(ex,
                "Não foi possível marcar o ArquivoID {ArquivoId} como inválido — a linha ficou como EnviadoParaConversao",
                arquivoId);
        }
    }
}
