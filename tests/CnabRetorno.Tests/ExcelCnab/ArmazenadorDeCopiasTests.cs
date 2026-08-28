using CnabRetorno.ExcelCnab.Worker.Armazenamento;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CnabRetorno.Tests.ExcelCnab;

/// <summary>
/// O fan-out das cópias. O ponto de ter dois destinos é não depender de
/// nenhum deles em particular — então o que estes testes travam é
/// sobretudo o comportamento quando **um** falha.
///
/// Os destinos aqui são dublês escritos à mão, não mocks de biblioteca: o
/// que está sendo testado é orquestração pura (quem é chamado, em que
/// ordem, o que acontece no erro), sem nenhuma infraestrutura real
/// envolvida.
/// </summary>
public class ArmazenadorDeCopiasTests
{
    private sealed class DestinoFake(string nome, Exception? falha = null) : IArmazenamentoArquivo
    {
        public int Chamadas { get; private set; }

        public Task<ArquivoArmazenado> ArmazenarAsync(
            Guid arquivoId, string nomeArquivo, byte[] conteudo, CancellationToken ct)
        {
            Chamadas++;
            return falha is not null
                ? Task.FromException<ArquivoArmazenado>(falha)
                : Task.FromResult(new ArquivoArmazenado(nome, $"{nome}://{arquivoId}"));
        }
    }

    private static ArmazenadorDeCopias Criar(
        IEnumerable<IArmazenamentoArquivo> destinos, bool falhaBloqueia = false)
        => new(
            destinos,
            Options.Create(new ArmazenamentoOptions { FalhaBloqueiaEnvio = falhaBloqueia }),
            NullLogger<ArmazenadorDeCopias>.Instance);

    private static Task<ResultadoCopias> Armazenar(ArmazenadorDeCopias armazenador)
        => armazenador.ArmazenarAsync(Guid.NewGuid(), "Simplificado_12345678000199.xlsx", [1, 2, 3], default);

    [Fact]
    public async Task Grava_em_todos_os_destinos_habilitados()
    {
        var gestor = new DestinoFake("GestorArquivos");
        var s3 = new DestinoFake("S3");

        var resultado = await Armazenar(Criar([gestor, s3]));

        Assert.Equal(1, gestor.Chamadas);
        Assert.Equal(1, s3.Chamadas);
        Assert.True(resultado.TudoOk);
        Assert.Equal(["GestorArquivos", "S3"], resultado.Armazenadas.Select(a => a.Destino));
    }

    [Fact]
    public async Task Um_destino_que_falha_nao_impede_o_outro()
    {
        // O motivo de existirem dois destinos: o Gestor fora do ar não
        // pode levar junto a cópia no bucket.
        var gestor = new DestinoFake("GestorArquivos", new HttpRequestException("gestor fora do ar"));
        var s3 = new DestinoFake("S3");

        var resultado = await Armazenar(Criar([gestor, s3]));

        Assert.Equal(1, s3.Chamadas);
        Assert.False(resultado.TudoOk);
        Assert.Equal("S3", Assert.Single(resultado.Armazenadas).Destino);
        Assert.Equal(nameof(DestinoFake), Assert.Single(resultado.Falhas).Destino);
    }

    [Fact]
    public async Task Por_padrao_a_falha_nao_derruba_o_processamento()
    {
        // Padrão não-bloqueante: guardar cópia é auxiliar, e um bucket
        // indisponível não impede a planilha de virar CNAB.
        var resultado = await Armazenar(Criar([new DestinoFake("S3", new InvalidOperationException("falhou"))]));

        Assert.False(resultado.TudoOk);
        Assert.Empty(resultado.Armazenadas);
    }

    [Fact]
    public async Task Com_FalhaBloqueiaEnvio_a_falha_sobe()
    {
        var armazenador = Criar(
            [new DestinoFake("GestorArquivos"), new DestinoFake("S3", new InvalidOperationException("falhou"))],
            falhaBloqueia: true);

        var erro = await Assert.ThrowsAsync<ArmazenamentoObrigatorioFalhouException>(
            () => Armazenar(armazenador));

        Assert.Contains(nameof(DestinoFake), erro.Message);
    }

    [Fact]
    public async Task Sem_destino_habilitado_e_no_op()
    {
        // Desligar Armazenamento:Habilitado faz o DI não registrar
        // destino nenhum — o processador continua chamando, e não
        // acontece nada.
        var resultado = await Armazenar(Criar([]));

        Assert.True(resultado.TudoOk);
        Assert.Empty(resultado.Armazenadas);
    }

    [Fact]
    public async Task Cancelamento_nao_e_tratado_como_falha_de_destino()
    {
        // OperationCanceledException é shutdown do worker, não um destino
        // com problema — engoli-la marcaria a cópia como "falhou" e
        // seguiria o fluxo durante um encerramento.
        var armazenador = Criar([new DestinoFake("S3", new OperationCanceledException())]);

        await Assert.ThrowsAsync<OperationCanceledException>(() => Armazenar(armazenador));
    }
}
