using System.Collections.Concurrent;
using CnabRetorno.RetornoCron.Worker.Origem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CnabRetorno.RetornoCron.Worker.Pipeline;

/// <summary>
/// Orquestrador do Robô 1: lista os arquivos V pendentes (passo 1) e
/// decide quantos processar em paralelo — a lógica de cada arquivo fica
/// isolada em <see cref="ProcessadorArquivoRetornoService"/>, resolvido num
/// escopo de DI próprio por arquivo.
/// </summary>
public class ProcessarArquivosVePvPipeline(
    PastaOrigemArquivosRetorno origem,
    IServiceScopeFactory scopeFactory,
    IOptions<PipelineOptions> opcoes,
    ILogger<ProcessarArquivosVePvPipeline> logger)
{
    private readonly PipelineOptions _opt = opcoes.Value;

    public async Task<ResumoExecucao> ExecutarAsync(CancellationToken ct)
    {
        var pendentes = await origem.ListarArquivosVAsync(ct);
        logger.LogInformation("Encontrados {Qtd} arquivo(s) V pendente(s)", pendentes.Count);

        var resumo = new ResumoExecucao();

        var opcoesParalelo = new ParallelOptions
        {
            MaxDegreeOfParallelism = _opt.MaxArquivosConcorrentes,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(pendentes, opcoesParalelo, async (pendente, tokenItem) =>
        {
            try
            {
                using var escopo = scopeFactory.CreateScope();
                var processador = escopo.ServiceProvider
                    .GetRequiredService<ProcessadorArquivoRetornoService>();

                var resultado = await processador.ProcessarAsync(pendente, tokenItem);
                resumo.Registrar(resultado);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao processar {Arquivo}", pendente.Caminho);
                resumo.Registrar(new ArquivoProcessado(ResultadoArquivo.Falha));
            }
        });

        logger.LogInformation(
            "Execução concluída: {Proc} processado(s), {Dup} duplicado(s), {Falha} falha(s)",
            resumo.Processados, resumo.Duplicados, resumo.Falhas);

        return resumo;
    }
}

/// <summary>Contadores agregados — atualizados a partir de várias tarefas em
/// paralelo, por isso <see cref="Interlocked"/> em vez de int comum. Também
/// acumula os CNPJs resolvidos, usados por
/// <see cref="ProcessadorClientesSemArquivoService"/> pra saber quem já foi
/// coberto neste lote.</summary>
public class ResumoExecucao
{
    private int _processados;
    private int _duplicados;
    private int _falhas;
    private readonly ConcurrentBag<string> _cnpjsProcessados = [];

    public int Processados => _processados;
    public int Duplicados => _duplicados;
    public int Falhas => _falhas;
    public IReadOnlyCollection<string> CnpjsProcessados => _cnpjsProcessados;

    public void Registrar(ArquivoProcessado resultado)
    {
        switch (resultado.Resultado)
        {
            case ResultadoArquivo.Processado:
                Interlocked.Increment(ref _processados);
                if (resultado.Cnpj is not null) _cnpjsProcessados.Add(resultado.Cnpj);
                break;
            case ResultadoArquivo.Duplicado: Interlocked.Increment(ref _duplicados); break;
            case ResultadoArquivo.Falha: Interlocked.Increment(ref _falhas); break;
        }
    }
}
