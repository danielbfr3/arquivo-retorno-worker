using Amazon;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.Options;

namespace CnabRetorno.ExcelCnab.Worker.Notificacao;

/// <summary>
/// Todo o registro no DI do aviso de conclusão, num lugar só — é a
/// **única linha** que o <c>Program.cs</c> precisa ter sobre o assunto.
/// Mesmo desenho do armazenamento, pelo mesmo motivo: o recurso sai
/// apagando a pasta <c>Notificacao/</c>, esta chamada, a chamada no
/// processador, a seção do <c>appsettings.json</c> e o
/// <c>PackageReference</c> do SDK.
/// </summary>
public static class NotificacaoServiceCollectionExtensions
{
    public static IServiceCollection AdicionarNotificacao(
        this IServiceCollection servicos, IConfiguration configuracao)
    {
        var secao = configuracao.GetSection(NotificacaoOptions.Secao);
        servicos.Configure<NotificacaoOptions>(secao);

        var opcoes = secao.Get<NotificacaoOptions>() ?? new NotificacaoOptions();
        if (!opcoes.Habilitado)
        {
            // Null Object: o processador continua chamando o notificador,
            // e não acontece nada. Nenhum if de configuração vaza pro
            // fluxo.
            servicos.AddSingleton<INotificadorConclusao, NotificadorDesligado>();
            return servicos;
        }

        servicos.AddSingleton<IAmazonSimpleNotificationService>(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<NotificacaoOptions>>().Value;
            var config = new AmazonSimpleNotificationServiceConfig
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(opt.Region),
            };
            if (!string.IsNullOrWhiteSpace(opt.ServiceUrl))
                config.ServiceURL = opt.ServiceUrl; // LocalStack em dev
            return new AmazonSimpleNotificationServiceClient(config);
        });

        servicos.AddScoped<INotificadorConclusao, SnsNotificadorConclusao>();
        return servicos;
    }
}
