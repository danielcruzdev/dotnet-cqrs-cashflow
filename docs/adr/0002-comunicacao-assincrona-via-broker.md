# ADR 0002 — Comunicação assíncrona via message broker

**Status:** aceito · **Data:** 26/07/2026 · **Deriva de:** RNF-01, ADR [0001](0001-separacao-em-microsservicos.md)

## Contexto

Decidida a separação em dois serviços, resta definir como o read model fica
sabendo dos lançamentos. Qualquer forma de comunicação em que Lançamentos
*espere* uma resposta do Consolidado reintroduz o acoplamento que a separação
existe para eliminar — a queda do segundo passaria a se manifestar como timeout,
latência ou erro no primeiro.

## Decisão

Comunicação **exclusivamente assíncrona por evento**, publicado em uma exchange
`topic` do RabbitMQ (`lancamentos.events`, routing key
`lancamento.realizado.v1`). O serviço de Lançamentos publica e segue em frente;
o Consolidado consome quando puder.

Não existe nenhuma chamada HTTP de Lançamentos para Consolidado no código, e
essa ausência é verificável: o serviço de Lançamentos não tem sequer a
connection string do outro banco nem a URL da outra API.

## Alternativas consideradas

**Chamada HTTP síncrona após gravar.** Simples e imediatamente consistente, e
falha no requisito: com o Consolidado fora, o `POST` passaria a esperar o
timeout ou a devolver erro. Mesmo com *fire and forget*, o evento se perde
quando o destino está fora — e "perdemos alguns saldos" é pior do que
"o saldo demorou 3 segundos".

**Consolidado consultando o banco de Lançamentos** (leitura direta ou CDC sobre
a tabela). Elimina a fila, mas cria acoplamento no schema: qualquer alteração na
tabela de lançamentos passa a ser mudança de contrato entre dois times. Também
transformaria o `lancamentos_db` em dependência de disponibilidade da **leitura**,
somando um consumidor de conexões ao banco que precisa continuar aceitando
escrita.

**Polling periódico da outbox pelo Consolidado.** Funcionaria, e dispensaria o
broker. Foi descartado porque troca um componente de infraestrutura por
acoplamento de banco (o mesmo problema acima), e porque o intervalo de polling
vira o piso do lag de consistência.

## Consequências

**Positivas.** A queda do Consolidado é invisível para o Lançamentos: as
mensagens acumulam na fila durável e são drenadas na volta. O contrato entre os
serviços é o **evento**, carimbado com versão na própria routing key (`.v1`) e
no envelope, e não o schema de uma tabela.

Uma ressalva honesta sobre essa versão: o binding atual da fila do consumidor é
`lancamento.#`, então hoje o segmento de versão **não** roteia — um
`lancamento.realizado.v2` cairia na mesma fila do consumidor v1 e seria
rejeitado para a DLQ. A versão está no lugar certo para servir de discriminador;
transformá-la em roteamento de verdade é trocar o binding curinga por um binding
por versão, e é o primeiro passo de qualquer evolução do contrato.

**Negativas, assumidas.** Entrega *at-least-once*: o broker pode entregar a
mesma mensagem mais de uma vez, e o publisher pode republicar se o commit da
outbox falhar depois do envio. O consumidor **precisa** ser idempotente — não é
refinamento, é obrigação (ADR [0003](0003-cqrs-e-transactional-outbox.md)).
Some-se a isso um componente de infraestrutura a operar, com DLQ e runbook
próprios.

**Nota de honestidade.** A fila durável do RabbitMQ não é o que garante RPO
zero: um broker standalone que perde o disco perde mensagem. Quem garante o RPO
neste desenho é a **retenção da outbox** no `lancamentos_db`, que permite
republicar qualquer período (ver [runbook](../runbook.md)).
