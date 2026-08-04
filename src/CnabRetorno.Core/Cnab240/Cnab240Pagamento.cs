namespace CnabRetorno.Core.Cnab240;

/// <summary>
/// Posições dos campos dos segmentos de pagamento no layout FEBRABAN 240
/// V10.11 — §3.1.2 (segmentos A/B/C, "Pagamento através de Crédito em
/// Conta, Cheque, OP, DOC, TED ou Pagamento com Autenticação") e §3.1.3
/// (segmentos J/J-52, "Pagamento de Títulos de Cobrança e QR Code Pix").
///
/// Só os campos que o retorno precisa ler da remessa gravada estão aqui —
/// não é uma transcrição completa do layout. Posições 1-based, mesma
/// convenção de <see cref="Cnab240Campos"/>.
/// </summary>
public static class Cnab240Pagamento
{
    /// <summary>Segmento A — transferências (TEF, TED, PIX por dados
    /// bancários).</summary>
    public static class SegmentoA
    {
        public const char Codigo = 'A';

        public static string FavorecidoCamara(string l) => Cnab240Campos.LerTrim(l, 18, 20);
        public static string FavorecidoBanco(string l) => Cnab240Campos.LerTrim(l, 21, 23);
        public static string FavorecidoAgencia(string l) => Cnab240Campos.LerTrim(l, 24, 28);
        public static string FavorecidoDvAgencia(string l) => Cnab240Campos.LerTrim(l, 29, 29);
        public static string FavorecidoConta(string l) => Cnab240Campos.LerTrim(l, 30, 41);
        public static string FavorecidoDvConta(string l) => Cnab240Campos.LerTrim(l, 42, 42);
        public static string FavorecidoNome(string l) => Cnab240Campos.LerTrim(l, 44, 73);
        public static string SeuNumero(string l) => Cnab240Campos.LerTrim(l, 74, 93);
        public static string DataPagamento(string l) => Cnab240Campos.LerTrim(l, 94, 101);
        public static string TipoMoeda(string l) => Cnab240Campos.LerTrim(l, 102, 104);
        public static decimal ValorPagamento(string l) => Cnab240Campos.LerValor(l, 120, 134);
        public static string NossoNumero(string l) => Cnab240Campos.LerTrim(l, 135, 154);
        public static string Informacao2(string l) => Cnab240Campos.LerTrim(l, 178, 217);
    }

    /// <summary>Segmento B — complemento do favorecido (tipo/número de
    /// inscrição). Obrigatório junto do A.</summary>
    public static class SegmentoB
    {
        public const char Codigo = 'B';

        public static string TipoInscricao(string l) => Cnab240Campos.LerTrim(l, 18, 18);
        public static string NumeroInscricao(string l) => Cnab240Campos.LerTrim(l, 19, 32);
    }

    /// <summary>Segmento J — pagamento de títulos (boleto, tricon) e
    /// QR-Code Pix.</summary>
    public static class SegmentoJ
    {
        public const char Codigo = 'J';

        /// <summary>Distingue o J "normal" do registro opcional J-52, que
        /// carrega '52' nas posições 18-19.</summary>
        public static bool EhRegistroOpcional(string l) => Cnab240Campos.Ler(l, 18, 19) is "52" or "53";

        public static string CodigoBarras(string l) => Cnab240Campos.LerTrim(l, 18, 61);
        public static string NomeBeneficiario(string l) => Cnab240Campos.LerTrim(l, 62, 91);
        public static string DataVencimento(string l) => Cnab240Campos.LerTrim(l, 92, 99);
        public static decimal ValorTitulo(string l) => Cnab240Campos.LerValor(l, 100, 114);
        public static decimal ValorDesconto(string l) => Cnab240Campos.LerValor(l, 115, 129);
        public static decimal ValorAcrescimos(string l) => Cnab240Campos.LerValor(l, 130, 144);
        public static string DataPagamento(string l) => Cnab240Campos.LerTrim(l, 145, 152);
        public static decimal ValorPagamento(string l) => Cnab240Campos.LerValor(l, 153, 167);
        public static string SeuNumero(string l) => Cnab240Campos.LerTrim(l, 183, 202);
        public static string NossoNumero(string l) => Cnab240Campos.LerTrim(l, 203, 222);
        public static string CodigoMoeda(string l) => Cnab240Campos.LerTrim(l, 223, 224);
    }

    /// <summary>Segmento J-52 — identificação de pagador/beneficiário do
    /// título. Note que os campos de inscrição têm 15 posições aqui, não
    /// 14 como no header e no segmento B.</summary>
    public static class SegmentoJ52
    {
        public static string BeneficiarioTipoInscricao(string l) => Cnab240Campos.LerTrim(l, 76, 76);
        public static string BeneficiarioNumeroInscricao(string l) => Cnab240Campos.LerTrim(l, 77, 91);
        public static string BeneficiarioNome(string l) => Cnab240Campos.LerTrim(l, 92, 131);

        /// <summary>Variante PIX do J-52 — chave de endereçamento e TXID
        /// ocupam o espaço que no J-52 comum é do pagador final.</summary>
        public static string ChavePix(string l) => Cnab240Campos.LerTrim(l, 132, 210);
        public static string TxId(string l) => Cnab240Campos.LerTrim(l, 211, 240);
    }

    /// <summary>Tipo de Movimento (G060, posição 15) — no retorno é
    /// inclusão ou estorno.</summary>
    public static class TipoMovimento
    {
        public const string Inclusao = "0";
        public const string Estorno = "3"; // só retorno
    }

    /// <summary>Código da Instrução para Movimento (G061, posições 16-17).</summary>
    public static class CodigoInstrucao
    {
        public const string InclusaoRegistroLiberado = "00";
        public const string EstornoPorDevolucaoCamara = "33"; // exige TipoMovimento = '3'
    }

    /// <summary>Versão do layout do lote (G030, posições 14-16) — default
    /// do layout por tipo de lote.</summary>
    public static class VersaoLayoutLote
    {
        public const string SegmentoA = "046";
        public const string SegmentoJ = "040";
    }

    /// <summary>Código Remessa/Retorno (G015, header de arquivo posição
    /// 143).</summary>
    public static class CodigoRemessaRetorno
    {
        public const string Remessa = "1";
        public const string Retorno = "2";
    }
}
