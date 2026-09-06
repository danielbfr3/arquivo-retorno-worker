using CnabRetorno.Common.Http;
using CnabRetorno.Core.Aplicacao;
using CnabRetorno.ExcelCnab.Worker;
using CnabRetorno.ExcelCnab.Worker.Http;
using CnabRetorno.ExcelCnab.Worker.Origem;
using CnabRetorno.ExcelCnab.Worker.Persistencia;
using CnabRetorno.ExcelCnab.Worker.Pipeline;
using CnabRetorno.ExcelCnab.Worker.Planilha;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Options
builder.Services.Configure<CronOptions>(builder.Configuration.GetSection(CronOptions.Secao));
builder.Services.Configure<OrigemOptions>(builder.Configuration.GetSection(OrigemOptions.Secao));
builder.Services.Configure<NomenclaturaOptions>(builder.Configuration.GetSection(NomenclaturaOptions.Secao));
builder.Services.Configure<PreenchimentoOptions>(builder.Configuration.GetSection(PreenchimentoOptions.Secao));
builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection(PipelineOptions.Secao));
builder.Services.Configure<RegistroArquivoOptions>(builder.Configuration.GetSection(RegistroArquivoOptions.Secao));
builder.Services.Configure<ConversaoOptions>(builder.Configuration.GetSection(ConversaoOptions.Secao));
builder.Services.Configure<ApiClientOptions>(
    "LayoutConversaoApi", builder.Configuration.GetSection("LayoutConversaoApi"));

builder.Services.AddSingleton(TimeProvider.System);

// Persistência — base de outro time, nenhuma migration daqui.
// CASH_COBRANCA: escreve a linha do arquivo e lê Cobranca.DocumentoDados
// (dados de preenchimento, incluindo a razão social — ver
// DocumentoDados.ChaveRazaoSocial).
builder.Services.AddDbContext<CobrancaDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Cobranca")));
builder.Services.AddScoped<ArquivoRepository>();
builder.Services.AddScoped<DocumentoDadosRepository>();

// Pasta de origem (compartilhamento SMB em hml/prd) e leitura do nome.
builder.Services.AddScoped<PastaOrigemExcel>();
builder.Services.AddSingleton<NomeArquivoSimplificado>(); // regex compilada uma vez, reusada

// Preenchimento da planilha (ClosedXML) — sem estado mutável, singleton.
builder.Services.AddSingleton<PreenchedorPlanilhaExcel>();

// Conversor de layout — a planilha (já preenchida) vai no multipart, sem
// passar por storage intermediário.
builder.Services.AddHttpClient<ILayoutConversaoApiClient, LayoutConversaoApiClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptionsMonitor<ApiClientOptions>>().Get("LayoutConversaoApi");
    http.BaseAddress = new Uri(opt.BaseUrl);
    http.Timeout = opt.Timeout;
    if (!string.IsNullOrWhiteSpace(opt.ApiKey))
        http.DefaultRequestHeaders.Add("X-Api-Key", opt.ApiKey);
}).AddStandardResilienceHandler();

// Pipeline.
builder.Services.AddScoped<ProcessadorArquivoExcelService>();
builder.Services.AddScoped<EnviarPlanilhasPipeline>();
builder.Services.AddHostedService<ExcelCnabWorker>();

var host = builder.Build();
await host.RunAsync();
