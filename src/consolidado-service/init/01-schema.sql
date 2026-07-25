-- Serviço de Consolidado Diário — schema inicial.
-- Este banco guarda uma projeção, não a fonte da verdade: todo o conteúdo é
-- derivável dos eventos retidos na outbox do serviço de Lançamentos.

-- Uma linha por comerciante/dia/moeda, incrementada a cada evento consumido.
-- A moeda entra na chave porque saldo só existe dentro de uma moeda.
CREATE TABLE saldo_diario (
    comerciante_id      UUID            NOT NULL,
    data                DATE            NOT NULL,
    moeda               CHAR(3)         NOT NULL,
    total_debitos       NUMERIC(18,2)   NOT NULL DEFAULT 0,
    total_creditos      NUMERIC(18,2)   NOT NULL DEFAULT 0,
    saldo               NUMERIC(18,2)   NOT NULL DEFAULT 0,
    qtd_lancamentos     INT             NOT NULL DEFAULT 0,
    atualizado_em       TIMESTAMPTZ     NOT NULL DEFAULT now(),

    CONSTRAINT pk_saldo_diario PRIMARY KEY (comerciante_id, data, moeda),
    CONSTRAINT ck_saldo_totais_nao_negativos
        CHECK (total_debitos >= 0 AND total_creditos >= 0),
    CONSTRAINT ck_saldo_qtd_nao_negativa
        CHECK (qtd_lancamentos >= 0)
);

COMMENT ON COLUMN saldo_diario.total_debitos IS
    'Movimentação bruta, não líquida: estorno é lançamento contrário, então um '
    'crédito de 100 estornado resulta em creditos=100, debitos=100, saldo=0.';
COMMENT ON COLUMN saldo_diario.qtd_lancamentos IS
    'Métrica de integridade: comparada com o COUNT do write model no período.';
COMMENT ON COLUMN saldo_diario.atualizado_em IS
    'Indicador de frescor para o cliente. Não serve para medir lag — é '
    'sobrescrito por cada evento seguinte.';


-- Dedupe do consumidor: o broker entrega at-least-once.
-- O INSERT aqui e o UPSERT em saldo_diario ficam na mesma transação, com
-- ON CONFLICT DO NOTHING — deixar a violação de PK estourar abortaria a transação.
CREATE TABLE eventos_processados (
    event_id        UUID            PRIMARY KEY,
    processado_em   TIMESTAMPTZ     NOT NULL DEFAULT now()
);
