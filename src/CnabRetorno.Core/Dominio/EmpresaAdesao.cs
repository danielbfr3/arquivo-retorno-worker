namespace CnabRetorno.Core.Dominio;

/// <summary>
/// Cadastro institucional do cliente — os campos do header FEBRABAN que
/// não existem em nenhuma tabela de <c>ASA_CASH_PAGAMENTO</c> (nem de
/// <c>CASH_COBRANCA</c>, conferido): convênio, agência/conta com
/// dígitos verificadores, nome e endereço da empresa.
///
/// Fonte hipotética: base <c>ASA_CASH_ADESAO</c>, apontada pelo usuário
/// como o lugar provável — mas **nunca inspecionada**. Nome de tabela,
/// schema e de cada coluna abaixo são placeholder de propósito
/// (`TODO(a-confirmar)` coletivo desta classe inteira); o mapeamento real
/// fica em <c>AdesaoDbContext.OnModelCreating</c>, um único lugar pra
/// corrigir quando o schema real chegar.
///
/// Só existe porque o retorno de pagamentos tem uma segunda estratégia de
/// geração — <see cref="ArquivoStatus"/> não muda, mas o CNAB pode ser
/// escrito diretamente pelo worker (<c>Geracao:Modo = CnabDireto</c>) em
/// vez de pedido ao conversor externo. Ver
/// <c>Cnab240.EscritorCnab240Pagamento</c> e
/// docs/pagamento-referencia.md §5.
/// </summary>
public class EmpresaAdesao
{
    public required string Documento { get; init; }

    /// <summary>G007, header posições 33-52 (20 alfa). Sem este campo o
    /// arquivo sai com convênio zerado, e muitos parsers de cliente
    /// validam justamente esse campo primeiro.</summary>
    public string? CodigoConvenio { get; init; }

    // G008-G012 — mesma tripla (agência/conta/DVs) usada no header de
    // arquivo e no header de cada lote.
    public string? Agencia { get; init; }
    public string? DvAgencia { get; init; }
    public string? Conta { get; init; }
    public string? DvConta { get; init; }
    public string? DvAgenciaConta { get; init; }

    /// <summary>G013, 30 alfa.</summary>
    public string? NomeEmpresa { get; init; }

    // G032-G036 — endereço da empresa, só no header de lote (não existe
    // no header de arquivo).
    public string? Logradouro { get; init; }
    public string? NumeroEndereco { get; init; }
    public string? ComplementoEndereco { get; init; }
    public string? Cidade { get; init; }
    public string? Cep { get; init; }
    public string? ComplementoCep { get; init; }
    public string? Estado { get; init; }
}
