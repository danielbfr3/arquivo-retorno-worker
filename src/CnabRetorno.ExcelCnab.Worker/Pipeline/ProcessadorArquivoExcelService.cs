using CnabRetorno.Core.Aplicacao;
using CnabRetorno.Core.Aplicacao.Dtos;
using CnabRetorno.Core.Dominio;
using CnabRetorno.ExcelCnab.Worker.Origem;
using CnabRetorno.ExcelCnab.Worker.Persistencia;
using CnabRetorno.ExcelCnab.Worker.Planilha;

namespace CnabRetorno.ExcelCnab.Worker.Pipeline;

public enum ResultadoEnvio
{
    Enviado,
    NaoReconhecido,
    ClienteNaoEncontrado,
    DocumentoSemDados,
    ColunaNaoEncontrada,
    Falhou,
}

public sealed record PlanilhaProcessada(ResultadoEnvio Resultado, Guid? ArquivoId = null, string? Cnpj = null);

/// <summary>
/// O fluxo de uma planilha, na ordem em que ele acontece:
///
/// <list type="number">
///   <item>lê o CNPJ do nome do arquivo (<c>Simplificado_{cnpj}.xlsx</c>);</item>
///   <item>busca o cliente na base de adesão pra saber a razão social;</item>
///   <item>busca os dados de preenchimento em <c>Cobranca.DocumentoDados</c>,
///   pelo mesmo CNPJ;</item>
///   <item>abre a planilha em memória e escreve cada valor do JSON de dados
///   na coluna cujo cabeçalho bate, em todas as linhas de dados;</item>
///   <item>cria a linha em <c>Cobranca.Arquivo</c> com um ArquivoID novo —
///   só depois que os bytes finais já estão prontos;</item>
///   <item>envia a planilha já preenchida ao conversor assíncrono (pipeline
///   <c>excel-cnab</c>), com CNPJ e razão social em JSON no corpo da
///   mensagem;</item>
///   <item>grava a versão preenchida em Backup e apaga o arquivo original
///   da pasta de entrada.</item>
/// </list>
///
/// A ordem "preenche, registra, depois envia" não é estética: o conversor é
/// assíncrono e a conclusão chega depois, correlacionada pelo ArquivoID —
/// se a linha não existir, a conclusão chega sem ter onde se ancorar
/// (docs/cash-cobranca-referencia.md §2.4). E registrar antes de saber que
/// a planilha pôde ser preenchida criaria uma linha órfã pra um arquivo que
/// nunca chega a ser enviado.
/// </summary>
public class ProcessadorArquivoExcelService(
    PastaOrigemExcel origem,
    NomeArquivoSimplificado nomes,
    EmpresaAdesaoRepository adesao,
    DocumentoDadosRepository documentoDados,
    PreenchedorPlanilhaExcel preenchedor,
    ArquivoRepository arquivos,
    ILayoutConversaoApiClient conversor,
    ILogger<ProcessadorArquivoExcelService> logger)
{
    public async Task<PlanilhaProcessada> ProcessarAsync(ArquivoPendente pendente, CancellationToken ct)
    {
        // 1. CNPJ do nome do arquivo — a única identificação do documento
        //    que existe antes de abrir a planilha.
        if (!nomes.TentarReconhecer(pendente.Nome, out var reconhecido))
        {
            logger.LogWarning(
                "Arquivo {Nome} fora do padrão esperado (Simplificado_<cnpj>.xlsx) — movendo pra quarentena",
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

        // 3. Dados de preenchimento em Cobranca.DocumentoDados — só leitura,
        //    quem popula é outro sistema. Sem linha, ou JSON inválido/vazio,
        //    não há o que escrever na planilha.
        var valores = await ObterValoresParaPreencherAsync(reconhecido.Cnpj, ct);
        if (valores is null)
        {
            logger.LogError(
                "Documento {Cnpj} sem dados válidos em Cobranca.DocumentoDados — arquivo {Nome} movido pra quarentena, sem envio",
                reconhecido.Cnpj, pendente.Nome);
            origem.MoverParaQuarentena(pendente.Caminho);
            return new PlanilhaProcessada(ResultadoEnvio.DocumentoSemDados, Cnpj: reconhecido.Cnpj);
        }

        var conteudoOriginal = await origem.LerAsync(pendente.Caminho, ct);

        // 4. Preenchimento em memória. Uma chave sem coluna correspondente
        //    (ou planilha sem linha de dados) rejeita o arquivo inteiro:
        //    nada é registrado nem enviado, e o original vai pra
        //    quarentena intacto — é a evidência do problema.
        byte[] conteudoPreenchido;
        try
        {
            conteudoPreenchido = preenchedor.Preencher(conteudoOriginal, valores);
        }
        catch (Exception ex) when (ex is ColunaNaoEncontradaException or PlanilhaSemLinhasDeDadosException)
        {
            logger.LogError(ex,
                "Falha ao preencher {Nome} (cliente {Cnpj}) — arquivo movido pra quarentena, sem envio",
                pendente.Nome, reconhecido.Cnpj);
            origem.MoverParaQuarentena(pendente.Caminho);
            return new PlanilhaProcessada(ResultadoEnvio.ColunaNaoEncontrada, Cnpj: reconhecido.Cnpj);
        }

        // 5. O ArquivoID nasce aqui e vale pro registro e pra conversão —
        //    um id só na cadeia inteira, nunca um GUID novo por chamada.
        //    Só chega até aqui uma planilha que já foi preenchida com
        //    sucesso.
        var arquivoId = Guid.NewGuid();
        await arquivos.RegistrarAsync(arquivoId, pendente.Nome, reconhecido.Cnpj, ct);

        var metadados = new MetadadosCliente(reconhecido.Cnpj, empresa.RazaoSocial.Trim()).Serializar();

        ConvertAsyncUploadResponse aceite;
        try
        {
            // 6. Envio ao conversor assíncrono — bytes já preenchidos, não
            //    mais os originais.
            aceite = await conversor.EnviarParaConversaoAsync(
                conteudoPreenchido, pendente.Nome, arquivoId, metadados, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A linha já existe e a conversão não vai acontecer. Marcar
            // como inválida impede que ela fique pendurada esperando uma
            // conclusão que nunca chega; o arquivo original vai pra
            // quarentena porque deixá-lo na origem faria o próximo ciclo
            // criar uma segunda linha, com ArquivoID novo, pro mesmo
            // arquivo.
            logger.LogError(ex,
                "Falha ao enviar {Nome} (ArquivoID {ArquivoId}, cliente {Cnpj}) — arquivo movido pra quarentena",
                pendente.Nome, arquivoId, reconhecido.Cnpj);

            await MarcarInvalidoSemMascararErroAsync(arquivoId, ct);
            origem.MoverParaQuarentena(pendente.Caminho);
            return new PlanilhaProcessada(ResultadoEnvio.Falhou, arquivoId, reconhecido.Cnpj);
        }

        // 7. Só depois do aceite o arquivo sai da pasta de entrada — e o
        //    que fica em Backup é a versão preenchida, que foi de fato
        //    mandada ao conversor.
        await origem.GravarNoBackupAsync(pendente.Caminho, conteudoPreenchido, ct);

        logger.LogInformation(
            "Planilha {Nome} do cliente {Cnpj} ({RazaoSocial}) preenchida e enviada pra conversão — ArquivoID {ArquivoId}, job {JobId}",
            pendente.Nome, reconhecido.Cnpj, empresa.RazaoSocial, arquivoId, aceite.JobId ?? "<sem jobId>");

        return new PlanilhaProcessada(ResultadoEnvio.Enviado, arquivoId, reconhecido.Cnpj);
    }

    /// <summary>Devolve <c>null</c> quando não há linha pra este documento
    /// ou o JSON de <c>Dados</c> não desserializa em nada aproveitável (ver
    /// <see cref="DocumentoDados.DesserializarDados"/>) — os dois casos
    /// tratados como "documento sem dados" pra quem chama.</summary>
    private async Task<IReadOnlyDictionary<string, string>?> ObterValoresParaPreencherAsync(
        string cnpj, CancellationToken ct)
    {
        var linha = await documentoDados.ObterPorDocumentoAsync(cnpj, ct);
        if (linha is null) return null;

        var valores = linha.DesserializarDados();
        if (valores is null)
            logger.LogError("JSON inválido ou vazio em Cobranca.DocumentoDados.Dados pro documento {Cnpj}", cnpj);

        return valores;
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
