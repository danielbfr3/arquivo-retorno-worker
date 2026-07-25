using CnabRetorno.Core.Aplicacao.Dtos;

namespace CnabRetorno.RetornoCron.Worker.Json;

/// <summary>Dados mínimos pra montar um arquivo/lote sintético quando não
/// existe um V real de origem (ver <see cref="MesclagemDadosConvertidos.MontarSintetico"/>).</summary>
public sealed record HeaderSintetico(string Banco, string Cnpj, string NomeEmpresa);

/// <summary>
/// Une os <see cref="DadosConvertidos"/> de V e PV (cada um convertido
/// separadamente via sync/upload) com as pendências do CASH_COBRANCA — tudo
/// a nível de JSON, não mais de bytes CNAB (substitui por completo
/// <c>MesclagemCnab240</c>, apagada). Concatena <c>Titulos</c>, renumera
/// tudo do zero e recalcula <c>Totais</c>.
/// </summary>
public class MesclagemDadosConvertidos
{
    /// <exception cref="DadosConvertidosDivergentesException">
    /// Banco, Empresa ou Conta de V e PV não coincidem — os dois arquivos
    /// não pertencem à mesma remessa.
    /// </exception>
    public DadosConvertidos Mesclar(
        DadosConvertidos v, DadosConvertidos? pv, IReadOnlyList<TituloConvertido> pendencias)
    {
        if (pv is not null) CompararCabecalhos(v, pv);

        var titulos = new List<TituloConvertido>(v.Titulos.Count + (pv?.Titulos.Count ?? 0) + pendencias.Count);
        titulos.AddRange(v.Titulos);
        if (pv is not null) titulos.AddRange(pv.Titulos);
        titulos.AddRange(pendencias);

        var renumerados = Renumerar(titulos);

        return v with
        {
            Titulos = renumerados,
            Totais = RecalcularTotais(v.Totais, pv?.Totais, renumerados.Count),
        };
    }

    /// <summary>Monta um <see cref="DadosConvertidos"/> válido só com
    /// <paramref name="pendencias"/>, quando não há V real de origem —
    /// caso do laço pós-lote (cliente com pendência no CASH_COBRANCA mas
    /// sem V/PV no dia). Agência/conta ficam em branco (sem valor de
    /// configuração disponível — TODO(a-confirmar)).</summary>
    public DadosConvertidos MontarSintetico(HeaderSintetico header, IReadOnlyList<TituloConvertido> pendencias)
    {
        var renumerados = Renumerar(pendencias);

        return new DadosConvertidos
        {
            Arquivo = new ArquivoConvertido
            {
                Banco = header.Banco,
                Empresa = new EmpresaConvertida { TipoInscricao = "2", NumeroInscricao = header.Cnpj, Nome = header.NomeEmpresa },
                Conta = new ContaConvertida(),
            },
            Lote = new LoteConvertido
            {
                Empresa = new EmpresaConvertida { TipoInscricao = "2", NumeroInscricao = header.Cnpj, Nome = header.NomeEmpresa },
                Conta = new ContaConvertida(),
            },
            Titulos = renumerados,
            Totais = RecalcularTotais(new TotaisConvertidos(), null, renumerados.Count),
        };
    }

    /// <summary>
    /// Substitui o número sequencial do arquivo pelo valor reservado em
    /// <c>Cobranca.Parametro.SequencialAtual</c> (ver
    /// <see cref="Persistencia.SequencialArquivoRepository"/>).
    ///
    /// Escreve nos **dois** campos — header de arquivo
    /// (<c>NumeroSequencialArquivo</c>) e header de lote
    /// (<c>NumeroRemessaRetorno</c>) — porque o CNAB carrega o mesmo
    /// número nos dois lugares; atualizar só um deixaria o arquivo
    /// internamente inconsistente.
    ///
    /// Aplicado **depois** da mesclagem, não durante: se a mesclagem
    /// falhar (headers divergentes), nenhum número é consumido — buraco
    /// na série do cliente é um problema real, já que o outro lado pode
    /// validar continuidade.
    /// </summary>
    public DadosConvertidos AplicarSequencial(DadosConvertidos dados, long sequencial) => dados with
    {
        // TODO(a-confirmar): o campo do CNAB tem 6 posições, então a série
        // vira inválida acima de 999999 — não há regra definida pra
        // rotação (voltar a 1?) nem alerta antes disso.
        Arquivo = dados.Arquivo with { NumeroSequencialArquivo = (int)sequencial },
        Lote = dados.Lote with { NumeroRemessaRetorno = (int)sequencial },
    };

    private static List<TituloConvertido> Renumerar(IReadOnlyList<TituloConvertido> titulos)
        => titulos.Select((t, i) => t with { NumeroRegistro = 1 + i * 2 }).ToList();

    // TODO(a-confirmar): nenhum exemplo confirma a fórmula de
    // QuantidadeRegistros — assumido 2 por item (T+U implícito, mesma
    // convenção do NumeroRegistro "+2"). ValorTotalCobrancaSimples soma os
    // totais reais de V+PV; pendência não contribui valor (mesma decisão
    // já tomada pro trailer CNAB, ver docs/riscos-conhecidos.md item 5).
    private static TotaisConvertidos RecalcularTotais(
        TotaisConvertidos vTotais, TotaisConvertidos? pvTotais, int quantidadeTitulos) => new()
    {
        Titulos = quantidadeTitulos,
        QuantidadeRegistros = quantidadeTitulos * 2,
        ValorTotalCobrancaSimples = vTotais.ValorTotalCobrancaSimples + (pvTotais?.ValorTotalCobrancaSimples ?? 0m),
    };

    private static void CompararCabecalhos(DadosConvertidos v, DadosConvertidos pv)
    {
        Comparar("Banco (arquivo)", v.Arquivo.Banco, pv.Arquivo.Banco);
        Comparar("Tipo inscrição empresa (arquivo)", v.Arquivo.Empresa.TipoInscricao, pv.Arquivo.Empresa.TipoInscricao);
        Comparar("Inscrição empresa (arquivo)", v.Arquivo.Empresa.NumeroInscricao, pv.Arquivo.Empresa.NumeroInscricao);
        Comparar("Agência (arquivo)", v.Arquivo.Conta.Agencia, pv.Arquivo.Conta.Agencia);
        Comparar("Conta (arquivo)", v.Arquivo.Conta.Conta, pv.Arquivo.Conta.Conta);
        Comparar("Tipo inscrição empresa (lote)", v.Lote.Empresa.TipoInscricao, pv.Lote.Empresa.TipoInscricao);
        Comparar("Inscrição empresa (lote)", v.Lote.Empresa.NumeroInscricao, pv.Lote.Empresa.NumeroInscricao);
        Comparar("Conta (lote)", v.Lote.Conta.Conta, pv.Lote.Conta.Conta);
    }

    private static void Comparar(string campo, string? valorV, string? valorPv)
    {
        if (!string.Equals(valorV, valorPv, StringComparison.Ordinal))
            throw new DadosConvertidosDivergentesException(campo, valorV, valorPv);
    }
}
