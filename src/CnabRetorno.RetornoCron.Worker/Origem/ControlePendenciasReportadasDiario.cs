using System.Collections.Concurrent;
using System.Text.Json;
using CnabRetorno.RetornoCron.Worker.Persistencia;
using Microsoft.Extensions.Options;

namespace CnabRetorno.RetornoCron.Worker.Origem;

/// <summary>
/// Controle de "esta pendência (título/instrução negado ou com erro) já
/// virou uma linha T/U num arquivo enviado hoje" — sem esse controle, dois
/// arquivos V do mesmo cliente no mesmo dia (dois lotes intraday,
/// reprocessamento manual antes do backup) geram a mesma linha duas vezes,
/// duplicando a informação reportada ao cliente (ver
/// docs/riscos-conhecidos.md, item 1).
///
/// Mesmo padrão de <see cref="ControleIdempotenciaDiario"/>: estado em
/// arquivo próprio na pasta de origem, resetado diariamente, sem depender
/// de banco (decisão arquitetural do Robô 1). Também expõe um lock
/// assíncrono por CNPJ — necessário porque o filtro (consultar pendências
/// não reportadas) e o registro (marcar como reportada) não são atômicos
/// entre si; sem esse lock, duas V do mesmo CNPJ processadas em paralelo
/// podem consultar antes de qualquer uma registrar, e as duas incluírem a
/// mesma pendência.
/// </summary>
public class ControlePendenciasReportadasDiario
{
    private readonly object _lock = new();
    private readonly string _caminhoArquivo;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locksPorCnpj = new();
    private Estado _estado;

    public ControlePendenciasReportadasDiario(IOptions<OrigemOptions> opcoes)
    {
        _caminhoArquivo = Path.Combine(opcoes.Value.Pasta, ".pendencias-reportadas-hoje.json");
        _estado = CarregarOuNovo(_caminhoArquivo);
    }

    public static string ChaveTitulo(Guid tituloId) => $"T:{tituloId:N}";
    public static string ChaveInstrucao(Guid instrucaoId) => $"I:{instrucaoId:N}";

    public bool JaReportada(string chave)
    {
        lock (_lock) return _estado.Chaves.Contains(chave);
    }

    public IReadOnlyList<TituloPendente> FiltrarNaoReportados(IReadOnlyList<TituloPendente> titulos)
    {
        lock (_lock)
            return titulos.Where(t => !_estado.Chaves.Contains(ChaveTitulo(t.Titulo.TituloID))).ToList();
    }

    public IReadOnlyList<InstrucaoPendente> FiltrarNaoReportados(IReadOnlyList<InstrucaoPendente> instrucoes)
    {
        lock (_lock)
            return instrucoes.Where(i => !_estado.Chaves.Contains(ChaveInstrucao(i.Instrucao.InstrucaoID))).ToList();
    }

    public void RegistrarReportadas(IEnumerable<string> chaves)
    {
        lock (_lock)
        {
            if (!_estado.Data.Equals(DateOnly.FromDateTime(DateTime.UtcNow)))
                _estado = new Estado(DateOnly.FromDateTime(DateTime.UtcNow), []);

            foreach (var chave in chaves) _estado.Chaves.Add(chave);
            Salvar(_caminhoArquivo, _estado);
        }
    }

    /// <summary>
    /// Precisa ficar seguro desde a consulta de pendências até
    /// <see cref="RegistrarReportadas"/>, atravessando as chamadas de
    /// conversão — se for liberado antes disso (ex.: logo após gerar as
    /// linhas T/U), a segunda V do mesmo CNPJ pode consultar e ainda ver a
    /// pendência como "não reportada", porque a primeira só registra
    /// depois que sua própria conversão termina. CNPJs diferentes nunca
    /// disputam o mesmo semáforo — só serializa arquivos do mesmo cliente.
    /// </summary>
    public async Task<IAsyncDisposable> AdquirirLockCnpjAsync(string cnpj, CancellationToken ct)
    {
        var semaforo = _locksPorCnpj.GetOrAdd(cnpj, _ => new SemaphoreSlim(1, 1));
        await semaforo.WaitAsync(ct);
        return new Liberador(semaforo);
    }

    private sealed class Liberador(SemaphoreSlim semaforo) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaforo.Release();
            return ValueTask.CompletedTask;
        }
    }

    private static Estado CarregarOuNovo(string caminho)
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        if (!File.Exists(caminho)) return new Estado(hoje, []);

        try
        {
            var estado = JsonSerializer.Deserialize<Estado>(File.ReadAllText(caminho));
            return estado is not null && estado.Data.Equals(hoje) ? estado : new Estado(hoje, []);
        }
        catch (JsonException)
        {
            return new Estado(hoje, []);
        }
    }

    /// <summary>Grava em arquivo temporário e só então move por cima do
    /// definitivo — reduz a chance de um .json corrompido por crash no
    /// meio da escrita (mesmo hardening aplicado em
    /// <see cref="ControleIdempotenciaDiario"/>).</summary>
    private static void Salvar(string caminho, Estado estado)
    {
        var tmp = caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(estado));
        File.Move(tmp, caminho, overwrite: true);
    }

    private sealed record Estado(DateOnly Data, HashSet<string> Chaves);
}
