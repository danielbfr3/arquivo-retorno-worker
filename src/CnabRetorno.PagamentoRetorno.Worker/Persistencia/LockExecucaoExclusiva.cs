using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CnabRetorno.PagamentoRetorno.Worker.Persistencia;

/// <summary>
/// Lock de execução entre processos via <c>sp_getapplock</c> (lock
/// aplicativo do SQL Server, dono = sessão).
///
/// É o que impede duas réplicas do worker de gerarem a mesma janela: sem
/// isso, cada uma produziria um arquivo por cliente com NSAs diferentes —
/// pior que um erro visível, porque os dois pareceriam legítimos. O banco
/// é o único ponto que todas as réplicas já compartilham, então o lock
/// mora nele; não precisa de Redis/ZooKeeper pra isso.
///
/// A conexão fica aberta enquanto o lock viver (lock de sessão morre com
/// a sessão — é o que garante liberação automática se o pod cair no meio).
/// <c>@LockTimeout = 0</c>: quem não consegue na hora desiste — a réplica
/// perdedora pula a janela em vez de enfileirar atrás da vencedora e
/// gerar tudo de novo logo depois.
/// </summary>
public static class LockExecucaoExclusiva
{
    /// <summary>Devolve o liberador do lock, ou <c>null</c> se outra
    /// sessão já o detém. ADO puro (não <c>SqlQuery</c>) — EXEC não
    /// sobrevive ao embrulho em subselect do EF.</summary>
    public static async Task<IAsyncDisposable?> TentarAdquirirAsync(
        DatabaseFacade db, string recurso, CancellationToken ct)
    {
        await db.OpenConnectionAsync(ct);

        var resultado = await ExecutarApplockAsync(db.GetDbConnection(), "sp_getapplock", recurso, adquirir: true, ct);

        // >= 0: concedido (0 direto, 1 após espera). Negativo: recusado.
        if (resultado >= 0) return new Liberador(db, recurso);

        await db.CloseConnectionAsync();
        return null;
    }

    private static async Task<int> ExecutarApplockAsync(
        DbConnection conexao, string procedure, string recurso, bool adquirir, CancellationToken ct)
    {
        await using var comando = conexao.CreateCommand();
        comando.CommandText = procedure;
        comando.CommandType = CommandType.StoredProcedure;

        AdicionarParametro(comando, "@Resource", recurso);
        AdicionarParametro(comando, "@LockOwner", "Session");
        if (adquirir)
        {
            AdicionarParametro(comando, "@LockMode", "Exclusive");
            AdicionarParametro(comando, "@LockTimeout", 0);
        }

        var retorno = comando.CreateParameter();
        retorno.ParameterName = "@Retorno";
        retorno.DbType = DbType.Int32;
        retorno.Direction = ParameterDirection.ReturnValue;
        comando.Parameters.Add(retorno);

        await comando.ExecuteNonQueryAsync(ct);
        return (int)(retorno.Value ?? -999);
    }

    private static void AdicionarParametro(DbCommand comando, string nome, object valor)
    {
        var parametro = comando.CreateParameter();
        parametro.ParameterName = nome;
        parametro.Value = valor;
        comando.Parameters.Add(parametro);
    }

    private sealed class Liberador(DatabaseFacade db, string recurso) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecutarApplockAsync(db.GetDbConnection(), "sp_releaseapplock", recurso, adquirir: false, CancellationToken.None);
            }
            catch
            {
                // Falha ao liberar não pode derrubar o fim da janela — e o
                // lock morre junto com a sessão quando a conexão fechar.
            }
            finally
            {
                await db.CloseConnectionAsync();
            }
        }
    }
}
