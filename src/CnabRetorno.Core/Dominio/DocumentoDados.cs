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
/// documento. Ex.: <c>{"Nome Cliente": "ACME LTDA", "Valor": "1500.00"}</c>.
/// O formato mora com o tipo (mesma ideia de <c>MetadadosCliente.Serializar()</c>
/// em <c>Aplicacao/Dtos/</c>): um lugar só pra mudar se o contrato mudar.
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
}
