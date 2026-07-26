# ADR 0003 — CQRS e Transactional Outbox

**Status:** aceito · **Data:** 26/07/2026 · **Deriva de:** ADR [0001](0001-separacao-em-microsservicos.md), [0002](0002-comunicacao-assincrona-via-broker.md)

## Contexto

O domínio é um livro-razão: eventos imutáveis de entrada e uma projeção agregada
por cima. Comando (lançar) e consulta (consolidar) já vêm separados pelo próprio
negócio, com padrões de acesso opostos — escrita transacional linha a linha de
um lado, leitura agregada por chave do outro.

Além disso, gravar o lançamento no banco e publicar o evento no broker são duas
operações em dois sistemas. Sem cuidado, uma pode ter sucesso e a outra falhar:
lançamento gravado sem evento (saldo nunca atualiza, divergência silenciosa) ou
evento publicado sem lançamento (saldo conta algo que não existe).

## Decisão

**CQRS em dois níveis.** No macro, os dois serviços *são* o write e o read
model. No micro, dentro de cada serviço, casos de uso são
`ICommandHandler<,>` / `IQueryHandler<,>` na camada Application
(ADR [0009](0009-handlers-proprios-vs-mediatr.md)).

**Transactional Outbox.** O `INSERT` em `lancamentos` e o `INSERT` em
`outbox_messages` acontecem na **mesma transação local**. Um
`BackgroundService` lê as pendentes com `FOR UPDATE SKIP LOCKED`, publica com
publisher confirms e marca `processado_em`. Se a publicação falhar, o
`ROLLBACK` devolve as linhas para pendentes.

**Consumidor idempotente.** O `INSERT` em `eventos_processados`
(`ON CONFLICT DO NOTHING`) e o `UPSERT` em `saldo_diario` acontecem na mesma
transação; `rowsAffected == 0` no primeiro significa duplicata e o segundo é
pulado.

## Alternativas consideradas

**Two-phase commit entre banco e broker.** Resolveria a atomicidade de forma
direta. Descartado: o RabbitMQ não participa de transação distribuída com o
PostgreSQL de forma prática, e 2PC introduz coordenador bloqueante — o oposto do
que um requisito de disponibilidade pede.

**Publicar direto depois do commit, sem outbox.** É o caminho de menos código, e
falha na janela entre o `COMMIT` e o `publish`: se o processo morre ali, o
lançamento existe e o evento nunca sai. Não há como detectar depois, porque não
sobrou registro da intenção.

**Change Data Capture (Debezium sobre o WAL).** Tecnicamente superior — nem
exige a tabela de outbox — e é o caminho certo em escala. Descartado pelo custo
de infraestrutura (Kafka Connect ou equivalente) e por acoplar a publicação ao
formato físico do WAL, num projeto cujo problema não é volume.

**Event Sourcing completo.** Guardar os eventos como fonte da verdade em vez do
estado atual daria auditoria nativa, o que combina com fluxo de caixa.
Descartado porque a retenção da outbox já entrega replay a uma fração do custo,
e porque Event Sourcing traz junto snapshot, versionamento de evento e
reconstrução — problemas que este sistema não tem.

## Consequências

**Positivas.** Nenhum evento se perde por falha parcial: o lançamento e a
intenção de publicar são atômicos. O publisher escala em réplicas sem publicar
duplicado, graças ao `SKIP LOCKED`. E, como as linhas processadas **não são
deletadas**, a outbox vira o log de eventos do sistema — é ela que sustenta o
replay descrito no [runbook](../runbook.md).

**Negativas, assumidas.**

- **A garantia é *at-least-once*, não exactly-once.** Se o `COMMIT` do
  `processado_em` falhar depois de o broker confirmar, a linha volta a pendente
  e o evento é publicado de novo. Nada se perde, mas **pode duplicar** — e é o
  `eventos_processados` do consumidor que fecha essa ponta.
- Uma linha de outbox por lançamento, crescendo indefinidamente. O expurgo com
  janela de retenção está em evoluções futuras; hoje o custo é conhecido e
  aceito em troca da capacidade de replay.
- O lag de publicação tem piso no intervalo de varredura do publisher (2 s em
  produção), que entra no orçamento do SLO de consistência eventual.
