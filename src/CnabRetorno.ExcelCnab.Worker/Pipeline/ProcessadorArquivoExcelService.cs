using CnabRetorno.Core.Aplicacao;
using CnabRetorno.Core.Aplicacao.Dtos;
using CnabRetorno.ExcelCnab.Worker.Armazenamento;
using CnabRetorno.ExcelCnab.Worker.Notificacao;
using CnabRetorno.ExcelCnab.Worker.Origem;
using CnabRetorno.ExcelCnab.Worker.Persistencia;
using CnabRetorno.ExcelCnab.Worker.Http;
using Microsoft.Extensions.Options;

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
///   <item>guarda uma cópia da planilha em cada destino habilitado
///   (Gestor de Arquivos e bucket S3);</item>
///   <item>envia a planilha ao conversor assíncrono (pipeline
///   <c>excel-cnab</c>), com CNPJ e razão social em JSON no corpo da
///   mensagem;</item>
///   <item>move o arquivo pra Backup;</item>
///   <item>publica o aviso de conclusão no tópico SNS.</item>
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
    ArmazenadorDeCopias copias,
    ILayoutConversaoApiClient conversor,
    INotificadorConclusao notificador,
    IOptions<ConversaoOptions> opcoesConversao,
    TimeProvider tempo,
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

        var metadados = new MetadadosCliente(reconhecido.Cnpj, empresa.RazaoSocial.Trim()).Serializar();

        ConvertAsyncUploadResponse aceite;
        try
        {
            // 4. Cópias, antes do envio: é a partir do envio que o arquivo
            //    sai da pasta de entrada, e a cópia é o que sobra dele.
            //    Uma cópia que falha só chega a interromper o processamento
            //    se Armazenamento:FalhaBloqueiaEnvio estiver ligado; no
            //    padrão, sai erro no log e o fluxo segue.
            await copias.ArmazenarAsync(arquivoId, pendente.Nome, conteudo, ct);

            // 5. Envio ao conversor assíncrono.
            aceite = await conversor.EnviarParaConversaoAsync(
                conteudo, pendente.Nome, arquivoId, metadados, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A linha já existe e a conversão não vai acontecer. Marcar
            // como inválida impede que ela fique pendurada esperando uma
            // conclusão que nunca chega; o arquivo vai pra quarentena
            // porque deixá-lo na origem faria o próximo ciclo criar uma
            // segunda linha, com ArquivoID novo, pro mesmo arquivo.
            //
            // Os dois passos falham do mesmo jeito de propósito: seja um
            // bucket obrigatório fora do ar ou o conversor recusando, o
            // estado a limpar é o mesmo — linha criada, arquivo ainda na
            // pasta.
            logger.LogError(ex,
                "Falha ao processar {Nome} (ArquivoID {ArquivoId}, cliente {Cnpj}) — arquivo movido pra quarentena",
                pendente.Nome, arquivoId, reconhecido.Cnpj);

            await MarcarInvalidoSemMascararErroAsync(arquivoId, ct);
            origem.MoverParaQuarentena(pendente.Caminho);
            return new PlanilhaProcessada(ResultadoEnvio.Falhou, arquivoId, reconhecido.Cnpj);
        }

        // 6. Só depois do aceite o arquivo sai da pasta de entrada.
        origem.MoverParaBackup(pendente.Caminho);

        logger.LogInformation(
            "Planilha {Nome} do cliente {Cnpj} ({RazaoSocial}) enviada pra conversão — ArquivoID {ArquivoId}, job {JobId}",
            pendente.Nome, reconhecido.Cnpj, empresa.RazaoSocial, arquivoId, aceite.JobId ?? "<sem jobId>");

        // 7. Aviso de conclusão, por último e sem poder derrubar nada: a
        //    planilha já foi aceita e o arquivo já saiu da pasta. Deixar o
        //    erro subir aqui marcaria como falha um arquivo que foi
        //    processado com sucesso — e o próximo ciclo não o
        //    reprocessaria, porque ele não está mais na origem.
        await NotificarSemDerrubarOEnvioAsync(
            new PlanilhaEnviadaEvento
            {
                ArquivoId = arquivoId,
                ArquivoNome = pendente.Nome,
                Cnpj = reconhecido.Cnpj,
                RazaoSocial = empresa.RazaoSocial.Trim(),
                AppId = opcoesConversao.Value.AppId,
                Pipeline = opcoesConversao.Value.Pipeline,
                JobId = aceite.JobId,
                OcorridoEm = tempo.GetUtcNow(),
            },
            ct);

        return new PlanilhaProcessada(ResultadoEnvio.Enviado, arquivoId, reconhecido.Cnpj);
    }

    /// <summary>
    /// O aviso é a última coisa que acontece e a menos crítica: o trabalho
    /// já está feito e registrado. Um tópico fora do ar não pode
    /// transformar um arquivo processado em falha — sai erro no log, que é
    /// o que permite reenviar o aviso à mão se alguém depender dele.
    /// </summary>
    private async Task NotificarSemDerrubarOEnvioAsync(PlanilhaEnviadaEvento evento, CancellationToken ct)
    {
        try
        {
            await notificador.NotificarAsync(evento, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Planilha {Nome} (ArquivoID {ArquivoId}) foi processada, mas o aviso de conclusão não foi publicado",
                evento.ArquivoNome, evento.ArquivoId);
        }
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
