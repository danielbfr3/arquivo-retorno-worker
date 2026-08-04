using CnabRetorno.Core.Aplicacao;
using CnabRetorno.RemessaVan.Worker.Origem;
using CnabRetorno.RemessaVan.Worker.Persistencia;
using CnabRetorno.RemessaVan.Worker.Vans;

namespace CnabRetorno.RemessaVan.Worker.Pipeline;

public enum ResultadoRemessa
{
    Ingerido,
    Duplicado,
    NaoReconhecido,
    IgnoradoNaoEhRemessa,
    Falhou,
}

public sealed record RemessaProcessada(ResultadoRemessa Resultado, Guid? ArquivoId = null, string? Nome = null);

/// <summary>
/// Ingestão de um arquivo de remessa de VAN — os passos 0 a 9 do checklist
/// de 03/08/2026, na ordem em que ele os descreve.
///
/// Não há conversão aqui. O robô renomeia, guarda e registra; transformar
/// o CNAB em JSON é responsabilidade de outro worker do ecossistema, que
/// parte do registro em <c>Cobranca.Arquivo</c>.
/// </summary>
public class ProcessadorArquivoRemessaService(
    PastaOrigemRemessa origem,
    CatalogoMascarasVan mascaras,
    NomeArquivoAsa nomeAsa,
    ParametroClienteRepository parametros,
    IngestaoIdempotenciaRepository idempotencia,
    IArmazenamentoArquivo armazenamento,
    ArquivoRepository arquivos,
    TimeProvider tempo,
    ILogger<ProcessadorArquivoRemessaService> logger)
{
    public async Task<RemessaProcessada> ProcessarAsync(ArquivoPendente pendente, CancellationToken ct)
    {
        // Passo 2: identificar a VAN pela máscara do nome.
        if (!mascaras.TentarReconhecer(pendente.Nome, out var reconhecido))
        {
            logger.LogWarning(
                "Arquivo {Nome} não casou com nenhuma máscara de VAN — movendo pra quarentena", pendente.Nome);
            origem.MoverParaQuarentena(pendente.Caminho);
            return new RemessaProcessada(ResultadoRemessa.NaoReconhecido);
        }

        if (reconhecido.Tipo != TipoArquivoVan.Remessa)
        {
            logger.LogInformation(
                "Arquivo {Nome} é {Tipo} da VAN {Van} — fora do escopo deste robô", pendente.Nome, reconhecido.Tipo, reconhecido.Van);
            origem.MoverParaIgnorados(pendente.Caminho);
            return new RemessaProcessada(ResultadoRemessa.IgnoradoNaoEhRemessa);
        }

        var conteudo = await origem.LerAsync(pendente.Caminho, ct);

        // Idempotência por conteúdo, antes de qualquer efeito: a VAN pode
        // retransmitir o mesmo arquivo (com nome novo, inclusive) dias
        // depois. O nome não serve de chave — o hash serve.
        var md5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(conteudo));
        if (await idempotencia.JaIngeridaAsync(md5, ct))
        {
            logger.LogWarning(
                "Arquivo {Nome} (MD5 {Md5}) já foi ingerido antes — movendo pra Backup sem reprocessar",
                pendente.Nome, md5);
            origem.MoverParaBackup(pendente.Caminho);
            return new RemessaProcessada(ResultadoRemessa.Duplicado);
        }

        // Passo 1: o GUID nasce aqui e vale pro storage e pro registro —
        // um id só na cadeia inteira.
        var arquivoId = Guid.NewGuid();

        // Passo 4: conta do cliente a partir do documento (passo 3, que a
        // máscara já resolveu ao capturar o CNPJ do nome).
        var contaHeader = await parametros.ObterContaHeaderAsync(reconhecido.Cnpj, ct);
        if (contaHeader is null)
            logger.LogWarning(
                "Cliente {Documento} sem linha em Cobranca.Parametro — arquivo {Nome} será registrado sem ContaHeader",
                reconhecido.Cnpj, pendente.Nome);

        // Passo 5: nome no padrão ASA.
        var nomeFinal = nomeAsa.Renderizar(new DadosNomeArquivo(
            Documento: reconhecido.Cnpj,
            ContaHeader: contaHeader,
            Van: reconhecido.Van,
            ArquivoId: arquivoId,
            NomeOriginal: pendente.Nome,
            Momento: tempo.GetLocalNow().DateTime));

        // Passos 6 e 7: presign + PUT (ou PutObject direto, conforme
        // Storage:Modo).
        var armazenado = await armazenamento.ArmazenarAsync(arquivoId, nomeFinal, conteudo, ct);

        // Passos 8 e 9: registro na tabela de arquivos.
        try
        {
            await arquivos.RegistrarRemessaAsync(arquivoId, nomeFinal, reconhecido.Cnpj, contaHeader, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // O objeto já está no storage e a linha não foi criada. Mandar
            // pra Backup faria o arquivo sumir da vista com o registro
            // faltando; deixar na origem faria o próximo ciclo gravar um
            // segundo objeto com GUID novo. Quarentena é o único destino
            // que preserva as duas coisas — e o log carrega a referência
            // do objeto órfão pra que dê pra limpá-lo.
            logger.LogError(ex,
                "Falha ao registrar {Nome} (ArquivoID {ArquivoId}) — objeto {Referencia} ficou órfão em {Destino}; arquivo movido pra quarentena",
                nomeFinal, arquivoId, armazenado.Referencia, armazenado.Destino);
            origem.MoverParaQuarentena(pendente.Caminho);
            return new RemessaProcessada(ResultadoRemessa.Falhou, arquivoId, nomeFinal);
        }

        // Hash gravado por último, depois da ingestão completa: um crash
        // antes daqui reprocessa (recuperável e visível); a ordem inversa
        // marcaria como ingerido um arquivo que não foi (perda
        // silenciosa).
        await idempotencia.RegistrarAsync(md5, arquivoId, pendente.Nome, ct);

        origem.MoverParaBackup(pendente.Caminho);

        logger.LogInformation(
            "Remessa {NomeOriginal} da VAN {Van} ingerida como {Nome} (ArquivoID {ArquivoId}, cliente {Documento}) em {Destino}",
            pendente.Nome, reconhecido.Van, nomeFinal, arquivoId, reconhecido.Cnpj, armazenado.Destino);

        return new RemessaProcessada(ResultadoRemessa.Ingerido, arquivoId, nomeFinal);
    }
}
