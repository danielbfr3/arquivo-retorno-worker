using CnabRetorno.Core.Aplicacao.Dtos;
using CnabRetorno.Core.Dominio;
using CnabRetorno.RetornoCron.Worker.Origem;
using CnabRetorno.RetornoCron.Worker.Persistencia;

namespace CnabRetorno.RetornoCron.Worker.Json;

/// <summary>
/// Converte títulos/instruções negados ou com erro (D-1) em objetos
/// <see cref="TituloConvertido"/> — mesmo shape que a API de conversão já
/// usa pra título "normal" (docs/cash-cobranca-referencia.md §2.4).
/// Substitui <c>Cnab240GeradorSegmentos</c> (gerava linhas CNAB cruas;
/// agora a pendência é injetada a nível de JSON, não de CNAB — ver
/// <see cref="MesclagemDadosConvertidos"/>).
///
/// Não numera (<see cref="TituloConvertido.NumeroRegistro"/> fica 0) —
/// quem renumera é <see cref="MesclagemDadosConvertidos"/>, numa única
/// passada sobre a lista final combinada (V + PV + pendências).
/// </summary>
public class PendenciasParaTitulosConvertidosFactory(
    CobrancaPendenciasRepository pendencias,
    ControlePendenciasReportadasDiario controlePendencias)
{
    // TODO(a-confirmar): "motivos" é literal fixo — confirmado no material
    // fornecido em 21/07/2026, não é mais a descrição do erro truncada.
    private const string MotivosFixo = "0000000000";
    private const string DirecionamentoCobrancaFixo = "0";
    private const string UsoExclusivoBancoFixo = "00";
    private const string ModalidadeComBancoCedenteFixo = "112";
    private const string CodigoOcorrenciaTituloPadrao = "03"; // Entrada Rejeitada — usado só se Titulo.CodigoOcorrencia vier vazio
    private const string CodigoOcorrenciaInstrucaoPadrao = "26"; // Instrução Rejeitada — idem

    public async Task<(IReadOnlyList<TituloConvertido> Titulos, IReadOnlyList<string> Chaves)>
        ObterPendenciasConvertidasAsync(string cnpj, DateOnly dataD1, CancellationToken ct)
    {
        var titulos = controlePendencias.FiltrarNaoReportados(
            await pendencias.ObterTitulosNegadosOuComErroAsync(cnpj, dataD1, ct));
        var instrucoes = controlePendencias.FiltrarNaoReportados(
            await pendencias.ObterInstrucoesNegadasOuComErroAsync(cnpj, dataD1, ct));

        var convertidos = new List<TituloConvertido>(titulos.Count + instrucoes.Count);
        var chaves = new List<string>(titulos.Count + instrucoes.Count);

        foreach (var p in titulos)
        {
            convertidos.Add(ConverterTitulo(p.Titulo));
            chaves.Add(ControlePendenciasReportadasDiario.ChaveTitulo(p.Titulo.TituloID));
        }

        foreach (var p in instrucoes)
        {
            convertidos.Add(ConverterInstrucao(p.Instrucao));
            chaves.Add(ControlePendenciasReportadasDiario.ChaveInstrucao(p.Instrucao.InstrucaoID));
        }

        return (convertidos, chaves);
    }

    /// <summary>Público (não só privado/internal) de propósito — mapeamento
    /// puro, testável isoladamente sem precisar de <see cref="CobrancaDbContext"/>
    /// (projeto de testes não usa mocks/EF InMemory).</summary>
    public static TituloConvertido ConverterTitulo(Titulo t) => new()
    {
        Cliente = new ClienteConvertido
        {
            ContaHeader = t.ClienteContaHeader,
            Documento = new DocumentoConvertido
            {
                Codigo = t.ClienteTipoDocumento?.ToString(),
                Inscricao = t.ClienteDocumento,
            },
        },
        Sacado = new SacadoConvertido
        {
            Documento = new DocumentoConvertido
            {
                Codigo = t.SacadorAvalistaTipoDocumento?.ToString(),
                Inscricao = t.SacadorAvalistaDocumento,
            },
            Nome = t.SacadorAvalistaNome,
        },
        NumeroCarteira = t.NumeroCarteira,
        NossoNumero = t.NossoNumero,
        CodigoBanco = t.CodigoBanco,
        Correspondente = new CorrespondenteConvertido
        {
            CodigoModalidade = t.CodigoModalidade,
            Banco = t.CodigoBanco,
            NossoNumero = t.NossoNumeroCorrespondente,
        },
        SeuNumero = t.SeuNumero,
        ValorNominal = t.ValorNominal ?? 0m,
        DataVencimento = t.DataVencimento?.ToString("yyyy-MM-dd"),
        CampoLivre = t.CampoLivre,
        CodigoIndice = t.CodigoIndice,
        Ocorrencia = new OcorrenciaConvertida
        {
            Codigo = string.IsNullOrWhiteSpace(t.CodigoOcorrencia) ? CodigoOcorrenciaTituloPadrao : t.CodigoOcorrencia,
            Descricao = t.DescricaoOcorrencia,
        },
        Motivos = MotivosFixo,
        Cobrador = new CobradorConvertido
        {
            Banco = t.RegistroRetornoCodBanco,
            Agencia = t.RegistroRetornoCodAgenciaCob,
        },
        DataOcorrencia = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
        AlegacaoSacado = AlegacaoSacadoFixa(),
        DirecionamentoCobranca = DirecionamentoCobrancaFixo,
        UsoExclusivoBanco = UsoExclusivoBancoFixo,
        ModalidadeComBancoCedente = ModalidadeComBancoCedenteFixo,
    };

    /// <summary>Público de propósito — ver <see cref="ConverterTitulo"/>.</summary>
    public static TituloConvertido ConverterInstrucao(InstrucaoComTitulo i) => new()
    {
        Cliente = new ClienteConvertido
        {
            ContaHeader = i.ClienteContaHeader,
            Documento = new DocumentoConvertido
            {
                Codigo = i.ClienteTipoDocumento?.ToString(),
                Inscricao = i.ClienteDocumento,
            },
        },
        Sacado = new SacadoConvertido
        {
            Documento = new DocumentoConvertido
            {
                Codigo = i.SacadorAvalistaTipoDocumento?.ToString(),
                Inscricao = i.SacadorAvalistaDocumento,
            },
            Nome = i.SacadorAvalistaNome,
        },
        NumeroCarteira = i.TituloNumeroCarteira, // do título casado, não da instrução — ver InstrucaoComTitulo
        NossoNumero = i.NossoNumero,
        CodigoBanco = i.CodigoBanco,
        Correspondente = new CorrespondenteConvertido
        {
            CodigoModalidade = i.CodigoModalidade,
            Banco = i.CodigoBanco,
        },
        SeuNumero = i.SeuNumero,
        ValorNominal = i.ValorNominal ?? 0m,
        DataVencimento = i.DataVencimento?.ToString("yyyy-MM-dd"),
        CampoLivre = i.CampoLivre,
        CodigoIndice = i.CodigoIndice,
        Ocorrencia = new OcorrenciaConvertida
        {
            Codigo = string.IsNullOrWhiteSpace(i.CodigoOcorrencia) ? CodigoOcorrenciaInstrucaoPadrao : i.CodigoOcorrencia,
            Descricao = i.DescricaoOcorrencia,
        },
        Motivos = MotivosFixo,
        Cobrador = new CobradorConvertido
        {
            Banco = i.RegistroRetornoCodBanco,
            Agencia = i.RegistroRetornoCodAgenciaCob,
        },
        DataOcorrencia = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
        AlegacaoSacado = AlegacaoSacadoFixa(),
        DirecionamentoCobranca = DirecionamentoCobrancaFixo,
        UsoExclusivoBanco = UsoExclusivoBancoFixo,
        ModalidadeComBancoCedente = ModalidadeComBancoCedenteFixo,
    };

    private static AlegacaoSacadoConvertida AlegacaoSacadoFixa() => new()
    {
        Codigo = "0000",
        Data = "00000000",
        Valor = 0m,
    };
}
