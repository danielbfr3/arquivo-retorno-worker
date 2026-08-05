using System.Globalization;
using System.Text;
using CnabRetorno.Core.Aplicacao.Dtos;
using CnabRetorno.Core.Dominio;

namespace CnabRetorno.Core.Cnab240;

/// <summary>
/// Escreve o CNAB240 de retorno de pagamentos posicionalmente, a partir do
/// mesmo <see cref="RetornoPagamentoJson"/> que hoje vai pro conversor
/// externo — é a via alternativa selecionada por
/// <c>Geracao:Modo = CnabDireto</c> (ver
/// <c>PagamentoRetorno.Worker/Cnab/CnabDiretoGeradorCnabPagamento.cs</c>).
///
/// Reaproveitar o mesmo DTO em vez de gerar direto das
/// <c>MovimentacaoPagamento</c> é o ponto central do desenho: toda a
/// lógica de negócio (forma de lançamento por meio, PIX QR x
/// transferência, título do próprio banco x outros, ocorrências,
/// valor/data reais só se efetivado) já mora em <c>MontagemRetornoPagamento</c>
/// e já é testada lá — este escritor só decide **como formatar** o que já
/// foi decidido, não repete a decisão.
///
/// A diferença de fundo entre os dois modos é a **origem dos dados
/// institucionais do header** (convênio, agência/conta com dígitos
/// verificadores, nome e endereço da empresa): o conversor externo tem
/// cadastro próprio e completa o que falta; aqui não existe outro lugar
/// pra buscar — por isso <see cref="Escrever"/> exige um
/// <see cref="EmpresaAdesao"/> não nulo e falha alto se o cliente não tem
/// linha, em vez de gravar um header incompleto que o banco rejeitaria
/// inteiro. Ver docs/pagamento-referencia.md §5.
/// </summary>
public static class EscritorCnab240Pagamento
{
    private const char EspacoBranco = ' ';

    /// <exception cref="ArgumentNullException"><paramref name="empresa"/> é nulo.</exception>
    /// <exception cref="ArgumentException">Nenhum lote a escrever.</exception>
    public static byte[] Escrever(RetornoPagamentoJson dados, EmpresaAdesao empresa)
    {
        ArgumentNullException.ThrowIfNull(empresa);

        if (dados.Lotes.Count == 0)
            throw new ArgumentException("Arquivo sem lote nenhum — nada a escrever.", nameof(dados));

        var banco = dados.Arquivo.Banco;

        var linhas = new List<string> { EscreverHeaderArquivo(dados.Arquivo, empresa) };

        foreach (var lote in dados.Lotes)
        {
            linhas.Add(EscreverHeaderLote(banco, lote, empresa));

            foreach (var pagamento in lote.Pagamentos)
                linhas.AddRange(EscreverDetalhe(banco, pagamento, lote.Numero, empresa));

            linhas.Add(EscreverTrailerLote(banco, lote));
        }

        linhas.Add(EscreverTrailerArquivo(banco, dados.Totais));

        // \r\n entre linhas — convenção de arquivo em disco; cada linha
        // já tem exatamente 240 posições (ver Cnab240Campos.QuebrarLinhas,
        // que aceita esta forma no sentido inverso).
        var texto = string.Join("\r\n", linhas);

        // Latin1, nunca UTF-8: o layout conta bytes, e um nome acentuado
        // em UTF-8 ocuparia duas posições e deslocaria a linha inteira.
        return Encoding.Latin1.GetBytes(texto);
    }

    private static string EscreverHeaderArquivo(ArquivoPagamento arquivo, EmpresaAdesao empresa)
    {
        var linha = NovaLinha();
        linha = EscreverControle(linha, arquivo.Banco, lote: 0, tipoRegistro: '0');
        linha = EscreverBlocoEmpresa(linha, arquivo.Empresa, arquivo.Conta, empresa);
        // Nome do Banco (G014, 103-132) — não modelado no DTO (o robô não
        // sabe o nome do próprio banco além do código); TODO(a-confirmar)
        // se precisar ser preenchido.
        linha = EscreverCodigo(linha, 143, 143, arquivo.CodigoRemessaRetorno);
        linha = EscreverData(linha, 144, 151, arquivo.DataGeracao);
        linha = EscreverHora(linha, 152, 157, arquivo.HoraGeracao);
        linha = Cnab240Campos.EscreverNumero(linha, 158, 163, arquivo.NumeroSequencialArquivo);
        linha = EscreverCodigo(linha, 164, 166, arquivo.VersaoLayout);
        // Densidade (167-171) e reservados (172-211) ficam em branco —
        // nenhum consumidor real destes campos foi identificado.
        return linha;
    }

    private static string EscreverTrailerArquivo(string? banco, TotaisArquivoPagamento totais)
    {
        var linha = NovaLinha();
        linha = EscreverControle(linha, banco, lote: 9999, tipoRegistro: '9');
        linha = Cnab240Campos.EscreverNumero(linha, 18, 23, totais.QuantidadeLotes);
        linha = Cnab240Campos.EscreverNumero(linha, 24, 29, totais.QuantidadeRegistros);
        // Qtde de Contas p/ Conciliação (30-35, G037) — só usado no
        // produto de extrato/conciliação; sempre zero aqui.
        linha = Cnab240Campos.EscreverNumero(linha, 30, 35, 0);
        return linha;
    }

    private static string EscreverHeaderLote(string? banco, LotePagamento lote, EmpresaAdesao empresa)
    {
        var linha = NovaLinha();
        linha = EscreverControle(linha, banco, lote: lote.Numero, tipoRegistro: '1');
        linha = Cnab240Campos.EscreverTexto(linha, 9, 9, "C"); // G028 — sempre crédito no retorno
        linha = EscreverCodigo(linha, 10, 11, lote.TipoServico);
        linha = EscreverCodigo(linha, 12, 13, lote.FormaLancamento);
        linha = EscreverCodigo(linha, 14, 16, lote.VersaoLayout);
        linha = EscreverBlocoEmpresa(linha, lote.Empresa, lote.Conta, empresa);
        // Informação 1 (103-142) — mensagem livre, não modelada.
        linha = Cnab240Campos.EscreverTexto(linha, 143, 172, empresa.Logradouro ?? string.Empty);
        linha = Cnab240Campos.EscreverTexto(linha, 173, 177, empresa.NumeroEndereco ?? string.Empty);
        linha = Cnab240Campos.EscreverTexto(linha, 178, 192, empresa.ComplementoEndereco ?? string.Empty);
        linha = Cnab240Campos.EscreverTexto(linha, 193, 212, empresa.Cidade ?? string.Empty);
        linha = Cnab240Campos.EscreverTexto(linha, 213, 217, empresa.Cep ?? string.Empty);
        linha = Cnab240Campos.EscreverTexto(linha, 218, 220, empresa.ComplementoCep ?? string.Empty);
        linha = Cnab240Campos.EscreverTexto(linha, 221, 222, empresa.Estado ?? string.Empty);
        // Indicativo de Forma de Pagamento (223-224, P014) — não
        // modelado; brancos aqui não invalidam o registro (campo é
        // usado só por alguns bancos como refinamento opcional).
        linha = Cnab240Campos.EscreverTexto(linha, 231, 240, lote.Ocorrencias ?? new string(EspacoBranco, 10));
        return linha;
    }

    private static string EscreverTrailerLote(string? banco, LotePagamento lote)
    {
        var linha = NovaLinha();
        linha = EscreverControle(linha, banco, lote: lote.Numero, tipoRegistro: '5');
        linha = Cnab240Campos.EscreverNumero(linha, 18, 23, lote.Totais.QuantidadeRegistros);
        linha = Cnab240Campos.EscreverValor(linha, 24, 41, lote.Totais.ValorTotal);
        // Qtde de Moeda (42-59) e Número Aviso Débito (60-65) — não
        // modelados; zerados.
        linha = Cnab240Campos.EscreverTexto(linha, 231, 240, lote.Ocorrencias ?? new string(EspacoBranco, 10));
        return linha;
    }

    private static IEnumerable<string> EscreverDetalhe(
        string? banco, DetalhePagamento pagamento, int numeroLote, EmpresaAdesao empresa)
    {
        return pagamento.Segmento switch
        {
            "A" => EscreverSegmentoAB(banco, pagamento, numeroLote),
            "J" => EscreverSegmentoJ(banco, pagamento, numeroLote, empresa),
            var s => throw new NotSupportedException(
                $"Segmento '{s}' não é escrito por este gerador — só 'A' e 'J' são produzidos por MontagemRetornoPagamento."),
        };
    }

    private static IEnumerable<string> EscreverSegmentoAB(string? banco, DetalhePagamento p, int numeroLote)
    {
        var favorecido = p.Favorecido
            ?? throw new ArgumentException($"Detalhe segmento A sem Favorecido (registro {p.NumeroRegistro}).");
        var credito = p.Credito
            ?? throw new ArgumentException($"Detalhe segmento A sem Credito (registro {p.NumeroRegistro}).");

        var a = NovaLinhaDetalhe(banco, numeroLote, p.NumeroRegistro, 'A');
        a = EscreverCodigo(a, 15, 15, p.TipoMovimento);
        a = EscreverCodigo(a, 16, 17, p.CodigoInstrucao);
        a = Cnab240Campos.EscreverTexto(a, 18, 20, favorecido.Camara ?? string.Empty);
        a = EscreverCodigo(a, 21, 23, favorecido.Banco);
        a = Cnab240Campos.EscreverTexto(a, 24, 28, favorecido.Agencia ?? string.Empty);
        a = Cnab240Campos.EscreverTexto(a, 29, 29, favorecido.DvAgencia ?? string.Empty);
        a = Cnab240Campos.EscreverTexto(a, 30, 41, favorecido.Conta ?? string.Empty);
        a = Cnab240Campos.EscreverTexto(a, 42, 42, favorecido.DvConta ?? string.Empty);
        a = Cnab240Campos.EscreverTexto(a, 43, 43, string.Empty); // DV Ag/Conta — não modelado
        a = Cnab240Campos.EscreverTexto(a, 44, 73, favorecido.Nome ?? string.Empty);
        a = Cnab240Campos.EscreverTexto(a, 74, 93, p.SeuNumero ?? string.Empty);
        a = EscreverData(a, 94, 101, credito.DataPagamento);
        a = Cnab240Campos.EscreverTexto(a, 102, 104, credito.TipoMoeda ?? "BRL");
        // Quantidade da Moeda (105-119) — não modelada, zerada.
        a = Cnab240Campos.EscreverValor(a, 120, 134, credito.ValorPagamento);
        a = Cnab240Campos.EscreverTexto(a, 135, 154, p.NossoNumero ?? string.Empty);
        a = EscreverData(a, 155, 162, credito.DataRealEfetivacao);
        a = Cnab240Campos.EscreverValor(a, 163, 177, credito.ValorRealEfetivacao);
        a = Cnab240Campos.EscreverTexto(a, 178, 217, credito.Informacao2 ?? string.Empty);
        a = Cnab240Campos.EscreverTexto(a, 231, 240, p.Ocorrencias);

        var b = NovaLinhaDetalhe(banco, numeroLote, p.NumeroRegistro + 1, 'B');
        // Identificação do Favorecido/Forma de Iniciação (15-17) — não
        // modelada, brancos.
        b = EscreverCodigo(b, 18, 18, favorecido.TipoInscricao);
        b = EscreverCodigo(b, 19, 32, favorecido.NumeroInscricao);
        // Informações complementares (33-226) e ISPB (233-240) — não
        // modeladas.

        return [a, b];
    }

    private static IEnumerable<string> EscreverSegmentoJ(
        string? banco, DetalhePagamento p, int numeroLote, EmpresaAdesao empresa)
    {
        var titulo = p.Titulo
            ?? throw new ArgumentException($"Detalhe segmento J sem Titulo (registro {p.NumeroRegistro}).");

        var j = NovaLinhaDetalhe(banco, numeroLote, p.NumeroRegistro, 'J');
        j = EscreverCodigo(j, 15, 15, p.TipoMovimento);
        j = EscreverCodigo(j, 16, 17, p.CodigoInstrucao);
        j = Cnab240Campos.EscreverTexto(j, 18, 61, titulo.CodigoBarras ?? string.Empty);
        j = Cnab240Campos.EscreverTexto(j, 62, 91, titulo.NomeBeneficiario ?? string.Empty);
        j = EscreverData(j, 92, 99, titulo.DataVencimento);
        j = Cnab240Campos.EscreverValor(j, 100, 114, titulo.ValorTitulo);
        j = Cnab240Campos.EscreverValor(j, 115, 129, titulo.ValorDesconto);
        j = Cnab240Campos.EscreverValor(j, 130, 144, titulo.ValorAcrescimos);
        j = EscreverData(j, 145, 152, titulo.DataPagamento);
        j = Cnab240Campos.EscreverValor(j, 153, 167, titulo.ValorPagamento);
        // Quantidade da Moeda (168-182) — não modelada, zerada.
        j = Cnab240Campos.EscreverTexto(j, 183, 202, p.SeuNumero ?? string.Empty);
        j = Cnab240Campos.EscreverTexto(j, 203, 222, p.NossoNumero ?? string.Empty);
        j = EscreverCodigo(j, 223, 224, titulo.CodigoMoeda ?? "09");
        j = Cnab240Campos.EscreverTexto(j, 231, 240, p.Ocorrencias);

        // O J-52 muda de forma conforme é PIX (Favorecido preenchido pela
        // montagem, ver MontagemRetornoPagamento.MontarFavorecidoPix) ou
        // título tradicional — ver docs/pagamento-referencia.md §2.4.
        var j52 = p.Favorecido is not null
            ? EscreverJ52Pix(banco, p.Favorecido, numeroLote, p.NumeroRegistro + 1, empresa)
            : EscreverJ52Titulo(banco, titulo, numeroLote, p.NumeroRegistro + 1, empresa);

        return [j, j52];
    }

    private static string EscreverJ52Titulo(
        string? banco, TituloPagamento titulo, int numeroLote, int numeroRegistro, EmpresaAdesao empresa)
    {
        var linha = NovaLinhaDetalhe(banco, numeroLote, numeroRegistro, 'J');
        linha = Cnab240Campos.EscreverTexto(linha, 18, 19, "52");
        // Pagador — a própria empresa ASA, dados institucionais.
        linha = EscreverCodigo(linha, 20, 20, TipoDocumento(empresa.Documento));
        linha = EscreverCodigo(linha, 21, 35, empresa.Documento);
        linha = Cnab240Campos.EscreverTexto(linha, 36, 75, empresa.NomeEmpresa ?? string.Empty);
        // Beneficiário — de quem é o título.
        linha = EscreverCodigo(linha, 76, 76, titulo.TipoInscricaoBeneficiario);
        linha = EscreverCodigo(linha, 77, 91, titulo.NumeroInscricaoBeneficiario);
        linha = Cnab240Campos.EscreverTexto(linha, 92, 131, titulo.NomeBeneficiario ?? string.Empty);
        // "Pagadorr" (132-187) — layout diz "responsável pela emissão do
        // título original", relevante em cenário de agregador/re-emissão
        // (Segmento J-53). TODO(a-confirmar): repetindo o beneficiário —
        // não há outra fonte de dado neste worker para diferenciar os
        // dois casos.
        linha = EscreverCodigo(linha, 132, 132, titulo.TipoInscricaoBeneficiario);
        linha = EscreverCodigo(linha, 133, 147, titulo.NumeroInscricaoBeneficiario);
        linha = Cnab240Campos.EscreverTexto(linha, 148, 187, titulo.NomeBeneficiario ?? string.Empty);
        return linha;
    }

    private static string EscreverJ52Pix(
        string? banco, FavorecidoPagamento favorecido, int numeroLote, int numeroRegistro, EmpresaAdesao empresa)
    {
        var linha = NovaLinhaDetalhe(banco, numeroLote, numeroRegistro, 'J');
        linha = Cnab240Campos.EscreverTexto(linha, 18, 19, "52");
        // Devedor — a própria empresa ASA.
        linha = EscreverCodigo(linha, 20, 20, TipoDocumento(empresa.Documento));
        linha = EscreverCodigo(linha, 21, 35, empresa.Documento);
        linha = Cnab240Campos.EscreverTexto(linha, 36, 75, empresa.NomeEmpresa ?? string.Empty);
        // Favorecido — quem recebeu o PIX.
        linha = EscreverCodigo(linha, 76, 76, favorecido.TipoInscricao);
        linha = EscreverCodigo(linha, 77, 91, favorecido.NumeroInscricao);
        linha = Cnab240Campos.EscreverTexto(linha, 92, 131, favorecido.Nome ?? string.Empty);
        linha = Cnab240Campos.EscreverTexto(linha, 132, 210, favorecido.ChavePix ?? string.Empty);
        // TXID (211-240) — não modelado neste worker.
        return linha;
    }

    /// <summary>Campos 18-102 — idênticos em posição no header de arquivo
    /// e no header de lote (tipo/número de inscrição, convênio,
    /// agência/conta com DVs, nome). Escrito uma vez só pra não duplicar
    /// as 8 linhas em cada chamador.
    ///
    /// Prioridade dos dados institucionais (convênio, conta, nome): vem
    /// de <see cref="EmpresaAdesao"/> — é a única fonte que os tem de
    /// verdade nesta modalidade de geração. Documento/tipo vêm sempre do
    /// JSON (dado transacional, correto nos dois modos de geração).</summary>
    private static string EscreverBlocoEmpresa(
        string linha, EmpresaPagamento empresaJson, ContaPagamento contaJson, EmpresaAdesao empresa)
    {
        linha = EscreverCodigo(linha, 18, 18, empresaJson.TipoInscricao);
        linha = EscreverCodigo(linha, 19, 32, empresaJson.NumeroInscricao);
        linha = Cnab240Campos.EscreverTexto(linha, 33, 52, empresa.CodigoConvenio ?? string.Empty);
        linha = Cnab240Campos.EscreverTexto(linha, 53, 57, empresa.Agencia ?? contaJson.Agencia ?? string.Empty);
        linha = Cnab240Campos.EscreverTexto(linha, 58, 58, empresa.DvAgencia ?? contaJson.DvAgencia ?? string.Empty);
        linha = Cnab240Campos.EscreverTexto(linha, 59, 70, empresa.Conta ?? contaJson.Conta ?? string.Empty);
        linha = Cnab240Campos.EscreverTexto(linha, 71, 71, empresa.DvConta ?? contaJson.DvConta ?? string.Empty);
        linha = Cnab240Campos.EscreverTexto(linha, 72, 72, empresa.DvAgenciaConta ?? contaJson.DvAgenciaConta ?? string.Empty);
        linha = Cnab240Campos.EscreverTexto(linha, 73, 102, empresa.NomeEmpresa ?? empresaJson.Nome ?? string.Empty);
        return linha;
    }

    /// <summary>Campos 1-8, comuns a todo registro: banco, lote de
    /// serviço, tipo de registro. O código do banco (posições 1-3) se
    /// repete **em toda linha do arquivo**, não só no header — por isso
    /// é resolvido uma vez em <see cref="Escrever"/> e passado adiante,
    /// em vez de cada escritor decidir de novo.</summary>
    private static string EscreverControle(string linha, string? banco, int lote, char tipoRegistro)
    {
        linha = EscreverCodigo(linha, 1, 3, banco);
        linha = Cnab240Campos.EscreverNumero(linha, 4, 7, lote);
        linha = Cnab240Campos.EscreverTexto(linha, 8, 8, tipoRegistro.ToString());
        return linha;
    }

    private static string NovaLinhaDetalhe(string? banco, int numeroLote, int numeroRegistro, char segmento)
    {
        var linha = NovaLinha();
        linha = EscreverCodigo(linha, 1, 3, banco);
        linha = Cnab240Campos.EscreverNumero(linha, 4, 7, numeroLote);
        linha = Cnab240Campos.EscreverTexto(linha, 8, 8, "3");
        linha = Cnab240Campos.EscreverNumero(linha, 9, 13, numeroRegistro);
        linha = Cnab240Campos.EscreverTexto(linha, 14, 14, segmento.ToString());
        return linha;
    }

    private static string NovaLinha() => new(EspacoBranco, Cnab240Campos.TamanhoLinha);

    /// <summary>Documento com 14 dígitos é CNPJ (tipo '2'); qualquer
    /// outro tamanho é CPF (tipo '1') — domínio G005 do layout.</summary>
    private static string TipoDocumento(string documento) => documento.Length == 14 ? "2" : "1";

    /// <summary>Campo numérico já formatado como string (ex.: "01", "98",
    /// "2") — comum nos DTOs, porque a mesma string às vezes precisa
    /// aparecer em log/JSON. Sem dígito válido, grava zero: é o caso de
    /// configuração ainda não preenchida (ex.: <c>RetornoOptions.CodigoBanco
    /// = "TODO"</c>), preferível a estourar a geração do arquivo inteiro.</summary>
    private static string EscreverCodigo(string linha, int de, int ate, string? valorNumericoTexto)
        => Cnab240Campos.EscreverNumero(linha, de, ate, long.TryParse(valorNumericoTexto, out var v) ? v : 0);

    /// <summary>Data no formato do JSON (<c>yyyy-MM-dd</c>) convertida
    /// pro formato posicional (<c>ddMMyyyy</c>). Nula/inválida vira zeros
    /// — mesma convenção que <c>Cnab240Campos.LerTrim</c> usa pra campo
    /// vazio no sentido de leitura.</summary>
    private static string EscreverData(string linha, int de, int ate, string? iso)
        => Cnab240Campos.EscreverTexto(linha, de, ate,
            DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.ToString("ddMMyyyy", CultureInfo.InvariantCulture)
                : new string('0', ate - de + 1));

    private static string EscreverHora(string linha, int de, int ate, string? hhmmss)
        => Cnab240Campos.EscreverTexto(linha, de, ate,
            DateTime.TryParseExact(hhmmss, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var h)
                ? h.ToString("HHmmss", CultureInfo.InvariantCulture)
                : new string('0', ate - de + 1));
}
