using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.PagamentoRetorno.Worker.Persistencia;

/// <summary>Não foi possível reservar um número sequencial pro cliente —
/// falha isolada do arquivo (não derruba a janela). Melhor não enviar do
/// que enviar um retorno com NSA errado: o cliente usa esse número pra
/// detectar arquivo faltando ou repetido.</summary>
public sealed class SequencialIndisponivelException(string documento, int linhasAfetadas)
    : Exception(linhasAfetadas == 0
        ? $"Nenhuma linha em Pagamento.Parametro pro documento '{documento}' — sem SequencialAtual pra reservar."
        : $"{linhasAfetadas} linhas em Pagamento.Parametro pro documento '{documento}' — " +
          "esperada exatamente 1. Todas foram incrementadas; corrigir manualmente.")
{
    public string Documento { get; } = documento;
    public int LinhasAfetadas { get; } = linhasAfetadas;
}

/// <summary>
/// Controle do NSA (Número Sequencial de Arquivo, campo G018 do header,
/// posições 158-163) por cliente, em <c>Pagamento.Parametro.SequencialAtual</c>.
/// </summary>
public class SequencialArquivoRepository(PagamentoDbContext db)
{
    /// <summary>
    /// Incrementa e devolve o próximo sequencial, reservando-o.
    ///
    /// O <c>UPDATE ... OUTPUT</c> num único statement é o ponto central:
    /// incremento e leitura do valor novo acontecem atomicamente no
    /// servidor, então duas execuções concorrentes (duas réplicas do
    /// worker, ou dois clientes processados em paralelo) nunca recebem o
    /// mesmo número — o que um <c>SELECT</c> seguido de <c>UPDATE</c> não
    /// garantiria.
    /// </summary>
    /// <exception cref="SequencialIndisponivelException">
    /// Nenhuma (ou mais de uma) linha em <c>Pagamento.Parametro</c> pro
    /// documento.
    /// </exception>
    public async Task<long> ReservarProximoAsync(string documento, CancellationToken ct)
    {
        // "AS Value" é exigência do SqlQuery<T> do EF Core pra tipo
        // escalar — a coluna do resultado precisa se chamar Value.
        var sequenciais = await db.Database
            .SqlQuery<long>($"""
                UPDATE Pagamento.Parametro
                SET SequencialAtual = SequencialAtual + 1
                OUTPUT INSERTED.SequencialAtual AS Value
                WHERE Documento = {documento}
                """)
            .ToListAsync(ct);

        if (sequenciais.Count != 1)
            throw new SequencialIndisponivelException(documento, sequenciais.Count);

        return sequenciais[0];
    }
}
