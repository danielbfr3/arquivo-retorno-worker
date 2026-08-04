using Amazon;
using Amazon.S3;
using CnabRetorno.Common.Http;
using CnabRetorno.Common.Storage;
using CnabRetorno.Core.Aplicacao;
using CnabRetorno.RemessaVan.Worker;
using CnabRetorno.RemessaVan.Worker.Origem;
using CnabRetorno.RemessaVan.Worker.Persistencia;
using CnabRetorno.RemessaVan.Worker.Pipeline;
using CnabRetorno.RemessaVan.Worker.Storage;
using CnabRetorno.RemessaVan.Worker.Vans;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Options
builder.Services.Configure<CronOptions>(builder.Configuration.GetSection(CronOptions.Secao));
builder.Services.Configure<OrigemOptions>(builder.Configuration.GetSection(OrigemOptions.Secao));
builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection(PipelineOptions.Secao));
builder.Services.Configure<VansOptions>(builder.Configuration.GetSection(VansOptions.Secao));
builder.Services.Configure<NomenclaturaOptions>(builder.Configuration.GetSection(NomenclaturaOptions.Secao));
builder.Services.Configure<RegistroArquivoOptions>(builder.Configuration.GetSection(RegistroArquivoOptions.Secao));
builder.Services.Configure<ArmazenamentoOptions>(builder.Configuration.GetSection(ArmazenamentoOptions.Secao));
builder.Services.Configure<GestorArquivoOptions>(builder.Configuration.GetSection(GestorArquivoOptions.Secao));
builder.Services.Configure<ApiClientOptions>(
    "GestorArquivosApi", builder.Configuration.GetSection("GestorArquivosApi"));

builder.Services.AddSingleton(TimeProvider.System);

// Persistência — base CASH_COBRANCA (SQL Server, de outro time). Escreve
// só em Cobranca.Arquivo; lê Cobranca.Parametro pro ContaHeader.
builder.Services.AddDbContext<CobrancaDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Cobranca")));
builder.Services.AddScoped<ArquivoRepository>();
builder.Services.AddScoped<ParametroClienteRepository>();

// Pasta de origem (compartilhamento SMB em produção) e máscaras das VANs.
builder.Services.AddScoped<PastaOrigemRemessa>();
builder.Services.AddSingleton<CatalogoMascarasVan>(); // regex compilada uma vez, reusada
builder.Services.AddSingleton<NomeArquivoAsa>();

// API Gestor Arquivo — presigned URLs, nunca S3 direto
// (docs/cash-cobranca-referencia.md §5.5).
builder.Services.AddHttpClient<IGestorArquivosApiClient, GestorArquivosApiClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptionsMonitor<ApiClientOptions>>().Get("GestorArquivosApi");
    http.BaseAddress = new Uri(opt.BaseUrl);
    http.Timeout = opt.Timeout;
    if (!string.IsNullOrWhiteSpace(opt.ApiKey))
        http.DefaultRequestHeaders.Add("X-Api-Key", opt.ApiKey);
}).AddStandardResilienceHandler();

// Storage: as duas versões pedidas convivem, escolhidas por Storage:Modo.
// O PUT na URL assinada vai num HttpClient próprio (sem BaseAddress: a
// URL assinada é absoluta e aponta pro S3, não pra API).
var modoStorage = builder.Configuration.GetValue<string>($"{ArmazenamentoOptions.Secao}:Modo") ?? "GestorArquivos";

if (string.Equals(modoStorage, "S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IAmazonS3>(sp =>
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
    builder.Services.AddScoped<IArmazenamentoArquivo, S3Storage>();
}
else
{
    builder.Services.AddHttpClient<GestorArquivoStorage>().AddStandardResilienceHandler();
    builder.Services.AddScoped<IArmazenamentoArquivo>(sp => sp.GetRequiredService<GestorArquivoStorage>());
}

// Pipeline.
builder.Services.AddScoped<ProcessadorArquivoRemessaService>();
builder.Services.AddScoped<IngerirRemessasVanPipeline>();
builder.Services.AddHostedService<RemessaVanWorker>();

var host = builder.Build();
await host.RunAsync();
