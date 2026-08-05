namespace CnabRetorno.PagamentoRetorno.Worker.Cnab;

public class GeracaoOptions
{
    public const string Secao = "Geracao";

    /// <summary>
    /// "Conversor" (padrão) — envia o JSON pro conversor síncrono externo,
    /// que devolve o CNAB240 já pronto (e completa os dados
    /// institucionais do header a partir do cadastro dele).
    ///
    /// "CnabDireto" — o próprio worker escreve o CNAB240 posicionalmente
    /// (<c>Core.Cnab240.EscritorCnab240Pagamento</c>), sem chamar o
    /// conversor. Exige <c>ConnectionStrings:Adesao</c> configurada: os
    /// dados institucionais (convênio, agência/conta com DVs, nome,
    /// endereço) vêm de <c>ASA_CASH_ADESAO</c>, buscados por
    /// <c>EmpresaAdesaoRepository</c> — ver docs/pagamento-referencia.md
    /// §5 pro porquê desses campos não existirem em nenhuma tabela já
    /// mapeada por este projeto.
    ///
    /// Trade-off registrado em docs/riscos-conhecidos.md: gerar direto
    /// pula a homologação byte-a-byte que o conversor (motor
    /// compartilhado do time) já tem com cada cliente.
    /// </summary>
    public string Modo { get; set; } = "Conversor";
}
