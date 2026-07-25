namespace CnabRetorno.Core.Dominio;

/// <summary>Tipo de arquivo pelo padrão de nome usado pelo banco.</summary>
public enum TipoArquivoRetorno
{
    /// <summary>Arquivo V — ex: "V1234567890001.txt".</summary>
    V,

    /// <summary>Arquivo PV (complementar) — ex: "PV1234567890301_002.txt".</summary>
    PV,
}

/// <summary>
/// Extrai o ClientId a partir do padrão de nome dos arquivos de retorno.
/// Compartilhado entre os dois robôs: o Robô 1 usa pra identificar V/PV na
/// pasta de origem (passos 1 e 5), o Robô 2 usa pra identificar o cliente a
/// partir do nome do arquivo final gerado (passo 5), quando os metadados da
/// mensagem não trouxerem o ClientId diretamente.
/// </summary>
public static class NomeArquivoRetorno
{
    /// <summary>
    /// Tamanho do ClientId, inferido dos exemplos do documento de tarefa:
    /// "V1234567890001.txt" → ClientId "1234567890" (10 dígitos logo após
    /// o prefixo V/PV). Ajustar se o padrão real divergir.
    /// </summary>
    private const int TamanhoClientId = 10;

    /// <summary>
    /// Tenta extrair o ClientId e o tipo (V/PV) a partir do nome do arquivo.
    /// Retorna false se o nome não seguir nenhum dos dois padrões.
    /// </summary>
    public static bool TentarExtrairClientId(
        string nomeArquivo, out string clientId, out TipoArquivoRetorno tipo)
    {
        var semExtensao = Path.GetFileNameWithoutExtension(nomeArquivo);

        // PV precisa ser checado antes de V — "PV..." também começa com um
        // prefixo que, se checado na ordem errada, colidiria com o padrão V.
        if (semExtensao.Length >= 2 + TamanhoClientId &&
            semExtensao.StartsWith("PV", StringComparison.OrdinalIgnoreCase))
        {
            clientId = semExtensao.Substring(2, TamanhoClientId);
            tipo = TipoArquivoRetorno.PV;
            return true;
        }

        if (semExtensao.Length >= 1 + TamanhoClientId &&
            semExtensao.StartsWith("V", StringComparison.OrdinalIgnoreCase))
        {
            clientId = semExtensao.Substring(1, TamanhoClientId);
            tipo = TipoArquivoRetorno.V;
            return true;
        }

        clientId = string.Empty;
        tipo = default;
        return false;
    }

    /// <summary>
    /// Verifica se um arquivo PV corresponde ao mesmo ClientId de um
    /// arquivo V já identificado (passo 5 do Robô 1).
    /// </summary>
    public static bool CorrespondeAoMesmoCliente(string nomeArquivoPv, string clientIdEsperado)
        => TentarExtrairClientId(nomeArquivoPv, out var clientId, out var tipo)
           && tipo == TipoArquivoRetorno.PV
           && clientId.Equals(clientIdEsperado, StringComparison.OrdinalIgnoreCase);
}
