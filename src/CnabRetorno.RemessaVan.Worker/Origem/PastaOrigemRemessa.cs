using Microsoft.Extensions.Options;

namespace CnabRetorno.RemessaVan.Worker.Origem;

public class OrigemOptions
{
    public const string Secao = "Origem";

    /// <summary>Pasta onde as VANs depositam os arquivos. Em produção é um
    /// compartilhamento SMB montado no pod — por isso é configuração, não
    /// caminho fixo.</summary>
    public string Pasta { get; set; } = default!;

    /// <summary>Subpasta pra onde vai o arquivo já processado com
    /// sucesso.</summary>
    public string PastaBackup { get; set; } = "Backup";

    /// <summary>Subpasta pra onde vai o arquivo que nenhuma máscara
    /// reconheceu, ou cujo processamento falhou. Nunca é apagado: sem
    /// saber de que cliente é, descartar significaria perder uma remessa
    /// silenciosamente.</summary>
    public string PastaQuarentena { get; set; } = "Quarentena";

    /// <summary>Subpasta pra onde vai o arquivo reconhecido que **não** é
    /// remessa (um retorno que a VAN devolveu na mesma pasta, por
    /// exemplo). Separado da quarentena de propósito: não é problema, é
    /// escopo de outro fluxo — e tirá-lo da pasta impede que cada ciclo o
    /// reavalie pra sempre.</summary>
    public string PastaIgnorados { get; set; } = "Ignorados";

    /// <summary>Ignora arquivos modificados há menos de X segundos, pra
    /// não ler um arquivo que a VAN ainda está gravando. Uma remessa lida
    /// pela metade seria registrada como válida.</summary>
    public int SegundosEstabilidade { get; set; } = 30;
}

public sealed record ArquivoPendente(string Caminho, string Nome);

/// <summary>
/// Varre a pasta de entrada das VANs e move o arquivo pra Backup ou
/// Quarentena depois de tratado. Só olha o nível de cima: as subpastas de
/// backup/quarentena vivem dentro da própria pasta de origem, e uma
/// varredura recursiva reprocessaria o que já foi tratado.
/// </summary>
public class PastaOrigemRemessa(IOptions<OrigemOptions> opcoes, TimeProvider tempo)
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
                .Where(caminho => File.GetLastWriteTimeUtc(caminho) <= corte)
                .Select(caminho => new ArquivoPendente(caminho, Path.GetFileName(caminho)))
                .OrderBy(a => a.Caminho, StringComparer.Ordinal)
        ];
    }

    public Task<byte[]> LerAsync(string caminho, CancellationToken ct)
        => File.ReadAllBytesAsync(caminho, ct);

    public void MoverParaBackup(string caminho) => Mover(caminho, _opt.PastaBackup);

    public void MoverParaQuarentena(string caminho) => Mover(caminho, _opt.PastaQuarentena);

    public void MoverParaIgnorados(string caminho) => Mover(caminho, _opt.PastaIgnorados);

    private void Mover(string caminho, string subpasta)
    {
        var destinoPasta = Path.Combine(_opt.Pasta, subpasta);
        Directory.CreateDirectory(destinoPasta);

        // Nunca sobrescreve: dois arquivos de mesmo nome em dias
        // diferentes são arquivos diferentes, e o segundo apagaria o
        // primeiro em silêncio — na quarentena, isso é perder justamente
        // a evidência do problema. Homônimo ganha sufixo de timestamp.
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
