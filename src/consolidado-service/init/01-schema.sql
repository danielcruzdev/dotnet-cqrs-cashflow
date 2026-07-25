-- =============================================================================
-- Serviço de Consolidado Diário — schema inicial
--
-- Este banco guarda uma PROJEÇÃO (read model), não a fonte da verdade. Todo
-- o seu conteúdo é derivável dos eventos retidos na outbox do serviço de
-- Lançamentos — o que significa que ele pode ser reconstruído do zero.
-- =============================================================================


-- -----------------------------------------------------------------------------
-- saldo_diario — read model
--
-- Uma linha por comerciante/dia/moeda, incrementada a cada evento consumido.
-- Não existe cópia dos lançamentos individuais aqui: o Consolidado nunca
-- precisa do detalhe, só da soma. Isso mantém a projeção pequena e a
-- consulta O(1) por chave primária.
-- -----------------------------------------------------------------------------
CREATE TABLE saldo_diario (
    comerciante_id      UUID            NOT NULL,
    data                DATE            NOT NULL,
    moeda               CHAR(3)         NOT NULL,
    total_debitos       NUMERIC(18,2)   NOT NULL DEFAULT 0,
    total_creditos      NUMERIC(18,2)   NOT NULL DEFAULT 0,
    saldo               NUMERIC(18,2)   NOT NULL DEFAULT 0,
    qtd_lancamentos     INT             NOT NULL DEFAULT 0,
    atualizado_em       TIMESTAMPTZ     NOT NULL DEFAULT now(),

    -- A moeda faz parte da chave porque saldo consolidado só existe DENTRO
    -- de uma moeda. Sem ela, um comerciante operando em BRL e USD teria os
    -- dois somados na mesma linha, produzindo um número sem significado.
    CONSTRAINT pk_saldo_diario PRIMARY KEY (comerciante_id, data, moeda),

    CONSTRAINT ck_saldo_totais_nao_negativos
        CHECK (total_debitos >= 0 AND total_creditos >= 0),
    CONSTRAINT ck_saldo_qtd_nao_negativa
        CHECK (qtd_lancamentos >= 0)
);

COMMENT ON TABLE saldo_diario IS
    'Projeção agregada do fluxo de caixa. Atualizada por UPSERT atômico a '
    'cada evento consumido, nunca por read-modify-write.';

COMMENT ON COLUMN saldo_diario.total_debitos IS
    'Movimentação BRUTA, não líquida. Como estorno é lançamento contrário, '
    'um crédito de 100 estornado resulta em creditos=100, debitos=100, '
    'saldo=0 e qtd=2. O saldo fica correto; os totais representam giro.';

COMMENT ON COLUMN saldo_diario.qtd_lancamentos IS
    'Métrica de integridade: comparada com o COUNT do write model no mesmo '
    'período, detecta divergência sem precisar recalcular a soma.';

COMMENT ON COLUMN saldo_diario.atualizado_em IS
    'Indicador de frescor para o cliente, já que a consistência é eventual. '
    'NÃO serve para medir lag: é sobrescrito por cada evento seguinte e não '
    'corresponde a nenhum lançamento específico.';


-- -----------------------------------------------------------------------------
-- eventos_processados — dedupe do consumidor
--
-- O RabbitMQ entrega at-least-once, nunca exactly-once. Sem esta tabela,
-- uma redelivery somaria o mesmo lançamento duas vezes no saldo.
--
-- O INSERT aqui e o UPSERT em saldo_diario acontecem na MESMA transação,
-- com ON CONFLICT DO NOTHING: se retornar 0 linhas, o evento já foi
-- processado e a mensagem é confirmada sem tocar no saldo. Deixar a
-- violação de PK estourar abortaria a transação inteira no PostgreSQL.
-- -----------------------------------------------------------------------------
CREATE TABLE eventos_processados (
    event_id        UUID            PRIMARY KEY,
    processado_em   TIMESTAMPTZ     NOT NULL DEFAULT now()
);

COMMENT ON TABLE eventos_processados IS
    'Chaves de eventos já aplicados à projeção. Cresce na mesma taxa dos '
    'lançamentos; expurgo por idade (além da janela de redelivery do broker) '
    'está registrado como evolução futura.';
