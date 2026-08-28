using CnabRetorno.Common.Http;
using CnabRetorno.Core.Aplicacao;
using CnabRetorno.ExcelCnab.Worker;
using CnabRetorno.ExcelCnab.Worker.Armazenamento;
using CnabRetorno.ExcelCnab.Worker.Http;
using CnabRetorno.ExcelCnab.Worker.Notificacao;
using CnabRetorno.ExcelCnab.Worker.Origem;
using CnabRetorno.ExcelCnab.Worker.Persistencia;
using CnabRetorno.ExcelCnab.Worker.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Options
builder.Services.Configure<CronOptions>(builder.Configuration.GetSection(CronOptions.Secao));
builder.Services.Configure<OrigemOptions>(builder.Configuration.GetSection(OrigemOptions.Secao));
builder.Services.Configure<NomenclaturaOptions>(builder.Configuration.GetSection(NomenclaturaOptions.Secao));
builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection(PipelineOptions.Secao));
builder.Services.Configure<RegistroArquivoOptions>(builder.Configuration.GetSection(RegistroArquivoOptions.Secao));
builder.Services.Configure<ConversaoOptions>(builder.Configuration.GetSection(ConversaoOptions.Secao));
builder.Services.Configure<ApiClientOptions>(
    "LayoutConversaoApi", builder.Configuration.GetSection("LayoutConversaoApi"));

builder.Services.AddSingleton(TimeProvider.System);

// Persistência — duas bases de outros times, nenhuma migration daqui.
// CASH_COBRANCA: escreve a linha do arquivo. Adesão: lê a razão social.
builder.Services.AddDbContext<CobrancaDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Cobranca")));
builder.Services.AddDbContext<AdesaoDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Adesao")));
builder.Services.AddScoped<ArquivoRepository>();
builder.Services.AddScoped<EmpresaAdesaoRepository>();

// Pasta de origem (compartilhamento SMB em hml/prd) e leitura do nome.
builder.Services.AddScoped<PastaOrigemExcel>();
builder.Services.AddSingleton<NomeArquivoSimplificado>(); // regex compilada uma vez, reusada

// Conversor de layout — a planilha vai no multipart, sem passar por
// storage intermediário.
builder.Services.AddHttpClient<ILayoutConversaoApiClient, LayoutConversaoApiClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptionsMonitor<ApiClientOptions>>().Get("LayoutConversaoApi");
    http.BaseAddress = new Uri(opt.BaseUrl);
    http.Timeout = opt.Timeout;
    if (!string.IsNullOrWhiteSpace(opt.ApiKey))
        http.DefaultRequestHeaders.Add("X-Api-Key", opt.ApiKey);
}).AddStandardResilienceHandler();

// Armazenamento das cópias (Gestor de Arquivos + bucket S3, os dois ao
// mesmo tempo). Recurso destacável: esta linha é tudo que o host sabe
// dele — ver Armazenamento/ArmazenamentoServiceCollectionExtensions.cs.
builder.Services.AdicionarArmazenamento(builder.Configuration);

// Aviso de conclusão no tópico SNS. Mesmo desenho destacável do
// armazenamento — ver Notificacao/NotificacaoServiceCollectionExtensions.cs.
builder.Services.AdicionarNotificacao(builder.Configuration);

// Pipeline.
builder.Services.AddScoped<ProcessadorArquivoExcelService>();
builder.Services.AddScoped<EnviarPlanilhasPipeline>();
builder.Services.AddHostedService<ExcelCnabWorker>();

var host = builder.Build();
await host.RunAsync();
