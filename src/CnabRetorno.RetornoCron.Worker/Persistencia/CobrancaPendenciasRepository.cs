using CnabRetorno.Core.Dominio;
using Microsoft.EntityFrameworkCore;

namespace CnabRetorno.RetornoCron.Worker.Persistencia;

/// <summary>Título/instrução pareado com seu erro, quando existir — usado
/// pelo <c>PendenciasParaTitulosConvertidosFactory</c> pra montar o
/// <c>TituloConvertido</c> correspondente.</summary>
public sealed record TituloPendente(Titulo Titulo, TituloErro? Erro);
public sealed record InstrucaoPendente(InstrucaoComTitulo Instrucao, InstrucaoErro? Erro);

/// <summary>
/// Consulta títulos e instruções negados ou com erro na base CASH_COBRANCA
/// (schema real, ver docs/cash-cobranca-referencia.md), pra virarem objetos
/// JSON injetados em <c>data.titulos[]</c> antes do envio pro conversor
/// assíncrono (ver docs/regras-de-negocio.md).
///
/// Estratégia de consulta: título/instrução "pendente" é qualquer um com
/// CodigoStatus negado OU com linha em TituloErro/InstrucaoErro na janela
/// D-1 — dedupe por ID garante um único item mesmo quando as duas
/// condições valem ao mesmo tempo (ver docs/regras-de-negocio.md).
/// </summary>
public class CobrancaPendenciasRepository(CobrancaDbContext db)
{
    /// <summary>
    /// TODO(a-confirmar): valor real de "negado" em Cobranca.Status não
    /// documentado — docs/cash-cobranca-referencia.md não lista os valores
    /// de CodigoStatus. Ajustar quando confirmado.
    /// </summary>
    private const short CodigoStatusNegado = -1;

    public async Task<IReadOnlyList<TituloPendente>> ObterTitulosNegadosOuComErroAsync(
        string cnpj, DateOnly dataD1, CancellationToken ct)
    {
        var (inicio, fim) = JanelaDoDia(dataD1);

        var titulosDoDia = await db.Titulos
            .Where(t => t.ClienteDocumento == cnpj && t.DataAtualizacao >= inicio && t.DataAtualizacao <= fim)
            .ToListAsync(ct);

        var ids = titulosDoDia.Select(t => t.TituloID).ToList();
        var errosPorTitulo = (await db.TitulosErro.Where(e => ids.Contains(e.TituloID)).ToListAsync(ct))
            .ToLookup(e => e.TituloID);

        return titulosDoDia
            .Select(t => new TituloPendente(t, errosPorTitulo[t.TituloID].FirstOrDefault()))
            .Where(p => p.Titulo.CodigoStatus == CodigoStatusNegado || p.Erro is not null)
            .ToList();
    }

    public async Task<IReadOnlyList<InstrucaoPendente>> ObterInstrucoesNegadasOuComErroAsync(
        string cnpj, DateOnly dataD1, CancellationToken ct)
    {
        var (inicio, fim) = JanelaDoDia(dataD1);

        var instrucoesDoDia = await db.InstrucoesComTitulo
            .Where(i => i.ClienteDocumento == cnpj && i.DataAtualizacao >= inicio && i.DataAtualizacao <= fim)
            .ToListAsync(ct);

        var ids = instrucoesDoDia.Select(i => i.InstrucaoID).ToList();
        var errosPorInstrucao = (await db.InstrucoesErro.Where(e => ids.Contains(e.InstrucaoID)).ToListAsync(ct))
            .ToLookup(e => e.InstrucaoID);

        return instrucoesDoDia
            .Select(i => new InstrucaoPendente(i, errosPorInstrucao[i.InstrucaoID].FirstOrDefault()))
            .Where(p => p.Instrucao.CodigoStatus == CodigoStatusNegado || p.Erro is not null)
            .ToList();
    }

    /// <summary>Lista os CNPJs com título ou instrução negados/com erro na
    /// janela D-1 — alimenta o laço pós-lote (clientes sem V/PV no dia mas
    /// com pendência a reportar).</summary>
    public async Task<IReadOnlyList<string>> ListarClientesComPendenciaAsync(DateOnly dataD1, CancellationToken ct)
    {
        var (inicio, fim) = JanelaDoDia(dataD1);

        var titulosDoDia = await db.Titulos
            .Where(t => t.DataAtualizacao >= inicio && t.DataAtualizacao <= fim)
            .Select(t => new { t.TituloID, t.ClienteDocumento, t.CodigoStatus })
            .ToListAsync(ct);
        var idsTitulos = titulosDoDia.Select(t => t.TituloID).ToList();
        var idsComErroTitulo = (await db.TitulosErro
            .Where(e => idsTitulos.Contains(e.TituloID))
            .Select(e => e.TituloID)
            .ToListAsync(ct)).ToHashSet();

        var instrucoesDoDia = await db.Instrucoes
            .Where(i => i.DataAtualizacao >= inicio && i.DataAtualizacao <= fim)
            .Select(i => new { i.InstrucaoID, i.ClienteDocumento, i.CodigoStatus })
            .ToListAsync(ct);
        var idsInstrucoes = instrucoesDoDia.Select(i => i.InstrucaoID).ToList();
        var idsComErroInstrucao = (await db.InstrucoesErro
            .Where(e => idsInstrucoes.Contains(e.InstrucaoID))
            .Select(e => e.InstrucaoID)
            .ToListAsync(ct)).ToHashSet();

        var cnpjsTitulos = titulosDoDia
            .Where(t => t.CodigoStatus == CodigoStatusNegado || idsComErroTitulo.Contains(t.TituloID))
            .Select(t => t.ClienteDocumento);

        var cnpjsInstrucoes = instrucoesDoDia
            .Where(i => i.CodigoStatus == CodigoStatusNegado || idsComErroInstrucao.Contains(i.InstrucaoID))
            .Select(i => i.ClienteDocumento);

        return cnpjsTitulos.Union(cnpjsInstrucoes).Distinct().ToList();
    }

    private static (DateTime inicio, DateTime fim) JanelaDoDia(DateOnly dia)
        => (dia.ToDateTime(TimeOnly.MinValue), dia.ToDateTime(TimeOnly.MaxValue));
}
