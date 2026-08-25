using Microsoft.Extensions.Options;

namespace CnabRetorno.ExcelCnab.Worker.Origem;

public class OrigemOptions
{
    public const string Secao = "Origem";

    /// <summary>Pasta onde as planilhas são depositadas. Localmente é um
    /// diretório comum; em homologação e produção é um compartilhamento
    /// SMB montado no pod — por isso é configuração, não caminho fixo, e
    /// por isso o código não assume nada além de "um diretório".</summary>
    public string Pasta { get; set; } = default!;

    /// <summary>Subpasta pra onde vai a planilha já enviada ao
    /// conversor.</summary>
    public string PastaBackup { get; set; } = "Backup";

    /// <summary>Subpasta pra onde vai o arquivo que não casou com a
    /// máscara, cujo cliente não está na base de adesão, ou cujo envio
    /// falhou. Nunca é apagado: descartar significaria perder a planilha
    /// de um cliente em silêncio.</summary>
    public string PastaQuarentena { get; set; } = "Quarentena";

    /// <summary>Ignora arquivos modificados há menos de X segundos, pra
    /// não ler uma planilha que ainda está sendo copiada pra pasta. Meio
    /// arquivo enviado ao conversor viraria um CNAB truncado — e num
    /// compartilhamento SMB a cópia de um .xlsx grande leva segundos.</summary>
    public int SegundosEstabilidade { get; set; } = 30;
}

public sealed record ArquivoPendente(string Caminho, string Nome);

/// <summary>
/// Varre a pasta de entrada e move o arquivo pra Backup ou Quarentena
/// depois de tratado. Só olha o nível de cima: as subpastas de
/// backup/quarentena vivem dentro da própria pasta de origem, e uma
/// varredura recursiva reprocessaria o que já foi tratado.
/// </summary>
public class PastaOrigemExcel(IOptions<OrigemOptions> opcoes, TimeProvider tempo)
{
    private readonly OrigemOptions _opt = opcoes.Value;

    public IReadOnlyList<ArquivoPendente> ListarPendentes()
    {
        if (!Directory.Exists(_opt.Pasta)) return [];

        var corte = tempo.GetUtcNow().UtcDateTime.AddSeconds(-_opt.SegundosEstabilidade);

        return
        [
            .. Directory
                .EnumerateFiles(_opt.Pasta, "*", SearchOption.TopDirectoryOnly)
                .Select(caminho => new ArquivoPendente(caminho, Path.GetFileName(caminho)))
                // Arquivo de trava do Excel: enquanto alguém tem a
                // planilha aberta num compartilhamento, o Excel deixa um
                // "~$Nome.xlsx" do lado. Não é planilha de ninguém — se
                // não for pulado, entope a quarentena todo ciclo.
                .Where(a => !a.Nome.StartsWith("~$", StringComparison.Ordinal))
                .Where(a => File.GetLastWriteTimeUtc(a.Caminho) <= corte)
                .OrderBy(a => a.Caminho, StringComparer.Ordinal)
        ];
    }

    public Task<byte[]> LerAsync(string caminho, CancellationToken ct)
        => File.ReadAllBytesAsync(caminho, ct);

    public void MoverParaBackup(string caminho) => Mover(caminho, _opt.PastaBackup);

    public void MoverParaQuarentena(string caminho) => Mover(caminho, _opt.PastaQuarentena);

    private void Mover(string caminho, string subpasta)
    {
        var destinoPasta = Path.Combine(_opt.Pasta, subpasta);
        Directory.CreateDirectory(destinoPasta);

        // Nunca sobrescreve: o mesmo cliente manda "Simplificado_<cnpj>"
        // toda semana, sempre com o mesmo nome — sobrescrever apagaria a
        // planilha da semana passada em silêncio, e na quarentena apagaria
        // justamente a evidência do problema. Homônimo ganha sufixo de
        // timestamp.
        var destino = Path.Combine(destinoPasta, Path.GetFileName(caminho));
        if (File.Exists(destino))
        {
            var nome = Path.GetFileNameWithoutExtension(caminho);
            var extensao = Path.GetExtension(caminho);
            destino = Path.Combine(
                destinoPasta,
                $"{nome}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{extensao}");
        }

        File.Move(caminho, destino, overwrite: false);
    }
}
