-- =============================================================================
-- Serviço de Lançamentos — schema inicial
--
-- Executado automaticamente pelo entrypoint da imagem oficial do PostgreSQL
-- (arquivos em /docker-entrypoint-initdb.d/ rodam na primeira inicialização
-- do volume). Sem ferramenta de migração no escopo do desafio; em produção
-- a resposta seria DbUp ou Flyway.
-- =============================================================================


-- -----------------------------------------------------------------------------
-- lancamentos — write model e fonte da verdade do sistema
--
-- Lançamento é IMUTÁVEL: não há UPDATE nem DELETE. Correção de erro se faz
-- por lançamento compensatório (estorno), preservando a trilha de auditoria
-- e permitindo que o read model se corrija pelo mesmo caminho de eventos.
-- -----------------------------------------------------------------------------
-- Nota sobre o id: na prática ele é sempre gerado pela aplicação, como UUIDv7
-- (Guid.CreateVersion7), cujos 48 bits iniciais são timestamp. Isso faz as
-- inserções caírem no final do índice B-tree em vez de espalhadas, evitando a
-- fragmentação de página típica de chave aleatória. O DEFAULT abaixo é só rede
-- de segurança para inserção manual e produz UUIDv4 — se ele disparar em volume,
-- é sinal de que algo está inserindo por fora da aplicação.
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

    -- O sinal da operação vem do tipo, nunca de valor negativo. Elimina a
    -- classe de bug "esqueci de negativar" no cálculo do saldo.
    CONSTRAINT ck_lancamento_tipo   CHECK (tipo IN ('DEBITO', 'CREDITO')),
    CONSTRAINT ck_lancamento_valor  CHECK (valor > 0),

    -- Idempotência é por comerciante, não global: dois comerciantes podem
    -- legitimamente usar a mesma chave (ex.: "pedido-123").
    CONSTRAINT uq_lancamento_idempotencia UNIQUE (comerciante_id, chave_idempotencia)
);

COMMENT ON COLUMN lancamentos.data_competencia IS
    'Dia contábil ao qual o lançamento pertence, no fuso America/Sao_Paulo. '
    'É esta coluna que agrega o consolidado — nunca criado_em.';

COMMENT ON COLUMN lancamentos.criado_em IS
    'Instante físico do registro, em UTC. Serve para auditoria e para o '
    'cálculo do lag de consistência; nunca para agregação.';

COMMENT ON COLUMN lancamentos.hash_payload IS
    'SHA-256 do payload da requisição. Permite distinguir retry legítimo '
    '(mesma chave, mesmo payload -> 200) de reuso indevido de chave '
    '(mesma chave, payload diferente -> 409).';

-- Consulta de lançamentos por período (RF-04).
CREATE INDEX idx_lancamentos_comerciante_data
    ON lancamentos (comerciante_id, data_competencia);

-- Um lançamento só pode ser estornado uma vez. Índice único parcial em vez
-- de constraint porque NULLs precisam continuar se repetindo livremente.
CREATE UNIQUE INDEX uq_lancamento_estorno
    ON lancamentos (estorno_de_id)
    WHERE estorno_de_id IS NOT NULL;


-- -----------------------------------------------------------------------------
-- outbox_messages — Transactional Outbox
--
-- Gravada na MESMA transação do lançamento: ou os dois vão, ou nenhum vai.
-- É isso que garante que todo lançamento aceito gera exatamente um evento,
-- sem transação distribuída e sem depender do broker estar de pé.
--
-- Linhas processadas NÃO são deletadas: a outbox retida é a única fonte de
-- replay do sistema, e portanto o caminho de reconstrução do read model.
-- -----------------------------------------------------------------------------
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

COMMENT ON TABLE outbox_messages IS
    'Fila de saída transacional. Linhas processadas são retidas de propósito: '
    'servem de log de eventos replayável para reconstruir o read model.';

COMMENT ON COLUMN outbox_messages.event_id IS
    'Mesmo eventId publicado no envelope. É a chave de dedupe do consumidor '
    'e o elo entre a outbox e a tabela eventos_processados do Consolidado.';

COMMENT ON COLUMN outbox_messages.data_competencia IS
    'Promovida do payload a coluna própria para permitir replay recortado por '
    'COMPETÊNCIA. Filtrar por criado_em deixaria de fora lançamento retroativo '
    'e estorno de dia antigo — o saldo reconstruído sairia silenciosamente menor.';

-- Leitura do publisher: só pendentes, em ordem de criação.
-- Índice parcial porque a tabela cresce indefinidamente mas a fatia
-- pendente é sempre pequena.
CREATE INDEX idx_outbox_pendentes
    ON outbox_messages (criado_em)
    WHERE processado_em IS NULL;

-- Replay por período de competência (reconstrução do read model).
CREATE INDEX idx_outbox_replay
    ON outbox_messages (comerciante_id, data_competencia);
