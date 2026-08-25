namespace CnabRetorno.Core.Dominio;

/// <summary>
/// Status do arquivo (smallint no banco), em <c>Cobranca.Arquivo</c>.
///
/// TODO(a-confirmar): os **nomes** vêm da entidade real da cash-cobranca-api
/// (extração de 24/07/2026), mas os **valores numéricos** não foram
/// fornecidos — os de baixo são suposição. Gravar o número errado numa
/// tabela compartilhada por todo o ecossistema CASH corrompe o
/// rastreamento de arquivo dos outros sistemas, não só deste worker (ver
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
/// Projeção de escrita/leitura de <c>Cobranca.Arquivo</c>, na base
/// CASH_COBRANCA (schema real em docs/cash-cobranca-referencia.md §1.1) —
/// a linha que o worker cria pra cada planilha antes de entregá-la ao
/// conversor.
///
/// A entidade **rica** (com a máquina de estados
/// <c>EtapasPermitidasPorStatus</c> e os métodos que impõem transição
/// válida) mora na API dona da tabela. Aqui é de propósito só uma projeção
/// mínima dos campos que este worker usa — não replicamos as invariantes
/// pra não ter duas fontes de verdade divergindo com o tempo.
///
/// A entidade **tem chave** (<see cref="ArquivoID"/>) porque é escrita, não
/// só lida. O <see cref="ArquivoID"/> é também o <c>id</c> da conversão: um
/// id só em toda a cadeia (registro, conversão, mensagem de conclusão),
/// nunca um GUID novo por chamada.
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
