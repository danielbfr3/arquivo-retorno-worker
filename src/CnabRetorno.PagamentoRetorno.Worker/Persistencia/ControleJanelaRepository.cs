using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.PagamentoRetorno.Worker.Persistencia;

/// <summary>
/// As duas camadas de idempotência dos arquivos **parciais**, ambas em
/// banco (DDL em deploy/pagamento-controle-janela.sql):
///
/// <list type="number">
///   <item><b>Marca d'água</b> (<c>ControleJanelaRetorno</c>) — até que
///   instante de desfecho cada cliente já foi reportado. Corta o grosso
///   do reprocessamento.</item>
///   <item><b>Pares reportados</b> (<c>ControlePagamentoReportado</c>) —
///   (PagamentoID, CodigoStatus) já enviados. Barra o caso que a marca
///   não pega: um UPDATE qualquer na linha do pagamento avança
///   <c>DataAtualizacao</c> e o traria de volta no delta com o mesmo
///   status de antes.</item>
/// </list>
///
/// Em banco, e não em memória: um restart no meio do expediente com o
/// controle em memória faria o parcial seguinte reenviar movimentações
/// que o cliente já recebeu, e arquivo bancário entregue não tem
/// desfazer. O consolidado não consulta nada disso — repete o dia útil
/// inteiro por design.
/// </summary>
public class ControleJanelaRepository(PagamentoDbContext db)
{
    /// <summary>Até onde este cliente já foi reportado. Sem linha
    /// devolve <paramref name="padrao"/> — o início do dia útil (o
    /// consolidado anterior), pra que o primeiro parcial de um cliente
    /// novo não puxe histórico antigo.</summary>
    public async Task<DateTime> ObterMarcaDaguaAsync(
        string documento, DateTime padrao, CancellationToken ct)
    {
        var marca = await db.ControleJanelas
            .Where(c => c.ClienteDocumento == documento)
            .Select(c => (DateTime?)c.UltimoInstanteReportado)
            .FirstOrDefaultAsync(ct);

        return marca ?? padrao;
    }

    /// <summary>Pares (PagamentoID, CodigoStatus) já reportados, dentre os
    /// candidatos — o delta exclui esses antes de montar o arquivo.</summary>
    public async Task<HashSet<(Guid, short)>> ObterReportadosAsync(
        IReadOnlyCollection<Guid> pagamentoIds, CancellationToken ct)
    {
        if (pagamentoIds.Count == 0) return [];

        var existentes = await db.Reportados
            .Where(r => pagamentoIds.Contains(r.PagamentoID))
            .ToListAsync(ct);

        return [.. existentes.Select(r => (r.PagamentoID, r.CodigoStatus))];
    }

    /// <summary>
    /// Registra o resultado de um arquivo gerado: avança a marca d'água
    /// pro maior instante **de fato incluído** (não pro horário da
    /// janela — uma movimentação com desfecho às 8h05 gravada só às 8h20
    /// ficaria de fora pra sempre se o corte fosse "8h30") e grava os
    /// pares reportados que ainda não existem (o consolidado repete
    /// pares que os parciais já gravaram — inserir de novo violaria a
    /// PK).
    ///
    /// A marca nunca retrocede: um consolidado, ou uma execução manual
    /// fora de ordem, não pode reabrir movimentações já reportadas.
    /// Tudo num único <c>SaveChangesAsync</c>.
    /// </summary>
    public async Task RegistrarAsync(
        string documento,
        DateTime ultimoInstante,
        IReadOnlyCollection<MovimentacaoPagamento> movimentacoes,
        CancellationToken ct)
    {
        var agora = DateTime.UtcNow;

        var marca = await db.ControleJanelas
            .AsTracking()
            .FirstOrDefaultAsync(c => c.ClienteDocumento == documento, ct);

        if (marca is null)
        {
            db.ControleJanelas.Add(new ControleJanelaRetorno
            {
                ClienteDocumento = documento,
                UltimoInstanteReportado = ultimoInstante,
                DataAtualizacao = agora,
            });
        }
        else if (ultimoInstante > marca.UltimoInstanteReportado)
        {
            marca.UltimoInstanteReportado = ultimoInstante;
            marca.DataAtualizacao = agora;
        }

        var ids = movimentacoes.Select(m => m.PagamentoID).ToList();
        var jaGravados = await ObterReportadosAsync(ids, ct);

        foreach (var m in movimentacoes)
        {
            if (jaGravados.Contains((m.PagamentoID, m.CodigoStatus))) continue;

            db.Reportados.Add(new PagamentoReportado
            {
                PagamentoID = m.PagamentoID,
                CodigoStatus = m.CodigoStatus,
                DataCriacao = agora,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
