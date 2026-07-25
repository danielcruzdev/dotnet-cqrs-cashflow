-- Serviço de Lançamentos — schema inicial.
-- Executado pelo entrypoint do PostgreSQL na primeira inicialização do volume.

-- Lançamento é imutável: sem UPDATE, sem DELETE. Correção se faz por estorno.
-- O id é gerado pela aplicação como UUIDv7 (ordenado no tempo); o DEFAULT é só
-- rede de segurança para inserção manual.
CREATE TABLE lancamentos (
    id                  UUID            PRIMARY KEY DEFAULT gen_random_uuid(),
    comerciante_id      UUID            NOT NULL,
    tipo                VARCHAR(10)     NOT NULL,
    valor               NUMERIC(18,2)   NOT NULL,
    moeda               CHAR(3)         NOT NULL DEFAULT 'BRL',
    data_competencia    DATE            NOT NULL,
    descricao           TEXT,
    estorno_de_id       UUID            NULL REFERENCES lancamentos (id),
    chave_idempotencia  VARCHAR(100)    NOT NULL,
    hash_payload        CHAR(64)        NOT NULL,
    criado_em           TIMESTAMPTZ     NOT NULL DEFAULT now(),

    CONSTRAINT ck_lancamento_tipo   CHECK (tipo IN ('DEBITO', 'CREDITO')),
    CONSTRAINT ck_lancamento_valor  CHECK (valor > 0),

    -- Por comerciante, não global: dois comerciantes podem usar "pedido-123".
    CONSTRAINT uq_lancamento_idempotencia UNIQUE (comerciante_id, chave_idempotencia)
);

COMMENT ON COLUMN lancamentos.data_competencia IS
    'Dia contábil no fuso America/Sao_Paulo. É esta coluna que agrega o consolidado.';
COMMENT ON COLUMN lancamentos.criado_em IS
    'Instante físico em UTC. Auditoria e cálculo de lag; nunca agregação.';
COMMENT ON COLUMN lancamentos.hash_payload IS
    'SHA-256 do payload. Distingue retry legítimo (200) de reuso de chave (409).';

CREATE INDEX idx_lancamentos_comerciante_data
    ON lancamentos (comerciante_id, data_competencia);

-- Índice parcial, não constraint: NULLs precisam continuar se repetindo.
CREATE UNIQUE INDEX uq_lancamento_estorno
    ON lancamentos (estorno_de_id)
    WHERE estorno_de_id IS NOT NULL;


-- Outbox transacional: gravada na MESMA transação do lançamento.
-- Linhas processadas não são deletadas — são a fonte de replay do read model.
CREATE TABLE outbox_messages (
    id                  UUID            PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id            UUID            NOT NULL UNIQUE,
    agregado_id         UUID            NOT NULL,
    comerciante_id      UUID            NOT NULL,
    data_competencia    DATE            NOT NULL,
    tipo_evento         VARCHAR(100)    NOT NULL,
    payload             JSONB           NOT NULL,
    criado_em           TIMESTAMPTZ     NOT NULL DEFAULT now(),
    processado_em       TIMESTAMPTZ     NULL,
    tentativas          INT             NOT NULL DEFAULT 0,

    CONSTRAINT ck_outbox_tentativas CHECK (tentativas >= 0)
);

COMMENT ON COLUMN outbox_messages.data_competencia IS
    'Promovida do payload para permitir replay por competência. Filtrar por '
    'criado_em deixaria de fora lançamento retroativo e estorno de dia antigo.';

CREATE INDEX idx_outbox_pendentes
    ON outbox_messages (criado_em)
    WHERE processado_em IS NULL;

CREATE INDEX idx_outbox_replay
    ON outbox_messages (comerciante_id, data_competencia);
