namespace CnabRetorno.Core.Dominio;

/// <summary>
/// Status do arquivo (smallint no banco), em <c>Cobranca.Arquivo</c> e
/// <c>Pagamento.Arquivo</c>.
///
/// TODO(a-confirmar): os **nomes** vêm da entidade real da cash-cobranca-api
/// (extração de 24/07/2026), mas os **valores numéricos** não foram
/// fornecidos — os de baixo são suposição. Gravar o número errado numa
/// tabela compartilhada por todo o ecossistema CASH corrompe o
/// rastreamento de arquivo dos outros sistemas, não só destes workers (ver
/// docs/riscos-conhecidos.md).
/// </summary>
public enum ArquivoStatus : short
{
    AguardandoProcessamento = 1,
    EmProcessamento = 2,
    Processado = 3,
}

/// <summary>Etapa do arquivo (smallint no banco). Mesma ressalva de <see
/// cref="ArquivoStatus"/> sobre os valores numéricos serem suposição.</summary>
public enum ArquivoEtapa : short
{
    GeradoUrlBucket = 1,
    ArquivoConferido = 2,
    EnviadoParaConversao = 3,
    ArquivoConvertido = 4,
    ArquivoInvalido = 5,
    Registrando = 6,
    Registrado = 7,
}

/// <summary>
/// Projeção de escrita/leitura da tabela de arquivos. A mesma classe serve
/// às duas bases porque as duas tabelas têm a mesma forma:
///
/// <list type="bullet">
///   <item><c>Cobranca.Arquivo</c> na base CASH_COBRANCA (schema real em
///   docs/cash-cobranca-referencia.md §1.1) — escrita pelo Robô 1 ao
///   ingerir uma remessa de VAN.</item>
///   <item><c>Pagamento.Arquivo</c> na base ASA_CASH_PAGAMENTO — escrita
///   pelo Robô 2 ao gerar um arquivo de retorno de pagamentos.
///   TODO(a-confirmar): o schema desta segunda tabela **não** foi
///   capturado na extração de 03/08/2026 (a lista de tabelas a mostra,
///   mas as colunas não foram fotografadas). Está mapeada aqui como
///   espelho da de cobrança; conferir antes de subir pra homologação.</item>
/// </list>
///
/// A entidade **rica** (com a máquina de estados
/// <c>EtapasPermitidasPorStatus</c> e os métodos que impõem transição
/// válida) mora na API dona da tabela. Aqui é de propósito só uma projeção
/// mínima dos campos que estes workers usam — não replicamos as
/// invariantes pra não ter duas fontes de verdade divergindo com o tempo.
///
/// Diferente das demais projeções deste projeto, esta entidade **tem
/// chave** (<see cref="ArquivoID"/>) — é a única que é escrita, não só
/// lida. O <see cref="ArquivoID"/> é também o identificador do objeto no
/// Gestor de Arquivos: um id só em toda a cadeia (registro, storage,
/// conversão), nunca um GUID novo por chamada.
/// </summary>
public class Arquivo
{
    public required Guid ArquivoID { get; init; }
    public required string AppID { get; init; }

    public string? ArquivoNome { get; init; }
    public string? ClienteContaHeader { get; init; }
    public short? ClienteTipoDocumento { get; init; } // 1-CPF, 2-CNPJ
    public string? ClienteDocumento { get; init; }
    public string? CriadoPor { get; init; }
    public string? DescricaoProduto { get; init; }
    public DateTime? DataCriacao { get; init; }

    public DateTime? DataAtualizacao { get; set; }
    public short ArquivoStatus { get; set; }
    public short ArquivoEtapa { get; set; }
}
