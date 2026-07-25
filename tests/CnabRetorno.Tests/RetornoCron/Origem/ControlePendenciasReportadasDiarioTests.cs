using CnabRetorno.Core.Dominio;
using CnabRetorno.RetornoCron.Worker.Origem;
using CnabRetorno.RetornoCron.Worker.Persistencia;
using Microsoft.Extensions.Options;
using Xunit;

namespace CnabRetorno.Tests.RetornoCron.Origem;

public class ControlePendenciasReportadasDiarioTests
{
    private static ControlePendenciasReportadasDiario NovoControle(string pasta)
        => new(Options.Create(new OrigemOptions { Pasta = pasta }));

    private static string NovaPastaTemporaria()
        => Directory.CreateTempSubdirectory("cnab-retorno-tests-").FullName;

    private static Titulo TituloDeExemplo(Guid? id = null) => new()
    {
        TituloID = id ?? Guid.NewGuid(),
        ClienteDocumento = "12345678000199",
        CodigoStatus = -1,
        DataAtualizacao = DateTime.UtcNow,
    };

    private static InstrucaoComTitulo InstrucaoDeExemplo(Guid? id = null) => new()
    {
        InstrucaoID = id ?? Guid.NewGuid(),
        ClienteDocumento = "12345678000199",
        CodigoStatus = -1,
        DataAtualizacao = DateTime.UtcNow,
    };

    [Fact]
    public void JaReportada_deve_ser_falso_pra_chave_nunca_registrada()
    {
        var controle = NovoControle(NovaPastaTemporaria());

        Assert.False(controle.JaReportada("T:abc"));
    }

    [Fact]
    public void RegistrarReportadas_deve_marcar_a_chave_como_reportada()
    {
        var controle = NovoControle(NovaPastaTemporaria());

        controle.RegistrarReportadas(["T:abc"]);

        Assert.True(controle.JaReportada("T:abc"));
    }

    [Fact]
    public void FiltrarNaoReportados_titulo_deve_excluir_ja_reportado()
    {
        var controle = NovoControle(NovaPastaTemporaria());
        var id = Guid.NewGuid();
        var pendente = new TituloPendente(TituloDeExemplo(id), null);
        controle.RegistrarReportadas([ControlePendenciasReportadasDiario.ChaveTitulo(id)]);

        var resultado = controle.FiltrarNaoReportados(new List<TituloPendente> { pendente });

        Assert.Empty(resultado);
    }

    [Fact]
    public void FiltrarNaoReportados_titulo_deve_manter_nao_reportado()
    {
        var controle = NovoControle(NovaPastaTemporaria());
        var pendente = new TituloPendente(TituloDeExemplo(), null);

        var resultado = controle.FiltrarNaoReportados(new List<TituloPendente> { pendente });

        Assert.Single(resultado);
    }

    [Fact]
    public void FiltrarNaoReportados_instrucao_deve_excluir_ja_reportada()
    {
        var controle = NovoControle(NovaPastaTemporaria());
        var id = Guid.NewGuid();
        var pendente = new InstrucaoPendente(InstrucaoDeExemplo(id), null);
        controle.RegistrarReportadas([ControlePendenciasReportadasDiario.ChaveInstrucao(id)]);

        var resultado = controle.FiltrarNaoReportados(new List<InstrucaoPendente> { pendente });

        Assert.Empty(resultado);
    }

    [Fact]
    public void FiltrarNaoReportados_instrucao_deve_manter_nao_reportada()
    {
        var controle = NovoControle(NovaPastaTemporaria());
        var pendente = new InstrucaoPendente(InstrucaoDeExemplo(), null);

        var resultado = controle.FiltrarNaoReportados(new List<InstrucaoPendente> { pendente });

        Assert.Single(resultado);
    }

    [Fact]
    public void Estado_deve_resetar_quando_arquivo_e_de_um_dia_anterior()
    {
        var pasta = NovaPastaTemporaria();
        var controleOntem = NovoControle(pasta);
        controleOntem.RegistrarReportadas(["T:antiga"]);

        // "Envelhece" o arquivo persistido pra simular execução de ontem,
        // sem depender do shape exato do JSON — só troca a data dentro do
        // arquivo que a própria classe acabou de escrever.
        var caminhoArquivo = Path.Combine(pasta, ".pendencias-reportadas-hoje.json");
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var ontem = hoje.AddDays(-1);
        var conteudo = File.ReadAllText(caminhoArquivo)
            .Replace(hoje.ToString("yyyy-MM-dd"), ontem.ToString("yyyy-MM-dd"));
        File.WriteAllText(caminhoArquivo, conteudo);

        var controleHoje = NovoControle(pasta);

        Assert.False(controleHoje.JaReportada("T:antiga"));
    }

    [Fact]
    public async Task RegistrarReportadas_deve_ser_thread_safe_sob_concorrencia_real()
    {
        var pasta = NovaPastaTemporaria();
        var controle = NovoControle(pasta);
        var chaves = Enumerable.Range(0, 200).Select(i => $"T:{i}").ToList();

        await Task.WhenAll(chaves.Select(chave => Task.Run(() => controle.RegistrarReportadas([chave]))));

        Assert.All(chaves, chave => Assert.True(controle.JaReportada(chave)));

        // Reconstrói a partir do arquivo persistido — nada deve ter sido
        // perdido por escrita concorrente (lock interno + escrita atômica).
        var controleRecarregado = NovoControle(pasta);
        Assert.All(chaves, chave => Assert.True(controleRecarregado.JaReportada(chave)));
    }

    [Fact]
    public async Task AdquirirLockCnpjAsync_mesmo_cnpj_nunca_deve_sobrepor()
    {
        var controle = NovoControle(NovaPastaTemporaria());
        var emUso = false;
        var sobreposicaoDetectada = false;

        async Task TrabalhoAsync()
        {
            await using var lockCnpj = await controle.AdquirirLockCnpjAsync("11122233000144", CancellationToken.None);
            if (emUso) sobreposicaoDetectada = true;
            emUso = true;
            await Task.Delay(15);
            emUso = false;
        }

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => TrabalhoAsync()));

        Assert.False(sobreposicaoDetectada);
    }

    [Fact]
    public async Task AdquirirLockCnpjAsync_cnpjs_diferentes_nao_devem_serializar()
    {
        var controle = NovoControle(NovaPastaTemporaria());

        async Task TrabalhoAsync(string cnpj)
        {
            await using var lockCnpj = await controle.AdquirirLockCnpjAsync(cnpj, CancellationToken.None);
            await Task.Delay(150);
        }

        var inicio = DateTime.UtcNow;
        await Task.WhenAll(TrabalhoAsync("11111111000191"), TrabalhoAsync("22222222000172"));
        var duracao = DateTime.UtcNow - inicio;

        // Serializado levaria >= 300ms; em paralelo, ~150ms. Margem
        // generosa pra não flakear em CI mais lento.
        Assert.True(duracao < TimeSpan.FromMilliseconds(280), $"Levou {duracao} — esperava < 280ms (paralelo)");
    }
}
