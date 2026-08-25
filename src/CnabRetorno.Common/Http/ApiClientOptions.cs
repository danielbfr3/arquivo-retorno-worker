namespace CnabRetorno.Common.Http;

/// <summary>Config genérica de client HTTP pra uma API externa — usar uma
/// seção própria por API (hoje só "LayoutConversaoApi").</summary>
public class ApiClientOptions
{
    public string BaseUrl { get; set; } = default!;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>TODO(a-confirmar): mecanismo de autenticação real das APIs
    /// (API key, OAuth client-credentials, mTLS?) não foi especificado no
    /// documento de tarefa. Placeholder de API key simples por enquanto.</summary>
    public string? ApiKey { get; set; }
}
