-- Tabela de controle das janelas do robô de retorno de pagamentos
-- (CnabRetorno.PagamentoRetorno.Worker).
--
-- É a única tabela criada por este projeto — todas as outras pertencem a
-- outros times. Guarda, por cliente e por dia, até onde o último arquivo
-- PARCIAL já reportou; o arquivo CONSOLIDADO das 18h ignora a marca e
-- reenvia o dia inteiro.
--
-- Por que em banco e não em memória: um restart no meio do expediente com
-- o controle em memória faria o parcial seguinte reenviar movimentações
-- que o cliente já recebeu, e arquivo bancário entregue não tem desfazer.
--
-- Base: ASA_CASH_PAGAMENTO
-- Schema: Pagamento

IF NOT EXISTS (
    SELECT 1 FROM sys.tables t
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = 'Pagamento' AND t.name = 'ControleJanelaRetorno'
)
BEGIN
    CREATE TABLE Pagamento.ControleJanelaRetorno
    (
        ClienteDocumento        varchar(20)  NOT NULL,
        DataReferencia          date         NOT NULL,

        -- Maior instante de desfecho (COALESCE(DataAtualizacao,
        -- DataCriacao)) já incluído num arquivo deste cliente no dia. O
        -- próximo parcial pega o que for estritamente posterior.
        --
        -- Guarda-se o maior instante INCLUÍDO, e não o horário da janela:
        -- uma movimentação com desfecho às 8h05 gravada só às 8h20
        -- ficaria de fora para sempre se o corte fosse "8h30".
        UltimoInstanteReportado datetime2(7) NOT NULL,

        DataAtualizacao         datetime2(7) NOT NULL,

        CONSTRAINT PK_ControleJanelaRetorno
            PRIMARY KEY CLUSTERED (ClienteDocumento, DataReferencia)
    );
END
GO

-- Pagamento.Parametro precisa de SequencialAtual (NSA por cliente, campo
-- G018 do header do CNAB) — espelho do que já existe em
-- Cobranca.Parametro. Só é criada se a tabela existir e a coluna não.
--
-- TODO(a-confirmar): o schema de Pagamento.Parametro não foi capturado.
-- Se a chave por cliente não for a coluna Documento, este script precisa
-- ser ajustado antes de rodar.
IF EXISTS (
    SELECT 1 FROM sys.tables t
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = 'Pagamento' AND t.name = 'Parametro'
)
AND NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Pagamento.Parametro') AND name = 'SequencialAtual'
)
BEGIN
    ALTER TABLE Pagamento.Parametro
        ADD SequencialAtual bigint NOT NULL CONSTRAINT DF_Parametro_SequencialAtual DEFAULT 0;
END
GO
