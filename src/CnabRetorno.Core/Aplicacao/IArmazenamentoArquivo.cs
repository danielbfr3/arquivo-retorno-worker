namespace CnabRetorno.Core.Aplicacao;

/// <summary>Onde o arquivo acabou parando — <see cref="Destino"/> é a
/// estratégia usada ("GestorArquivos" ou "S3") e <see cref="Referencia"/>
/// é o endereço dentro dela (a URL assinada ou a chave no bucket), pra
/// aparecer no log e permitir rastrear o objeto depois.</summary>
public sealed record ArquivoArmazenado(string Destino, string Referencia);

/// <summary>
/// Guarda o arquivo já pronto. Duas implementações convivem porque o
/// pedido original pede as duas versões:
///
/// <list type="bullet">
///   <item><b>Gestor de Arquivos</b> (padrão) — presigned URL, o caminho
///   oficial do ecossistema CASH.</item>
///   <item><b>S3 direto</b> — PutObject via SDK, para o caso de o Gestor
///   não estar disponível no ambiente.</item>
/// </list>
///
/// O <paramref name="arquivoId"/> é o <c>ArquivoID</c> da linha na tabela
/// de arquivos, o mesmo identificador em toda a cadeia. Como é
/// determinístico, reprocessar o mesmo arquivo sobrescreve o objeto em vez
/// de criar um duplicado.
/// </summary>
public interface IArmazenamentoArquivo
{
    Task<ArquivoArmazenado> ArmazenarAsync(
        Guid arquivoId, string nomeArquivo, byte[] conteudo, CancellationToken ct);
}
