namespace CnabRetorno.Core.Dominio;

/// <summary>
/// Cadastro do cliente na base de adesão (<c>ASA_CASH_ADESAO</c>) — a
/// fonte da razão social, que não existe em <c>CASH_COBRANCA</c>.
///
/// Projeção mínima de propósito: o robô só precisa saber quem é o cliente
/// dono da planilha (documento) e como ele se chama (razão social), que é
/// o que vai no JSON enviado ao conversor. Se o fluxo passar a precisar de
/// mais campos do cadastro, eles entram aqui e no mapeamento de
/// <c>AdesaoDbContext</c>.
///
/// TODO(a-confirmar) **em bloco**: schema, tabela e nomes de coluna são
/// placeholder — esta base nunca foi inspecionada. O mapeamento real fica
/// em <c>AdesaoDbContext.OnModelCreating</c>, um único lugar pra corrigir
/// quando o schema chegar. É caminho crítico: sem razão social o arquivo
/// não é enviado.
/// </summary>
public class EmpresaAdesao
{
    /// <summary>CNPJ do cliente, só dígitos — é por ele que a linha é
    /// procurada, usando o valor extraído do nome do arquivo.</summary>
    public required string Documento { get; init; }

    /// <summary>Razão social — o "razao social" do JSON que acompanha a
    /// planilha na chamada do conversor.</summary>
    public string? RazaoSocial { get; init; }
}
