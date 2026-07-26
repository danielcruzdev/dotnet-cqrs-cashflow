# ADR 0004 — RabbitMQ como broker, em vez de Kafka

**Status:** aceito · **Data:** 26/07/2026 · **Responde a:** "avalie as ferramentas de integração" (enunciado)

## Contexto

Decidida a comunicação assíncrona (ADR
[0002](0002-comunicacao-assincrona-via-broker.md)), falta escolher a ferramenta.
O enunciado pede *avaliação*, não escolha — então o critério precisa estar
explícito antes da conclusão.

Os números do sistema: um evento por lançamento, com volume de escrita na ordem
de dezenas a centenas por comerciante por dia. Um único consumidor lógico. Um
único tipo de evento. Necessidade real de DLQ, de retentativa e de replay por
período.

## Decisão

**RabbitMQ**, com exchange `topic` `lancamentos.events`, fila durável
`consolidado.lancamento-realizado`, dead-letter exchange
`lancamentos.events.dlx` e DLQ `consolidado.lancamento-realizado.dlq`.

## Alternativas consideradas

**Apache Kafka.** É a escolha certa quando o log particionado *é* o produto:
alto throughput sustentado, múltiplos consumer groups lendo o mesmo tópico em
ritmos diferentes, retenção longa como fonte de verdade e reprocessamento por
offset. Nenhuma dessas quatro condições existe aqui — há um consumidor, um tipo
de evento e volume baixo. Em compensação, Kafka traria ordenação por partição
(que este domínio não exige, já que o `UPSERT` é comutativo por natureza:
somar créditos e débitos dá o mesmo resultado em qualquer ordem), operação
significativamente mais pesada e **nenhuma DLQ nativa** — o padrão exigiria uma
tópico de erro e lógica própria de retry, enquanto no RabbitMQ é uma
configuração de fila.

**Azure Service Bus / Amazon SQS+SNS.** Excelentes, e o argumento decisivo
contra é de contexto: o desafio precisa subir na máquina do avaliador com um
comando. Um broker gerenciado exigiria conta em nuvem e credencial.

**Redis Streams.** O Redis já está no compose para cache, então seria um
componente a menos. Descartado por durabilidade: as garantias de persistência do
Redis são mais fracas, e usar a mesma instância para cache e para o caminho de
entrega de eventos acopla dois papéis com perfis de falha muito diferentes.

## Consequências

**Positivas.** DLQ, TTL de mensagem e retry por configuração da fila, não por
código. Console de administração em `http://localhost:15672` — que num desafio
vale bastante, porque o avaliador *vê* a mensagem passando. Operação leve o
suficiente para caber num `docker compose`.

**Negativas, assumidas.** Sem log retido: uma mensagem consumida e confirmada
sai da fila para sempre. É por isso que o replay depende da retenção da
**outbox**, não do broker. E se um segundo consumidor com semântica diferente
aparecer (por exemplo, um serviço de antifraude), será preciso uma segunda fila
com binding próprio, em vez de simplesmente um novo consumer group.

## Nota de implementação — topologia por `definitions.json`

Exchanges, filas, bindings e DLX vêm de `infra/rabbitmq/definitions.json`,
carregado no boot do broker — o equivalente ao `/docker-entrypoint-initdb.d/`
usado nos Postgres. A vantagem é que a topologia fica declarada em um arquivo
legível e versionado, em vez de espalhada por chamadas de API na inicialização
dos serviços.

**O trade-off, que é real:** o serviço deixa de ser autossuficiente. Apontado
para um broker não provisionado, publicar numa exchange sem binding **não é
erro** para o RabbitMQ — é descarte silencioso. E o `mandatory: true` não cobre
esse caso: com publisher confirms ligado, a mensagem sem rota é confirmada
normalmente e devolvida por um evento `BasicReturn` assíncrono separado; sem
tratar esse retorno, ela some do mesmo jeito. Tratar `BasicReturn` é o primeiro
item a acrescentar se o sistema passar a rodar contra um broker compartilhado.

## Dívida registrada

A routing key é hoje derivada no publisher por um `switch` sobre `tipo_evento` —
ou seja, o transporte interpretando a carga. `EventoDeDominio.RoutingKey` já
existe no domínio; promovê-la a coluna da outbox apagaria o `switch` e o ramo de
"tipo desconhecido". Não foi feito porque altera o DDL, e a mudança de schema
não se paga no prazo desta entrega.
