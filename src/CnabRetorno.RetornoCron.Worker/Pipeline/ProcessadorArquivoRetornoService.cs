using System.Security.Cryptography;
using System.Text.Json;
using CnabRetorno.Core.Aplicacao;
using CnabRetorno.Core.Aplicacao.Dtos;
using CnabRetorno.Core.Cnab240;
using CnabRetorno.RetornoCron.Worker.Http;
using CnabRetorno.RetornoCron.Worker.Json;
using CnabRetorno.RetornoCron.Worker.Origem;
using CnabRetorno.RetornoCron.Worker.Persistencia;
using Microsoft.Extensions.Logging;

namespace CnabRetorno.RetornoCron.Worker.Pipeline;

public enum ResultadoArquivo { Processado, Duplicado, Falha }

/// <summary>Resultado de um arquivo processado com sucesso — carrega o
/// CNPJ resolvido, usado por <see cref="ProcessadorClientesSemArquivoService"/>
/// pra saber quem já foi coberto neste lote.</summary>
public sealed record ArquivoProcessado(ResultadoArquivo Resultado, string? Cnpj = null);

/// <summary>
/// Processa um arquivo V (+ PV, se existir): extrai o CNPJ do header, envia
/// V e PV **separadamente** pro conversor síncrono, consulta pendências
/// (títulos/instruções negados ou com erro em D-1) na base CASH_COBRANCA,
/// mescla os dois/três JSONs resultantes num único <c>DadosConvertidos</c>
/// (ver <see cref="MesclagemDadosConvertidos"/>), **registra o arquivo de
/// retorno em <c>Cobranca.Arquivo</c>** e manda pro conversor assíncrono
/// usando o ID dessa linha como correlação — é por ele que o Robô 2
/// reencontra o cliente quando a conclusão chega (ver
/// <see cref="ArquivoRepository"/>). Idempotência de reprocessamento
/// continua sendo via <see cref="ControleIdempotenciaDiario"/> (arquivo,
/// não banco).
///
/// Registrado Scoped e resolvido a partir de um escopo de DI próprio por
/// arquivo — <see cref="CobrancaDbContext"/> (usado por
/// <see cref="PendenciasParaTitulosConvertidosFactory"/> e
/// <see cref="ArquivoRepository"/>) não é thread-safe, mesmo motivo já
/// documentado no restante do projeto.
/// </summary>
public class ProcessadorArquivoRetornoService(
    PastaOrigemArquivosRetorno origem,
    ControleIdempotenciaDiario controleIdempotencia,
    ControlePendenciasReportadasDiario controlePendencias,
    PendenciasParaTitulosConvertidosFactory pendenciasFactory,
    MesclagemDadosConvertidos mesclagem,
    ArquivoRepository arquivos,
    SequencialArquivoRepository sequenciais,
    ILayoutConversaoApiClient conversor,
    ILogger<ProcessadorArquivoRetornoService> logger)
{
    public async Task<ArquivoProcessado> ProcessarAsync(ArquivoVPendente pendente, CancellationToken ct)
    {
        var conteudoV = await origem.LerAsync(pendente.Caminho, ct);
        var md5V = Convert.ToHexString(MD5.HashData(conteudoV));

        if (controleIdempotencia.JaProcessadoHoje(md5V))
        {
            logger.LogWarning(
                "Arquivo {Nome} (MD5 {Md5}) já processado hoje — movendo pra Backup sem reprocessar",
                pendente.Nome, md5V);
            await origem.MoverParaBackupAsync(pendente.Caminho, ct);
            return new ArquivoProcessado(ResultadoArquivo.Duplicado);
        }

        // Extrai o CNPJ logo na leitura do V, antes de qualquer outro passo.
        var cnpj = Cnab240Campos.ExtrairCnpjHeaderArquivo(conteudoV);

        // Lock por CNPJ: evita que duas V do mesmo cliente (dois lotes
        // intraday, reprocessamento manual) consultem pendências em
        // paralelo e dupliquem o mesmo item no JSON — precisa ficar seguro
        // até depois de RegistrarReportadas, ver
        // ControlePendenciasReportadasDiario.AdquirirLockCnpjAsync.
        await using var lockCnpj = await controlePendencias.AdquirirLockCnpjAsync(cnpj, ct);

        var caminhoPv = origem.LocalizarPvCorrespondente(pendente.ClientId);
        var nomePv = caminhoPv is not null ? Path.GetFileName(caminhoPv) : null;
        byte[]? conteudoPv = caminhoPv is not null ? await origem.LerAsync(caminhoPv, ct) : null;

        if (caminhoPv is null)
        {
            // TODO(a-confirmar): comportamento pra "V sem PV" listado como
            // ponto em aberto no documento de tarefa. Tratamento atual:
            // segue o fluxo só com os dados do V.
            logger.LogInformation(
                "Arquivo V {Nome} sem PV correspondente — seguindo só com os dados do V",
                pendente.Nome);
        }

        // Correlação das chamadas SÍNCRONAS — descartável de propósito: a
        // linha em Cobranca.Arquivo (cujo ID vira a correlação do envio
        // assíncrono) só é criada depois que V e PV converteram com
        // sucesso, pra não deixar registro órfão de arquivo que nunca foi
        // enviado.
        var idCorrelacaoSync = Guid.NewGuid().ToString();

        try
        {
            var syncV = await conversor.ConverterCnabParaJsonAsync(conteudoV, pendente.Nome, idCorrelacaoSync, ct);
            var dadosPv = conteudoPv is not null
                ? (await conversor.ConverterCnabParaJsonAsync(conteudoPv, nomePv!, idCorrelacaoSync, ct)).Data
                : null;

            var dataD1 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            var (pendenciasConvertidas, chaves) =
                await pendenciasFactory.ObterPendenciasConvertidasAsync(cnpj, dataD1, ct);

            var dadosMesclados = mesclagem.Mesclar(syncV.Data, dadosPv, pendenciasConvertidas);

            // Sequencial do RETORNO — não o que veio no header do V (esse
            // é o da remessa, e vem errado se o arquivo for regerado).
            // Reservado só depois da mesclagem ter dado certo, pra não
            // consumir número à toa.
            var sequencial = await sequenciais.ReservarProximoAsync(cnpj, ct);
            var dadosFinais = mesclagem.AplicarSequencial(dadosMesclados, sequencial);

            var jsonSerializado = JsonSerializer.SerializeToUtf8Bytes(dadosFinais, JsonOpcoesSaida);

            // Registra o arquivo de retorno ANTES do envio assíncrono — o
            // ID gerado é o que vai no campo "id" da chamada e o que o
            // Robô 2 usa pra reencontrar o cliente na conclusão.
            var nomeArquivoRetorno = MontarNomeArquivoRetorno(cnpj);
            var arquivoId = await arquivos.RegistrarEnvioParaConversaoAsync(
                nomeArquivoRetorno, cnpj, dadosFinais, ct);

            ConvertAsyncUploadIniciado resultadoConversao;
            try
            {
                resultadoConversao = await conversor.ConverterJsonParaCnabAsync(
                    jsonSerializado, $"{nomeArquivoRetorno}.json", arquivoId.ToString(), ct);
            }
            catch
            {
                // Compensação: sem o envio, a linha não deve existir.
                await arquivos.RemoverAsync(arquivoId, ct);
                throw;
            }

            // Ordem importa: registra o MD5 antes de marcar as pendências
            // como reportadas. Se o processo morrer entre as duas, o pior
            // caso é o MD5 ficar marcado sem as pendências marcadas (só
            // reabre risco se outra V do mesmo CNPJ aparecer depois) — a
            // ordem inversa arriscaria perder a pendência de vez.
            controleIdempotencia.RegistrarProcessado(md5V);
            controlePendencias.RegistrarReportadas(chaves);
            await origem.MoverParaBackupAsync(pendente.Caminho, ct);
            if (caminhoPv is not null) await origem.MoverParaBackupAsync(caminhoPv, ct);

            logger.LogInformation(
                "Arquivo {Nome} processado — ClientId {ClientId}, Cnpj {Cnpj}, Sequencial {Sequencial}, " +
                "ArquivoID {ArquivoID}, JobId {JobId}",
                pendente.Nome, pendente.ClientId, cnpj, sequencial, arquivoId, resultadoConversao.JobId);

            return new ArquivoProcessado(ResultadoArquivo.Processado, cnpj);
        }
        catch (Exception ex) when (ex is ConversaoCnabFalhouException
            or DadosConvertidosDivergentesException
            or SequencialIndisponivelException)
        {
            logger.LogError(ex,
                "Falha ao processar ClientId {ClientId} (Cnpj {Cnpj})", pendente.ClientId, cnpj);
            return new ArquivoProcessado(ResultadoArquivo.Falha);
        }
    }

    /// <summary>TODO(a-confirmar): nomenclatura do arquivo de retorno ainda
    /// não foi definida — é sempre um por dia, por cliente, então o par
    /// documento+data identifica de forma única enquanto o padrão real não
    /// chega.</summary>
    internal static string MontarNomeArquivoRetorno(string documento)
        => $"RETORNO-{documento}-{DateTime.UtcNow:yyyyMMdd}";

    private static readonly JsonSerializerOptions JsonOpcoesSaida = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
