using CnabRetorno.Common.Http;
using CnabRetorno.Core.Aplicacao;
using CnabRetorno.RetornoCron.Worker;
using CnabRetorno.RetornoCron.Worker.Http;
using CnabRetorno.RetornoCron.Worker.Json;
using CnabRetorno.RetornoCron.Worker.Origem;
using CnabRetorno.RetornoCron.Worker.Persistencia;
using CnabRetorno.RetornoCron.Worker.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Options
builder.Services.Configure<CronOptions>(builder.Configuration.GetSection(CronOptions.Secao));
builder.Services.Configure<OrigemOptions>(builder.Configuration.GetSection(OrigemOptions.Secao));
builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection(PipelineOptions.Secao));
builder.Services.Configure<ApiClientOptions>(
    "LayoutConversaoApi", builder.Configuration.GetSection("LayoutConversaoApi"));

// Persistência — base CASH_COBRANCA (SQL Server, existente). Sem banco
// próprio: só leitura das pendências + o registro do arquivo de retorno em
// Cobranca.Arquivo (ver docs/regras-de-negocio.md). Idempotência de
// reprocessamento continua via ControleIdempotenciaDiario (arquivo, não
// banco).
builder.Services.AddDbContext<CobrancaDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Cobranca")));
builder.Services.AddScoped<CobrancaPendenciasRepository>();
builder.Services.AddScoped<ArquivoRepository>();
builder.Services.AddScoped<SequencialArquivoRepository>();

// Origem (pasta X).
builder.Services.AddScoped<PastaOrigemArquivosRetorno>();

// Idempotência diária — estado em memória compartilhado pelo processo
// inteiro (não Scoped: precisa ser a mesma instância entre arquivos
// processados em paralelo dentro da mesma execução).
builder.Services.AddSingleton<ControleIdempotenciaDiario>();

// Controle de pendências já reportadas hoje + lock por CNPJ — também
// Singleton, mesmo motivo (ver docs/riscos-conhecidos.md, item 1).
builder.Services.AddSingleton<ControlePendenciasReportadasDiario>();

// Mesclagem de DadosConvertidos (V + PV + pendências do CASH_COBRANCA, a
// nível de JSON) e mapeamento pendência -> TituloConvertido — lógica pura,
// sem dependências externas.
builder.Services.AddScoped<MesclagemDadosConvertidos>();
builder.Services.AddScoped<PendenciasParaTitulosConvertidosFactory>();

// API de conversão CNAB<->JSON — único cliente HTTP que conhece o formato
// real da API (ver docs/evoluindo-com-libs-externas.md).
builder.Services.AddHttpClient<ILayoutConversaoApiClient, LayoutConversaoApiClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptionsMonitor<ApiClientOptions>>().Get("LayoutConversaoApi");
    http.BaseAddress = new Uri(opt.BaseUrl);
    http.Timeout = opt.Timeout;
    if (!string.IsNullOrWhiteSpace(opt.ApiKey))
        http.DefaultRequestHeaders.Add("X-Api-Key", opt.ApiKey);
});

// Pipeline.
builder.Services.AddScoped<ProcessadorArquivoRetornoService>();
builder.Services.AddScoped<ProcessarArquivosVePvPipeline>();
builder.Services.AddScoped<ProcessadorClientesSemArquivoService>();
builder.Services.AddHostedService<RetornoCronWorker>();

var host = builder.Build();
await host.RunAsync();
