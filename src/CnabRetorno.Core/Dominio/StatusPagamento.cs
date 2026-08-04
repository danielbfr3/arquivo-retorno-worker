namespace CnabRetorno.Core.Dominio;

/// <summary>
/// Valores reais de <c>Pagamento.Status</c> (base ASA_CASH_PAGAMENTO,
/// extração de 03/08/2026) — diferente dos enums de <see cref="Arquivo"/>,
/// estes **não** são suposição.
/// </summary>
public enum StatusPagamento : short
{
    Incluido = 1,
    Processando = 2,
    Rejeitado = 3,
    Cancelado = 4,
    Erro = 5,
    Finalizado = 6,
}

/// <summary>
/// Regras de quais pagamentos entram no arquivo de retorno e como o status
/// vira código de ocorrência FEBRABAN (domínio G059, campo "Códigos das
/// Ocorrências p/ Retorno" — 10 posições, até 5 ocorrências de 2 dígitos).
/// </summary>
public static class MovimentacaoRelatavel
{
    /// <summary>
    /// Só estados **finais** entram no retorno: Rejeitado, Cancelado, Erro
    /// e Finalizado. Incluído e Processando ainda estão em voo e voltariam
    /// depois com outro status — reportá-los mandaria informação
    /// contraditória pro cliente, que é o tipo de erro que não dá pra
    /// desfazer num arquivo já entregue.
    /// </summary>
    public static readonly short[] StatusFinais =
    [
        (short)StatusPagamento.Rejeitado,
        (short)StatusPagamento.Cancelado,
        (short)StatusPagamento.Erro,
        (short)StatusPagamento.Finalizado,
    ];

    public const string OcorrenciaEfetivado = "00"; // Crédito ou Débito Efetivado
    public const string OcorrenciaCanceladoPeloPagador = "02"; // Crédito ou Débito Cancelado pelo Pagador/Credor

    /// <summary>
    /// Resolve as 10 posições do campo de ocorrências.
    ///
    /// A ordem importa: <paramref name="codigoOcorrencia"/> das tabelas de
    /// cabeçalho é <c>varchar(10)</c> — exatamente a largura do campo
    /// G059 —, o que indica que o dado já é gravado no formato de destino
    /// pelo sistema que processa o pagamento. Quando ele vier preenchido,
    /// é ele que vale; o mapeamento por status abaixo é só o fallback pros
    /// casos em que o campo veio vazio.
    ///
    /// TODO(a-confirmar): confirmar com o time dono da base que
    /// <c>CodigoOcorrencia</c> é mesmo FEBRABAN G059 e não um código
    /// interno de mesma largura. Se for interno, precisa de uma tabela
    /// de-para aqui — sem ela o cliente recebe código inválido.
    /// </summary>
    public static string ResolverOcorrencias(string? codigoOcorrencia, short codigoStatus)
    {
        if (!string.IsNullOrWhiteSpace(codigoOcorrencia))
            return codigoOcorrencia.Trim().PadRight(10);

        var padrao = (StatusPagamento)codigoStatus switch
        {
            StatusPagamento.Finalizado => OcorrenciaEfetivado,
            StatusPagamento.Cancelado => OcorrenciaCanceladoPeloPagador,
            // Rejeitado/Erro sem código gravado: não há ocorrência genérica
            // de "rejeitado" no G059 — os códigos são todos específicos do
            // motivo (AE, AG, CD...). Brancos é o que o layout permite;
            // inventar um código erraria o motivo.
            _ => string.Empty,
        };

        return padrao.PadRight(10);
    }
}
