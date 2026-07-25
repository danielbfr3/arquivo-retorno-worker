using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.RetornoCron.Worker.Persistencia;

/// <summary>Não foi possível reservar um número sequencial pro cliente —
/// falha isolada do arquivo (não derruba o lote). Melhor não enviar do que
/// enviar um retorno com sequencial errado.</summary>
public sealed class SequencialIndisponivelException(string documento, int linhasAfetadas)
    : Exception(linhasAfetadas == 0
        ? $"Nenhuma linha em Cobranca.Parametro pro documento '{documento}' — sem SequencialAtual pra reservar."
        : $"{linhasAfetadas} linhas em Cobranca.Parametro pro documento '{documento}' — " +
          "esperada exatamente 1. Todas foram incrementadas; corrigir manualmente.")
{
    public string Documento { get; } = documento;
    public int LinhasAfetadas { get; } = linhasAfetadas;
}

/// <summary>
/// Controle do número sequencial de arquivo por cliente
/// (<c>Cobranca.Parametro.SequencialAtual</c>). A série é compartilhada
/// entre remessa e retorno: o banco recebe a remessa 1, envia o retorno 2,
/// e assim por diante — por isso o sequencial do retorno **não** pode ser
/// o que veio no header do arquivo V (esse é o da remessa, e vem errado
/// se o arquivo precisar ser gerado de novo).
/// </summary>
public class SequencialArquivoRepository(CobrancaDbContext db)
{
    /// <summary>
    /// Incrementa e devolve o próximo sequencial do cliente, reservando-o.
    ///
    /// O <c>UPDATE ... OUTPUT</c> num único statement é o ponto central:
    /// incremento e leitura do valor novo acontecem atomicamente no
    /// servidor, então duas execuções concorrentes (dois processos, ou o
    /// mesmo processo em paralelo) nunca recebem o mesmo número — o que
    /// um <c>SELECT</c> seguido de <c>UPDATE</c> não garantiria.
    /// </summary>
    /// <exception cref="SequencialIndisponivelException">
    /// Nenhuma (ou mais de uma) linha em <c>Cobranca.Parametro</c> pro
    /// documento.
    /// </exception>
    public async Task<long> ReservarProximoAsync(string documento, CancellationToken ct)
    {
        // "AS Value" é exigência do SqlQuery<T> do EF Core pra tipo
        // escalar — a coluna do resultado precisa se chamar Value.
        var sequenciais = await db.Database
            .SqlQuery<long>($"""
                UPDATE Cobranca.Parametro
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
