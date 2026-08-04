using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.PagamentoRetorno.Worker.Persistencia;

/// <summary>
/// Marca d'água dos arquivos parciais, em <c>Pagamento.ControleJanelaRetorno</c>
/// (tabela nova, exclusiva deste worker — DDL em
/// deploy/pagamento-controle-janela.sql).
///
/// Persistida em banco, e não em memória: um restart no meio do
/// expediente com o controle em memória faria o parcial seguinte
/// reenviar movimentações que o cliente já recebeu. Num arquivo bancário
/// já entregue isso não tem desfazer.
/// </summary>
public class ControleJanelaRepository(PagamentoDbContext db)
{
    /// <summary>Até onde este cliente já foi reportado no dia. Sem linha
    /// significa "nada ainda hoje" — devolve o início do dia, e o primeiro
    /// parcial pega tudo desde a virada.</summary>
    public async Task<DateTime> ObterMarcaDaguaAsync(
        string documento, DateOnly dia, DateTime inicioDoDia, CancellationToken ct)
    {
        var marca = await db.ControleJanelas
            .Where(c => c.ClienteDocumento == documento && c.DataReferencia == dia)
            .Select(c => (DateTime?)c.UltimoInstanteReportado)
            .FirstOrDefaultAsync(ct);

        return marca ?? inicioDoDia;
    }

    /// <summary>
    /// Avança a marca d'água pro maior instante **de fato incluído** no
    /// arquivo — não pro horário da janela.
    ///
    /// A diferença importa: se uma movimentação com desfecho às 8h05 só
    /// for gravada no banco às 8h20, guardar "8h30" (o horário da janela)
    /// a deixaria de fora pra sempre. Guardando o maior instante incluído,
    /// ela entra no parcial seguinte.
    /// </summary>
    public async Task RegistrarAsync(
        string documento, DateOnly dia, DateTime ultimoInstante, CancellationToken ct)
    {
        var existente = await db.ControleJanelas
            .AsTracking()
            .FirstOrDefaultAsync(c => c.ClienteDocumento == documento && c.DataReferencia == dia, ct);

        if (existente is null)
        {
            db.ControleJanelas.Add(new ControleJanelaRetorno
            {
                ClienteDocumento = documento,
                DataReferencia = dia,
                UltimoInstanteReportado = ultimoInstante,
                DataAtualizacao = DateTime.UtcNow,
            });
        }
        else
        {
            // Nunca retrocede: um consolidado, ou uma execução manual fora
            // de ordem, não pode reabrir movimentações já reportadas.
            if (ultimoInstante <= existente.UltimoInstanteReportado) return;

            existente.UltimoInstanteReportado = ultimoInstante;
            existente.DataAtualizacao = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
