using Microsoft.Extensions.Options;

namespace CnabRetorno.ExcelCnab.Worker.Armazenamento;

/// <summary>Uma cópia que não deu certo — o destino e o porquê.</summary>
public sealed record CopiaFalhou(string Destino, Exception Erro);

/// <summary>O que aconteceu com as cópias de um arquivo.</summary>
public sealed record ResultadoCopias(
    IReadOnlyList<ArquivoArmazenado> Armazenadas,
    IReadOnlyList<CopiaFalhou> Falhas)
{
    public bool TudoOk => Falhas.Count == 0;
}

/// <summary>Lançada quando uma cópia falha e
/// <c>Armazenamento:FalhaBloqueiaEnvio</c> está ligado.</summary>
public sealed class ArmazenamentoObrigatorioFalhouException(IReadOnlyList<CopiaFalhou> falhas)
    : Exception($"Falha ao armazenar cópia em: {string.Join(", ", falhas.Select(f => f.Destino))}.",
        falhas.Count == 1 ? falhas[0].Erro : new AggregateException(falhas.Select(f => f.Erro)));

/// <summary>
/// Guarda a planilha em **todos** os destinos habilitados. É o único ponto
/// que o resto do worker conhece do armazenamento — o processador chama
/// este serviço e não sabe quantos destinos existem nem quais são.
///
/// Regras que valem a pena não perder de vista:
///
/// <list type="bullet">
///   <item><b>Nenhum destino interrompe o outro.</b> Se o Gestor de
///   Arquivos cair, a cópia no S3 continua sendo tentada — o ponto de ter
///   dois destinos é justamente não depender de um.</item>
///   <item><b>Falha sai como erro no log</b>, mesmo sem bloquear o envio.
///   Com o padrão não-bloqueante, o log é a única coisa que impede uma
///   cópia faltando de passar despercebida.</item>
///   <item><b>Sem destino habilitado, é no-op.</b> Desligar
///   <c>Armazenamento:Habilitado</c> faz o DI não registrar nada, e este
///   serviço vira uma chamada vazia — o fluxo principal não muda de
///   forma.</item>
/// </list>
/// </summary>
public class ArmazenadorDeCopias(
    IEnumerable<IArmazenamentoArquivo> destinos,
    IOptions<ArmazenamentoOptions> opcoes,
    ILogger<ArmazenadorDeCopias> logger)
{
    private readonly ArmazenamentoOptions _opt = opcoes.Value;

    public async Task<ResultadoCopias> ArmazenarAsync(
        Guid arquivoId, string nomeArquivo, byte[] conteudo, CancellationToken ct)
    {
        var armazenadas = new List<ArquivoArmazenado>();
        var falhas = new List<CopiaFalhou>();

        foreach (var destino in destinos)
        {
            // O nome do tipo é o que identifica o destino no log quando a
            // chamada falha antes de devolver um ArquivoArmazenado.
            var nomeDestino = destino.GetType().Name;
            try
            {
                var armazenado = await destino.ArmazenarAsync(arquivoId, nomeArquivo, conteudo, ct);
                armazenadas.Add(armazenado);

                logger.LogInformation(
                    "Cópia de {Nome} (ArquivoID {ArquivoId}) gravada em {Destino}: {Referencia}",
                    nomeArquivo, arquivoId, armazenado.Destino, armazenado.Referencia);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                falhas.Add(new CopiaFalhou(nomeDestino, ex));

                logger.LogError(ex,
                    "Falha ao gravar cópia de {Nome} (ArquivoID {ArquivoId}) em {Destino}",
                    nomeArquivo, arquivoId, nomeDestino);
            }
        }

        if (falhas.Count > 0 && _opt.FalhaBloqueiaEnvio)
            throw new ArmazenamentoObrigatorioFalhouException(falhas);

        return new ResultadoCopias(armazenadas, falhas);
    }
}
