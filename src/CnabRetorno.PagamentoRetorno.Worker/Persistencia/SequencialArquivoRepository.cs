using System.Data;
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
    ///
    /// Em ADO puro (DbCommand), e não <c>SqlQuery&lt;T&gt;</c>, de
    /// propósito: o EF embrulha o SQL do <c>SqlQuery</c> num subselect
    /// (<c>SELECT ... FROM (&lt;sql&gt;)</c>), e <c>UPDATE ... OUTPUT</c>
    /// não é válido como subquery — o embrulho estouraria só em runtime,
    /// no cluster, já que nada aqui roda contra banco real nos testes.
    /// </summary>
    /// <exception cref="SequencialIndisponivelException">
    /// Nenhuma (ou mais de uma) linha em <c>Pagamento.Parametro</c> pro
    /// documento.
    /// </exception>
    public async Task<long> ReservarProximoAsync(string documento, CancellationToken ct)
    {
        var conexao = db.Database.GetDbConnection();
        var manterAberta = conexao.State == ConnectionState.Open;
        if (!manterAberta) await db.Database.OpenConnectionAsync(ct);

        try
        {
            await using var comando = conexao.CreateCommand();
            comando.CommandText = """
                UPDATE Pagamento.Parametro
                SET SequencialAtual = SequencialAtual + 1
                OUTPUT INSERTED.SequencialAtual
                WHERE Documento = @documento
                """;

            var parametro = comando.CreateParameter();
            parametro.ParameterName = "@documento";
            parametro.Value = documento;
            comando.Parameters.Add(parametro);

            var sequenciais = new List<long>();
            await using var leitor = await comando.ExecuteReaderAsync(ct);
            while (await leitor.ReadAsync(ct))
                sequenciais.Add(leitor.GetInt64(0));

            if (sequenciais.Count != 1)
                throw new SequencialIndisponivelException(documento, sequenciais.Count);

            return sequenciais[0];
        }
        finally
        {
            if (!manterAberta) await db.Database.CloseConnectionAsync();
        }
    }
}
