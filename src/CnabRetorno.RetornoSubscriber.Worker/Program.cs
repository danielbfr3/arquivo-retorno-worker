using CnabRetorno.Common.Http;
using CnabRetorno.Common.Mensageria;
using CnabRetorno.Core.Aplicacao;
using CnabRetorno.RetornoSubscriber.Worker.Http;
using CnabRetorno.RetornoSubscriber.Worker.Mensageria;
using CnabRetorno.RetornoSubscriber.Worker.Persistencia;
using CnabRetorno.RetornoSubscriber.Worker.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

var builder = Host.CreateApplicationBuilder(args);

// Options
builder.Services.Configure<GestorArquivoOptions>(
    builder.Configuration.GetSection(GestorArquivoOptions.Secao));
builder.Services.Configure<ApiClientOptions>(
    "GestorArquivosApi", builder.Configuration.GetSection("GestorArquivosApi"));

// Persistência — só Cobranca.Arquivo: busca a linha pelo ID que vem na
// mensagem SQS (registrada pelo Robô 1) e avança status/etapa no fim.
builder.Services.AddDbContext<CobrancaDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Cobranca")));
builder.Services.AddScoped<ArquivoRepository>();

// Gestor de Arquivos — presigned URLs, nunca acesso direto ao S3 (ver
// docs/cash-cobranca-referencia.md §3, §5.5). Resiliência conforme §4.2
// do mesmo documento (config real do client ArquivoApiClient); "TimeoutSeconds"
// do documento é tratado aqui como orçamento TOTAL da chamada (com retries),
// não por tentativa — TODO(a-confirmar): o documento não distingue os dois.
builder.Services.AddHttpClient<IGestorArquivosApiClient, GestorArquivosApiClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptionsMonitor<ApiClientOptions>>().Get("GestorArquivosApi");
    if (!string.IsNullOrWhiteSpace(opt.BaseUrl)) http.BaseAddress = new Uri(opt.BaseUrl);
})
.AddStandardResilienceHandler(options =>
{
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.BackoffType = DelayBackoffType.Constant;
    options.Retry.Delay = TimeSpan.FromSeconds(2);
    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    options.CircuitBreaker.MinimumThroughput = 5;
});

// HttpClient genérico pra baixar o arquivo CNAB gerado (passo 3) — sem
// base address fixa, a URL vem completa na mensagem.
builder.Services.AddHttpClient("Download");
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Download"));

// HttpClient genérico pro PUT na URL assinada de upload (passo 7+8) — vai
// pro host do S3/Gestor Arquivo, não pra BaseAddress do client acima.
builder.Services.AddHttpClient("Upload");
builder.Services.AddScoped<GestorArquivoStorage>(sp => new GestorArquivoStorage(
    sp.GetRequiredService<IGestorArquivosApiClient>(),
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Upload"),
    sp.GetRequiredService<IOptions<GestorArquivoOptions>>()));

builder.Services.AddScoped<ProcessarConclusaoConversaoService>();

// Mensageria — consumidor SQS da conclusão da conversão assíncrona (ver
// docs/cash-cobranca-referencia.md §2.4). TODO(a-confirmar): nome real da
// fila — placeholder abaixo.
builder.Services.AddCnabSqsConnection(builder.Configuration);
builder.Services.AddCnabSqsMessageConsumer<ConversaoConcluidaMessage, ProcessarConclusaoConversaoService>(
    new SqsTopologia(NomeFila: "cobranca-retorno-conversao-concluida"));

var host = builder.Build();
await host.RunAsync();
