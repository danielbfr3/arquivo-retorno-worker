using Amazon;
using Amazon.S3;
using Microsoft.Extensions.Options;

namespace CnabRetorno.ExcelCnab.Worker.Armazenamento;

/// <summary>
/// Todo o registro no DI do armazenamento, num lugar só — é a **única
/// linha** que o <c>Program.cs</c> precisa ter sobre o assunto. Remover o
/// recurso é apagar a pasta <c>Armazenamento/</c>, esta chamada, a chamada
/// no processador, a seção do <c>appsettings.json</c> e o
/// <c>PackageReference</c> do AWSSDK.S3 — nada mais.
/// </summary>
public static class ArmazenamentoServiceCollectionExtensions
{
    public static IServiceCollection AdicionarArmazenamento(
        this IServiceCollection servicos, IConfiguration configuracao)
    {
        var secao = configuracao.GetSection(ArmazenamentoOptions.Secao);
        servicos.Configure<ArmazenamentoOptions>(secao);

        // O fan-out existe mesmo desligado: assim o processador tem sempre
        // a mesma forma, e desativar não muda o código de lugar nenhum —
        // sem destino registrado, a chamada é um laço sobre lista vazia.
        servicos.AddScoped<ArmazenadorDeCopias>();

        var opcoes = secao.Get<ArmazenamentoOptions>() ?? new ArmazenamentoOptions();
        if (!opcoes.Habilitado) return servicos;

        if (opcoes.GestorArquivos.Habilitado)
        {
            // Client da API: BaseAddress + credencial, só pro presign.
            servicos.AddHttpClient<GestorArquivosApiClient>((sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ArmazenamentoOptions>>().Value.GestorArquivos;
                http.BaseAddress = new Uri(opt.BaseUrl);
                http.Timeout = opt.Timeout;
                if (!string.IsNullOrWhiteSpace(opt.ApiKey))
                    http.DefaultRequestHeaders.Add("X-Api-Key", opt.ApiKey);
            }).AddStandardResilienceHandler();

            // Client do PUT: sem BaseAddress e **sem** a credencial da API
            // — a URL assinada é absoluta e aponta pro S3, que não tem
            // nada a ver com a chave da API do gestor.
            servicos.AddHttpClient<GestorArquivoStorage>().AddStandardResilienceHandler();
            servicos.AddScoped<IArmazenamentoArquivo>(sp =>
                sp.GetRequiredService<GestorArquivoStorage>());
        }

        if (opcoes.S3.Habilitado)
        {
            servicos.AddSingleton<IAmazonS3>(sp =>
            {
                var opt = sp.GetRequiredService<IOptions<ArmazenamentoOptions>>().Value.S3;
                var config = new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(opt.Region) };
                if (!string.IsNullOrWhiteSpace(opt.ServiceUrl))
                {
                    config.ServiceURL = opt.ServiceUrl;
                    config.ForcePathStyle = true; // LocalStack/MinIO não suportam virtual-hosted style
                }
                return new AmazonS3Client(config);
            });
            servicos.AddScoped<IArmazenamentoArquivo, S3Storage>();
        }

        return servicos;
    }
}
