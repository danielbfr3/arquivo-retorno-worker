-- Tabelas de controle do robô de retorno de pagamentos
-- (CnabRetorno.PagamentoRetorno.Worker) — as únicas criadas por este
-- projeto na base ASA_CASH_PAGAMENTO; todas as outras pertencem a outros
-- times.
--
-- Duas camadas de idempotência dos arquivos PARCIAIS (o consolidado
-- repete o dia útil inteiro por design):
--
-- 1. ControleJanelaRetorno — marca d'água por cliente: até que instante
--    de desfecho ele já foi reportado. CONTÍNUA, sem dimensão de dia: o
--    "dia útil" do robô vai de consolidado a consolidado (18h→18h), e
--    uma marca por dia de calendário deixava desfechos pós-18h sem dono.
--
-- 2. ControlePagamentoReportado — pares (PagamentoID, CodigoStatus) já
--    enviados. Barra o que a marca não pega: um UPDATE qualquer na linha
--    do pagamento avança DataAtualizacao e o traria de volta no delta
--    com o mesmo status. Status novo passa (desfecho novo de verdade).
--
-- Por que em banco e não em memória: um restart no meio do expediente
-- com o controle em memória faria o parcial seguinte reenviar
-- movimentações que o cliente já recebeu, e arquivo bancário entregue
-- não tem desfazer.
--
-- Base: ASA_CASH_PAGAMENTO / Schema: Pagamento

IF NOT EXISTS (
    SELECT 1 FROM sys.tables t
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = 'Pagamento' AND t.name = 'ControleJanelaRetorno'
)
BEGIN
    CREATE TABLE Pagamento.ControleJanelaRetorno
    (
        ClienteDocumento        varchar(20)  NOT NULL,

        -- Maior instante de desfecho (COALESCE(DataAtualizacao,
        -- DataCriacao)) já incluído num arquivo deste cliente. O próximo
        -- parcial pega o que for estritamente posterior.
        --
        -- Guarda-se o maior instante INCLUÍDO, e não o horário da janela:
        -- uma movimentação com desfecho às 8h05 gravada só às 8h20
        -- ficaria de fora para sempre se o corte fosse "8h30".
        UltimoInstanteReportado datetime2(7) NOT NULL,

        DataAtualizacao         datetime2(7) NOT NULL,

        CONSTRAINT PK_ControleJanelaRetorno
            PRIMARY KEY CLUSTERED (ClienteDocumento)
    );
END
GO

-- Se existir a versão anterior desta tabela (PK composta com
-- DataReferencia), ela precisa ser recriada — o desenho por dia foi
-- substituído pelo contínuo. Este script NÃO derruba automaticamente:
-- conferir se há dado a preservar antes de rodar:
--   DROP TABLE Pagamento.ControleJanelaRetorno;  -- e rodar o CREATE acima
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('Pagamento.ControleJanelaRetorno')
      AND name = 'DataReferencia'
)
    RAISERROR ('Pagamento.ControleJanelaRetorno está no desenho antigo (com DataReferencia) — recriar manualmente.', 16, 1);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.tables t
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = 'Pagamento' AND t.name = 'ControlePagamentoReportado'
)
BEGIN
    CREATE TABLE Pagamento.ControlePagamentoReportado
    (
        PagamentoID  uniqueidentifier NOT NULL,
        CodigoStatus smallint         NOT NULL,
        DataCriacao  datetime2(7)     NOT NULL,

        CONSTRAINT PK_ControlePagamentoReportado
            PRIMARY KEY CLUSTERED (PagamentoID, CodigoStatus)
    );
    -- Cresce um punhado de linhas por pagamento com desfecho. Sem
    -- limpeza automática — TODO(a-confirmar): política de retenção
    -- (ex.: DELETE WHERE DataCriacao < DATEADD(month, -3, GETUTCDATE())).
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
