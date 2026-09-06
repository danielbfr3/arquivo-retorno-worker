using System.Text.Json;

namespace CnabRetorno.Core.Dominio;

/// <summary>
/// Projeção de leitura de <c>Cobranca.DocumentoDados</c>, na base
/// CASH_COBRANCA — tabela nova, dona de outro sistema (só escreve quem a
/// popula; este worker só lê por <see cref="NumeroDocumento"/>).
///
/// <see cref="Dados"/> é uma string JSON com um objeto plano — cada chave é
/// o cabeçalho de uma coluna da planilha e o valor é o que deve ser escrito
/// naquela coluna, em todas as linhas de dados do arquivo daquele
/// documento. Ex.: <c>{"Nome Cliente": "ACME LTDA", "Valor": "1500.00",
/// "Razão Social": "ACME DISTRIBUIDORA LTDA"}</c>. O formato mora com o
/// tipo (mesma ideia de <c>MetadadosCliente.Serializar()</c> em
/// <c>Aplicacao/Dtos/</c>): um lugar só pra mudar se o contrato mudar.
///
/// A chave <see cref="ChaveRazaoSocial"/> é reservada: além de preencher a
/// coluna homônima na planilha (como qualquer outra chave), é a fonte da
/// razão social usada nos metadados enviados ao conversor — ver
/// <c>ProcessadorArquivoExcelService</c>. Não existe mais uma base de
/// adesão separada para isso.
///
/// TODO(a-confirmar): esta tabela nunca foi inspecionada num ambiente real.
/// O formato exato de <see cref="NumeroDocumento"/> (14 dígitos sem
/// pontuação, igual a <c>Arquivo.ClienteDocumento</c>, ou outro formato) e
/// a garantia de uma linha só por documento são suposições — corrigir aqui
/// e no mapeamento de <c>CobrancaDbContext.OnModelCreating</c> assim que o
/// time dono confirmar.
/// </summary>
public class DocumentoDados
{
    /// <summary>Chave reservada em <see cref="Dados"/> pra razão social do
    /// cliente — comparação tolerante a caixa/espaço, igual ao casamento
    /// de cabeçalho da planilha (<c>PreenchimentoOptions</c>).</summary>
    public const string ChaveRazaoSocial = "Razão Social";

    public required string NumeroDocumento { get; init; }

    public required string Dados { get; init; }

    /// <summary>Desserializa <see cref="Dados"/> como um objeto plano
    /// chave/valor — cada chave é o cabeçalho de uma coluna da planilha.
    ///
    /// Devolve <c>null</c> quando o JSON é inválido, não é um objeto (é
    /// array, número, etc.) ou é um objeto vazio (<c>{}</c>) — os três
    /// casos significam "nada pra preencher" e são tratados por quem
    /// chama do mesmo jeito que "documento sem linha na tabela".</summary>
    public IReadOnlyDictionary<string, string>? DesserializarDados()
    {
        Dictionary<string, string>? valores;
        try
        {
            valores = JsonSerializer.Deserialize<Dictionary<string, string>>(Dados);
        }
        catch (JsonException)
        {
            return null;
        }

        return valores is null or { Count: 0 } ? null : valores;
    }

    /// <summary>Procura <see cref="ChaveRazaoSocial"/> em <paramref name="valores"/>
    /// (já desserializado por <see cref="DesserializarDados"/>), tolerante a
    /// caixa/espaço — mesma tolerância do casamento de cabeçalho da
    /// planilha, mas independente dele: aqui é comparação de chave JSON,
    /// não de cabeçalho de coluna.
    ///
    /// Devolve <c>null</c> quando a chave não existe ou o valor é
    /// vazio/espaços — os dois casos tratados como "sem razão social" por
    /// quem chama.</summary>
    public static string? ObterRazaoSocial(IReadOnlyDictionary<string, string> valores)
    {
        foreach (var (chave, valor) in valores)
        {
            if (string.Equals(chave.Trim(), ChaveRazaoSocial, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
        }

        return null;
    }
}
