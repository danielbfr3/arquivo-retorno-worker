using CnabRetorno.Core.Dominio;
using Microsoft.Extensions.Options;

namespace CnabRetorno.RetornoCron.Worker.Origem;

public class OrigemOptions
{
    public const string Secao = "Origem";

    /// <summary>Pasta X do documento de tarefa — onde o banco deposita os
    /// arquivos V e PV.</summary>
    public string Pasta { get; set; } = default!;

    /// <summary>Subpasta (dentro de <see cref="Pasta"/>) pra onde os
    /// arquivos processados são movidos — passo 14.</summary>
    public string PastaBackup { get; set; } = "Backup";
}

public sealed record ArquivoVPendente(string Caminho, string Nome, string ClientId);

/// <summary>
/// Varre a pasta X por arquivos V, localiza o PV correspondente (mesmo
/// ClientId) e move pra Backup depois de processado. Equivalente ao
/// PastaLocalOrigem do pipeline anterior, mas orientado ao padrão de nome
/// V/PV em vez de estrutura de pastas por cliente (ver
/// docs/segunda-fonte-de-dados-sql-server.md sobre esse tipo de decisão).
/// </summary>
public class PastaOrigemArquivosRetorno(IOptions<OrigemOptions> opcoes)
{
    private readonly OrigemOptions _opt = opcoes.Value;

    /// <summary>Passo 1: lista todos os arquivos V pendentes na pasta X.</summary>
    public Task<IReadOnlyList<ArquivoVPendente>> ListarArquivosVAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_opt.Pasta))
            return Task.FromResult<IReadOnlyList<ArquivoVPendente>>([]);

        var pendentes = Directory
            .EnumerateFiles(_opt.Pasta, "*", SearchOption.TopDirectoryOnly)
            .Select(caminho => (caminho, nome: Path.GetFileName(caminho)))
            .Where(a => NomeArquivoRetorno.TentarExtrairClientId(a.nome, out _, out var tipo)
                        && tipo == TipoArquivoRetorno.V)
            .Select(a =>
            {
                NomeArquivoRetorno.TentarExtrairClientId(a.nome, out var clientId, out _);
                return new ArquivoVPendente(a.caminho, a.nome, clientId);
            })
            .OrderBy(a => a.Caminho, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult<IReadOnlyList<ArquivoVPendente>>(pendentes);
    }

    /// <summary>Passo 5: procura, na mesma pasta, um arquivo PV cujo
    /// ClientId bata com o do arquivo V — retorna null se não existir
    /// (comportamento pra V sem PV: TODO(a-confirmar) no documento original,
    /// tratado aqui como "segue só com o V", sem erro).</summary>
    public string? LocalizarPvCorrespondente(string clientId)
    {
        if (!Directory.Exists(_opt.Pasta)) return null;

        return Directory
            .EnumerateFiles(_opt.Pasta, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(caminho =>
                NomeArquivoRetorno.CorrespondeAoMesmoCliente(Path.GetFileName(caminho), clientId));
    }

    public Task<byte[]> LerAsync(string caminho, CancellationToken ct)
        => File.ReadAllBytesAsync(caminho, ct);

    /// <summary>Passo 14: move o(s) arquivo(s) processado(s) pra Backup.</summary>
    public Task MoverParaBackupAsync(string caminho, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.Combine(_opt.Pasta, _opt.PastaBackup));
        var destino = Path.Combine(_opt.Pasta, _opt.PastaBackup, Path.GetFileName(caminho));
        File.Move(caminho, destino, overwrite: true);
        return Task.CompletedTask;
    }
}
