namespace CnabRetorno.ExcelCnab.Worker.Armazenamento;

/// <summary>Onde a cópia acabou parando — <see cref="Destino"/> é a
/// estratégia usada ("GestorArquivos" ou "S3") e <see cref="Referencia"/>
/// é o endereço dentro dela (o id no gestor ou a chave no bucket), pra
/// aparecer no log e permitir rastrear o objeto depois.</summary>
public sealed record ArquivoArmazenado(string Destino, string Referencia);

/// <summary>
/// Guarda uma cópia da planilha. Duas implementações **convivem** — o
/// mesmo arquivo vai pro Gestor de Arquivos e pro bucket S3, não é
/// escolha entre um e outro. Cada destino liga e desliga sozinho por
/// configuração (<c>Armazenamento:GestorArquivos:Habilitado</c> e
/// <c>Armazenamento:S3:Habilitado</c>).
///
/// O <paramref name="arquivoId"/> é o <c>ArquivoID</c> da linha em
/// <c>Cobranca.Arquivo</c>, o mesmo identificador em toda a cadeia. Como é
/// determinístico, reprocessar o mesmo arquivo sobrescreve o objeto em vez
/// de criar um duplicado.
///
/// A interface mora aqui, junto das implementações, e não em
/// <c>CnabRetorno.Core</c>: o armazenamento é um recurso que deve poder
/// ser removido apagando uma pasta só, e um contrato solto no domínio
/// compartilhado faria a remoção vazar pra outro projeto.
/// </summary>
public interface IArmazenamentoArquivo
{
    Task<ArquivoArmazenado> ArmazenarAsync(
        Guid arquivoId, string nomeArquivo, byte[] conteudo, CancellationToken ct);
}
