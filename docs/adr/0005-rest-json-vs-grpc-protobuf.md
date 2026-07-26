# ADR 0005 — REST + JSON nas APIs e JSON no evento

**Status:** aceito · **Data:** 26/07/2026 · **Responde a:** "avalie os protocolos e formatos de comunicação" (enunciado)

## Contexto

Há duas decisões de protocolo distintas, e tratá-las como uma só é o erro comum:

1. **Da borda para o sistema** — como o comerciante registra e consulta.
2. **Entre os serviços** — em que formato o evento trafega pelo broker.

## Decisão

**Borda: REST sobre HTTP/1.1 com JSON**, erros em `ProblemDetails` (RFC 7807).
**Evento: JSON** no corpo AMQP, com envelope explícito (`eventId`, `version`,
`eventType`, `occurredAt`, `correlationId`) e versionamento na routing key
(`lancamento.realizado.v1`).

## Alternativas consideradas — borda

**gRPC + Protobuf.** Payload binário menor, contrato forte gerado do `.proto`,
streaming nativo. O ganho não se materializa aqui: os payloads têm dezenas de
bytes, e a diferença de serialização é irrelevante diante de uma ida ao banco.
Em troca, o custo é concreto — o cliente é um app de comerciante ou um
front-end, onde gRPC exige gRPC-Web e um proxy de tradução; `curl` deixa de
funcionar; e o avaliador não consegue exercitar a API sem ferramenta
específica. Semânticas que este domínio usa de fato — `201 Created` com
`Location`, `409 Conflict` para chave de idempotência reusada, `422` para regra
de negócio insatisfeita, `Retry-After` no `429` e no `503` — são nativas do HTTP
e teriam de ser remapeadas em códigos de status gRPC menos expressivos.

**GraphQL.** Resolve o problema de *over-fetching* em grafos de dados. Aqui há
dois recursos com formato fixo; a flexibilidade não tem onde ser exercida, e o
custo de cache e de limite de complexidade de query seria pago à toa.

## Alternativas consideradas — evento

**Protobuf ou Avro com schema registry.** É a resposta certa em volume alto e
com muitos consumidores: contrato validado na publicação, evolução de schema
com regra de compatibilidade, payload compacto. Descartado porque exige um
componente a mais (o registry) e porque, com um consumidor e um tipo de evento,
o contrato é revisado por leitura e coberto pelo teste ponta a ponta.

O ganho de tamanho seria real mas irrelevante: o evento tem ~500 bytes em JSON,
e o volume de escrita é de dezenas a centenas por comerciante por dia.

**MessagePack / CBOR.** Compactam sem exigir registry, mas perdem justamente o
que mais vale aqui: a mensagem deixa de ser legível no console do RabbitMQ e na
DLQ. Diagnóstico de mensagem envenenada passaria a exigir ferramenta.

## Consequências

**Positivas.** Qualquer cliente HTTP funciona; os arquivos `.http` versionados
no repositório documentam os casos de borda e são executáveis. O evento é
inspecionável na fila e na DLQ sem ferramenta — o que importa exatamente no
momento em que algo deu errado. `ProblemDetails` dá ao cliente um `codigo`
estável para ramificar, em vez de parse de mensagem.

**Negativas, assumidas.** Sem contrato gerado, a compatibilidade entre produtor
e consumidor depende de revisão e de teste, não do compilador. A mitigação é
dupla: o contrato do evento é **copiado** nos dois serviços em vez de
compartilhado por biblioteca (o que permite evoluir um lado por vez, com a
versão carimbada na routing key e no envelope — ver a ressalva sobre o binding
curinga no [ADR 0002](0002-comunicacao-assincrona-via-broker.md)), e o
consumidor valida o payload antes de aplicar.
Um evento malformado vai para a DLQ em vez de contaminar a projeção.

**Nota:** OpenAPI/Swagger ficou fora desta entrega. O pacote
`Microsoft.AspNetCore.OpenApi` arrasta uma versão de `Microsoft.OpenApi` com CVE
conhecido, e a versão corrigida tem quebra de API incompatível com o source
generator que acompanha o pacote. Com auditoria de vulnerabilidade tratada como
erro de build, a saída correta foi não expor OpenAPI nesta versão em vez de
suprimir o aviso. A exploração da API fica pelos arquivos `.http` e pelos
exemplos de `curl` do README.
