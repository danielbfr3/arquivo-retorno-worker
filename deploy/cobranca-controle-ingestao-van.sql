-- Controle de idempotência por conteúdo do robô de remessa de VAN
-- (CnabRetorno.RemessaVan.Worker) — a única tabela criada por este
-- projeto na base CASH_COBRANCA; todas as outras pertencem a outros
-- times.
--
-- TODO(a-confirmar): confirmar com o time dono da base a permissão pra
-- criar esta tabela no schema Cobranca (alternativa: um schema próprio
-- do worker).
--
-- Guarda o MD5 de cada remessa já ingerida. O nome do arquivo não serve
-- de chave: a VAN pode retransmitir o mesmo conteúdo com nome novo dias
-- depois — sem este controle, a retransmissão ganharia GUID novo, segundo
-- objeto no storage e segunda linha em Cobranca.Arquivo, que o worker de
-- conversão processaria de novo (remessa duplicada pode virar boleto
-- duplicado lá na frente).
--
-- O hash é gravado DEPOIS da ingestão completa (upload + registro):
-- crash no meio reprocessa (recuperável e visível), nunca marca como
-- ingerido o que não foi (perda silenciosa).
--
-- Base: CASH_COBRANCA / Schema: Cobranca

IF NOT EXISTS (
    SELECT 1 FROM sys.tables t
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = 'Cobranca' AND t.name = 'ControleIngestaoVan'
)
BEGIN
    CREATE TABLE Cobranca.ControleIngestaoVan
    (
        -- MD5 do conteúdo em hexadecimal. A PK é a própria garantia de
        -- unicidade: duas instâncias em corrida (que o lock de execução
        -- deveria impedir) colidem aqui e a segunda vira aviso em log.
        Md5          varchar(32)      NOT NULL,

        ArquivoID    uniqueidentifier NOT NULL,
        NomeOriginal varchar(250)     NOT NULL,
        DataCriacao  datetime2(7)     NOT NULL,

        CONSTRAINT PK_ControleIngestaoVan PRIMARY KEY CLUSTERED (Md5)
    );
END
GO
