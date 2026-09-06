-- Referência de criação da tabela Cobranca.DocumentoDados, na base
-- CASH_COBRANCA.
--
-- Este worker SÓ LÊ esta tabela (ver
-- CnabRetorno.ExcelCnab.Worker/Persistencia/DocumentoDadosRepository.cs) —
-- quem a popula é outro sistema. É responsabilidade do time dono da base
-- rodar e confirmar este script (nomes de colunas, tipos, tamanhos) em
-- cada ambiente antes de ir para produção, no mesmo espírito dos
-- TODO(a-confirmar) já usados neste projeto para schemas de outros times
-- (ver docs/riscos-conhecidos.md).
--
-- Dados é um objeto JSON plano: cada chave é o cabeçalho de uma coluna da
-- planilha do documento, e o valor é o que deve ser escrito naquela
-- coluna. Ex.: {"Nome Cliente": "ACME LTDA", "Valor": "1500.00",
-- "Razão Social": "ACME DISTRIBUIDORA LTDA"}.
--
-- A chave "Razão Social" é reservada (ver
-- CnabRetorno.Core/Dominio/DocumentoDados.cs, ChaveRazaoSocial): além de
-- preencher a coluna homônima, é a fonte da razão social enviada ao
-- conversor. Não existe mais uma base de adesão separada para isso.
IF NOT EXISTS (
    SELECT 1
    FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'Cobranca' AND t.name = 'DocumentoDados'
)
BEGIN
    CREATE TABLE Cobranca.DocumentoDados (
        NumeroDocumento varchar(20) NOT NULL PRIMARY KEY,
        Dados           nvarchar(max) NOT NULL
            CONSTRAINT CK_DocumentoDados_Dados_Json CHECK (ISJSON(Dados) = 1)
    );
END
