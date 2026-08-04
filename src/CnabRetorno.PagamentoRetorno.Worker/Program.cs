using CnabRetorno.Common.Http;
using CnabRetorno.Common.Storage;
using CnabRetorno.Core.Aplicacao;
using CnabRetorno.PagamentoRetorno.Worker;
using CnabRetorno.PagamentoRetorno.Worker.Agendamento;
using CnabRetorno.PagamentoRetorno.Worker.Http;
using CnabRetorno.PagamentoRetorno.Worker.Json;
using CnabRetorno.PagamentoRetorno.Worker.Persistencia;
using CnabRetorno.PagamentoRetorno.Worker.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Options
builder.Services.Configure<JanelaOptions>(builder.Configuration.GetSection(JanelaOptions.Secao));
builder.Services.Configure<RetornoOptions>(builder.Configuration.GetSection(RetornoOptions.Secao));
builder.Services.Configure<ConversaoOptions>(builder.Configuration.GetSection(ConversaoOptions.Secao));
builder.Services.Configure<RegistroArquivoOptions>(builder.Configuration.GetSection(RegistroArquivoOptions.Secao));
builder.Services.Configure<GestorArquivoOptions>(builder.Configuration.GetSection(GestorArquivoOptions.Secao));
builder.Services.Configure<ApiClientOptions>(
    "LayoutConversaoApi", builder.Configuration.GetSection("LayoutConversaoApi"));
builder.Services.Configure<ApiClientOptions>(
    "GestorArquivosApi", builder.Configuration.GetSection("GestorArquivosApi"));

builder.Services.AddSingleton(TimeProvider.System);

// Persistência — base ASA_CASH_PAGAMENTO (SQL Server, de outro time).
// Lê as 5 duplas de meio de pagamento; escreve em Pagamento.Arquivo e na
// tabela de controle de janela (esta última, criada por este worker).
builder.Services.AddDbContext<PagamentoDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Pagamento")));
builder.Services.AddScoped<MovimentacoesRepository>();
builder.Services.AddScoped<ArquivoRepository>();
builder.Services.AddScoped<SequencialArquivoRepository>();
builder.Services.AddScoped<ControleJanelaRepository>();

// Agendamento e montagem do JSON — lógica pura, sem I/O.
builder.Services.AddSingleton<CalculadoraJanelas>();
builder.Services.AddScoped<MontagemRetornoPagamento>();

// API de conversão (síncrona: JSON entra, CNAB volta na mesma resposta).
builder.Services.AddHttpClient<ILayoutConversaoApiClient, LayoutConversaoApiClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptionsMonitor<ApiClientOptions>>().Get("LayoutConversaoApi");
    http.BaseAddress = new Uri(opt.BaseUrl);
    http.Timeout = opt.Timeout;
    if (!string.IsNullOrWhiteSpace(opt.ApiKey))
        http.DefaultRequestHeaders.Add("X-Api-Key", opt.ApiKey);
}).AddStandardResilienceHandler();

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

// O PUT vai num HttpClient próprio: a URL assinada é absoluta e aponta
// pro S3, não pra API — um BaseAddress atrapalharia.
builder.Services.AddHttpClient<GestorArquivoStorage>().AddStandardResilienceHandler();
builder.Services.AddScoped<IArmazenamentoArquivo>(sp => sp.GetRequiredService<GestorArquivoStorage>());

// Pipeline.
builder.Services.AddScoped<ProcessadorRetornoPagamentoService>();
builder.Services.AddScoped<GerarRetornosPagamentoPipeline>();
builder.Services.AddHostedService<PagamentoRetornoWorker>();

var host = builder.Build();
await host.RunAsync();
