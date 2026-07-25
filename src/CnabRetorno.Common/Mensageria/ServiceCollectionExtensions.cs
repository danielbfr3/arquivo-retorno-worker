using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CnabRetorno.Common.Mensageria;

public static class ServiceCollectionExtensions
{
    /// <summary>Registra o <see cref="IAmazonSQS"/> singleton — client
    /// caro de abrir, deve viver o processo inteiro. Chamar uma vez por
    /// worker, na Program.cs.</summary>
    public static IServiceCollection AddCnabSqsConnection(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SqsOptions>(configuration.GetSection(SqsOptions.Secao));

        services.AddSingleton<IAmazonSQS>(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<SqsOptions>>().Value;
            var config = new AmazonSQSConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(opt.Region) };
            if (!string.IsNullOrWhiteSpace(opt.ServiceUrl)) config.ServiceURL = opt.ServiceUrl;

            return string.IsNullOrWhiteSpace(opt.AccessKeyId)
                ? new AmazonSQSClient(config) // produção: IAM role / variáveis de ambiente
                : new AmazonSQSClient(opt.AccessKeyId, opt.SecretAccessKey, config); // dev/LocalStack
        });

        return services;
    }

    /// <summary>Registra o consumidor de uma fila SQS, delegando cada
    /// mensagem pro <see cref="IMessageService{TMessage}"/> resolvido em
    /// escopo próprio. <typeparamref name="THandler"/> é a implementação
    /// concreta do handler (o serviço de aplicação do robô).</summary>
    public static IServiceCollection AddCnabSqsMessageConsumer<TMessage, THandler>(
        this IServiceCollection services, SqsTopologia topologia)
        where THandler : class, IMessageService<TMessage>
    {
        services.AddScoped<IMessageService<TMessage>, THandler>();
        services.AddSingleton<IHostedService>(sp =>
            new SqsConsumerHostedService<TMessage>(
                sp.GetRequiredService<IAmazonSQS>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                topologia,
                sp.GetRequiredService<ILogger<SqsConsumerHostedService<TMessage>>>()));
        return services;
    }
}
