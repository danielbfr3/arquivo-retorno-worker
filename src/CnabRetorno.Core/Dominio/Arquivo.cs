namespace CnabRetorno.Core.Dominio;

/// <summary>
/// Status do arquivo em <c>Cobranca.Arquivo</c> (smallint no banco).
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

/// <summary>Etapa do arquivo em <c>Cobranca.Arquivo</c> (smallint no
/// banco). Mesma ressalva de <see cref="ArquivoStatus"/> sobre os valores
/// numéricos serem suposição.</summary>
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
/// Projeção de escrita/leitura de <c>Cobranca.Arquivo</c> (base
/// CASH_COBRANCA, SQL Server — schema em docs/cash-cobranca-referencia.md
/// §1.1). É a linha que o Robô 1 cria antes de mandar o retorno pro
/// conversor assíncrono, e cujo <see cref="ArquivoID"/> vai no campo
/// <c>id</c> da chamada — é por ele que o Robô 2 reencontra o cliente
/// quando a conclusão chega via SQS.
///
/// A entidade **rica** (com a máquina de estados
/// <c>EtapasPermitidasPorStatus</c>, métodos <c>Criar</c>/
/// <c>AtualizarStatus</c>/<c>AtualizarEtapa</c> que impõem transição
/// válida) mora na cash-cobranca-api, dona da tabela. Aqui é de propósito
/// só uma projeção mínima dos campos que estes dois workers usam — não
/// replicamos as invariantes pra não ter duas fontes de verdade
/// divergindo com o tempo.
///
/// Diferente das demais projeções deste projeto, esta entidade **tem
/// chave** (<see cref="ArquivoID"/>) — é a única que é escrita, não só
/// lida.
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

    // Mutáveis: o Robô 2 avança status/etapa quando conclui o registro do
    // arquivo final no Gestor de Arquivos.
    public DateTime? DataAtualizacao { get; set; }
    public short ArquivoStatus { get; set; }
    public short ArquivoEtapa { get; set; }
}
