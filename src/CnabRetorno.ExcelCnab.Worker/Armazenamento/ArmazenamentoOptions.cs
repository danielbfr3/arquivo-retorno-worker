namespace CnabRetorno.ExcelCnab.Worker.Armazenamento;

/// <summary>
/// Configuração do armazenamento de cópias da planilha. **Uma seção só**
/// (<c>Armazenamento</c>), de propósito: desativar o recurso inteiro é
/// mudar uma chave, e removê-lo é apagar esta seção junto com a pasta
/// <c>Armazenamento/</c> — nada de configuração de storage espalhada por
/// outras seções. Ver "Como desativar / como remover" em
/// docs/regras-de-negocio.md.
/// </summary>
public class ArmazenamentoOptions
{
    public const string Secao = "Armazenamento";

    /// <summary>Chave-mestra. <c>false</c> desliga os dois destinos de uma
    /// vez, sem precisar mexer em mais nada: nenhum destino é registrado
    /// no DI e o passo vira no-op.</summary>
    public bool Habilitado { get; set; } = true;

    /// <summary>Se uma cópia falhar, o arquivo ainda vai pro conversor?
    ///
    /// Padrão <c>false</c>: guardar cópia é auxiliar ao fluxo principal, e
    /// um bucket indisponível não deve impedir a planilha de virar CNAB.
    /// A falha sai como **erro** no log justamente porque não interrompe
    /// nada — é o que impede uma cópia faltando de passar semanas
    /// despercebida.
    ///
    /// <c>true</c> inverte: sem as duas cópias, não envia (o arquivo vai
    /// pra quarentena e volta na próxima execução).</summary>
    public bool FalhaBloqueiaEnvio { get; set; } = false;

    public GestorArquivosDestino GestorArquivos { get; set; } = new();

    public S3Destino S3 { get; set; } = new();
}

/// <summary>Gestor de Arquivos — o caminho oficial do ecossistema CASH,
/// via presigned URL (docs/cash-cobranca-referencia.md §3).</summary>
public class GestorArquivosDestino
{
    public bool Habilitado { get; set; } = true;

    /// <summary>AppID com que o worker se identifica no Gestor de
    /// Arquivos — o mesmo deste fluxo.</summary>
    public string AppId { get; set; } = "cash-cobranca";

    public string BaseUrl { get; set; } = default!;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>TODO(a-confirmar): mecanismo de autenticação real não foi
    /// especificado. Placeholder de API key simples.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>Bucket S3 direto, via <c>PutObject</c>.</summary>
public class S3Destino
{
    public bool Habilitado { get; set; } = true;

    public string Bucket { get; set; } = default!;

    /// <summary>Prefixo (pasta) dentro do bucket. Vazio grava na raiz.</summary>
    public string Prefixo { get; set; } = string.Empty;

    public string Region { get; set; } = "sa-east-1";

    /// <summary>Endpoint alternativo pra LocalStack/MinIO em dev.</summary>
    public string? ServiceUrl { get; set; }
}
