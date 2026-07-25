using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CnabRetorno.RetornoCron.Worker.Origem;

/// <summary>
/// Controle de idempotência sem banco: guarda os MD5 já processados
/// **hoje** num arquivo de controle dentro da pasta de origem, resetado
/// automaticamente a cada dia (se o arquivo for de uma data anterior, é
/// tratado como vazio). Substitui o índice único em banco que existia
/// antes da remoção da persistência do Robô 1 — suficiente porque o cron
/// roda uma vez por dia (madrugada) e o único cenário a evitar é
/// reprocessar o mesmo arquivo dentro da mesma execução/dia (ex.: reexecução
/// manual antes do arquivo ser movido pra Backup).
///
/// Estado em memória + persistido em arquivo a cada registro — thread-safe
/// via lock, já que arquivos são processados em paralelo (ver
/// PipelineOptions.MaxArquivosConcorrentes).
/// </summary>
public class ControleIdempotenciaDiario
{
    private readonly object _lock = new();
    private readonly string _caminhoArquivo;
    private Estado _estado;

    public ControleIdempotenciaDiario(IOptions<OrigemOptions> opcoes)
    {
        _caminhoArquivo = Path.Combine(opcoes.Value.Pasta, ".processados-hoje.json");
        _estado = CarregarOuNovo(_caminhoArquivo);
    }

    public bool JaProcessadoHoje(string md5)
    {
        lock (_lock) return _estado.Md5s.Contains(md5);
    }

    public void RegistrarProcessado(string md5)
    {
        lock (_lock)
        {
            if (!_estado.Data.Equals(DateOnly.FromDateTime(DateTime.UtcNow)))
                _estado = new Estado(DateOnly.FromDateTime(DateTime.UtcNow), []);

            _estado.Md5s.Add(md5);
            Salvar(_caminhoArquivo, _estado);
        }
    }

    private static Estado CarregarOuNovo(string caminho)
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        if (!File.Exists(caminho)) return new Estado(hoje, []);

        try
        {
            var estado = JsonSerializer.Deserialize<Estado>(File.ReadAllText(caminho));
            // Arquivo de um dia anterior — reseta (mesmo espírito do
            // "apagando no próximo dia" pedido).
            return estado is not null && estado.Data.Equals(hoje) ? estado : new Estado(hoje, []);
        }
        catch (JsonException)
        {
            return new Estado(hoje, []);
        }
    }

    /// <summary>Grava em arquivo temporário e só então move por cima do
    /// definitivo — reduz a chance de um .json corrompido por crash no
    /// meio da escrita.</summary>
    private static void Salvar(string caminho, Estado estado)
    {
        var tmp = caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(estado));
        File.Move(tmp, caminho, overwrite: true);
    }

    private sealed record Estado(DateOnly Data, HashSet<string> Md5s);
}
