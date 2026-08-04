using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.PagamentoRetorno.Worker.Persistencia;

/// <summary>Movimentações de um cliente numa janela, já agrupadas.</summary>
public sealed record MovimentacoesDoCliente(
    string Documento,
    short TipoDocumento,
    string? ContaHeader,
    IReadOnlyList<MovimentacaoPagamento> Movimentacoes)
{
    /// <summary>Maior instante de desfecho do grupo — é o que vira marca
    /// d'água depois que o arquivo é gerado com sucesso.</summary>
    public DateTime UltimoInstante => Movimentacoes.Max(m => m.DataAtualizacao ?? m.DataCriacao);
}

/// <summary>
/// Lê as movimentações que entram no arquivo de retorno.
///
/// O corte é feito sobre <c>COALESCE(DataAtualizacao, DataCriacao)</c> —
/// o instante em que o pagamento chegou ao desfecho. <c>DataCriacao</c>
/// sozinha diria quando ele foi registrado, que pode ser dias antes do
/// pagamento efetivamente acontecer.
/// </summary>
public class MovimentacoesRepository(PagamentoDbContext db, ILogger<MovimentacoesRepository> logger)
{
    /// <summary>
    /// Movimentações do dia até o instante da janela, agrupadas por
    /// cliente.
    ///
    /// Serve às duas janelas: o consolidado usa o resultado como está, e
    /// o parcial recorta por cliente depois, contra a marca d'água de cada
    /// um (ver <c>GerarRetornosPagamentoPipeline</c>). Um único intervalo
    /// na consulta não daria conta do parcial — cada cliente tem seu
    /// próprio ponto de corte, porque um pode ter falhado na janela
    /// anterior enquanto os outros passaram.
    /// </summary>
    public async Task<List<MovimentacoesDoCliente>> ObterDoDiaAsync(
        DateTime inicioDia, DateTime fimInclusivo, CancellationToken ct)
    {
        var linhas = await db.Movimentacoes
            .Where(m => MovimentacaoRelatavel.StatusFinais.Contains(m.CodigoStatus))
            .Where(m => (m.DataAtualizacao ?? m.DataCriacao) >= inicioDia
                     && (m.DataAtualizacao ?? m.DataCriacao) <= fimInclusivo)
            .ToListAsync(ct);

        // Agrupamento em memória de propósito: a projeção é sem chave e o
        // conjunto de uma janela é pequeno (movimentações de uma hora).
        // Agrupar no banco exigiria uma segunda consulta pra rebuscar as
        // linhas de cada grupo.
        return [.. linhas
            .GroupBy(m => m.ClienteDocumento)
            .Select(g => new MovimentacoesDoCliente(
                Documento: g.Key,
                TipoDocumento: g.First().ClienteTipoDocumento,
                ContaHeader: ResolverContaHeader(g.Key, g),
                Movimentacoes: [.. g.OrderBy(m => m.DataAtualizacao ?? m.DataCriacao)]))
            .OrderBy(c => c.Documento, StringComparer.Ordinal)];
    }

    /// <summary>
    /// A granularidade escolhida é **um arquivo por cliente** — então um
    /// cliente com movimentações em mais de uma <c>ClienteContaHeader</c>
    /// no mesmo dia é uma ambiguidade real: o header do CNAB só carrega
    /// uma conta, e todas as movimentações vão sair debaixo dela.
    ///
    /// Não dá pra resolver aqui (dividir em dois arquivos mudaria a
    /// granularidade decidida), mas silenciar seria pior: o arquivo sairia
    /// com a conta "errada" pra parte dos pagamentos e ninguém saberia.
    /// Fica o aviso com as contas envolvidas, pra decisão manual — se o
    /// caso aparecer de verdade, é sinal de que a granularidade precisa
    /// virar cliente+conta.
    /// </summary>
    private string? ResolverContaHeader(string documento, IEnumerable<MovimentacaoPagamento> grupo)
    {
        var contas = grupo
            .Select(m => m.ClienteContaHeader)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();

        if (contas.Count > 1)
            logger.LogWarning(
                "Cliente {Documento} tem movimentações em {Quantidade} contas distintas na mesma janela ({Contas}) — " +
                "o arquivo sai com a primeira; as demais movimentações ficam sob a conta \"errada\" no header",
                documento, contas.Count, string.Join(", ", contas));

        return contas.FirstOrDefault();
    }
}
