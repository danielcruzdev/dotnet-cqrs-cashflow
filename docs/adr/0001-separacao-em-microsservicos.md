# ADR 0001 — Separação em dois microsserviços

**Status:** aceito · **Data:** 26/07/2026 · **Deriva de:** RNF-01

## Contexto

O sistema tem dois casos de uso: registrar um lançamento de débito ou crédito e
consultar o saldo diário consolidado. Sozinhos, eles cabem confortavelmente em
uma aplicação com duas tabelas.

O que não cabe é a restrição:

> O serviço de controle de lançamentos não deve ficar indisponível se o sistema
> de consolidado diário cair.

O enunciado ainda cita quatro famílias de padrão arquitetural — microsserviços,
monolitos, SOA e serverless — e pede avaliação, não escolha por reflexo.

## Decisão

Dois serviços independentes: **Lançamentos** (write model, fonte da verdade) e
**Consolidado Diário** (read model, projeção agregada), cada um com seu próprio
banco PostgreSQL, seu próprio ciclo de vida e seu próprio deploy.

## Alternativas consideradas

**Monolito modular.** Seria mais simples de rodar, mais barato de operar e daria
consistência forte de graça — sem fila, sem outbox, sem dedupe, sem janela de
consistência eventual. É a alternativa mais forte, e em quase qualquer outro
enunciado seria a escolha certa. Foi descartada por um motivo só: no mesmo
processo, o isolamento entre os dois fluxos passa a depender de disciplina —
um `try/catch` esquecido, um pool de conexões compartilhado esgotado por uma
consulta pesada, um `OutOfMemory` causado pelo relatório. Nenhum desses é
hipotético, e nenhum é detectável em code review de forma confiável. Com dois
processos, a garantia deixa de depender de comportamento e passa a depender de
topologia.

**SOA clássico com ESB.** É o antipadrão exato deste caso: centraliza a lógica
de integração num barramento compartilhado, criando justamente o acoplamento
que o requisito pede para eliminar. Aqui o broker é *dumb pipe* — roteia
mensagem, não executa regra.

**Serverless (Functions/Lambda).** Atraente pela escala automática e pelo custo
em carga baixa, mas colide em dois pontos concretos: o consumer é um processo de
longa duração com controle fino de `prefetch` e `ack`, desconfortável no modelo
de execução por invocação; e o cold start ameaça o SLO de p95 abaixo de 100 ms.
O gargalo real acabaria sendo a pool de conexões do Postgres sob fan-out de
instâncias.

## Consequências

**Positivas.** O isolamento de falha é estrutural, não convencional. Os bancos
separados (*database per service*) impedem que um pico de leitura no Consolidado
afete a escrita. Cada lado escala por conta própria — e a assimetria é real: a
leitura tem SLO de 50 rps, a escrita são dezenas a centenas de operações por
comerciante por dia.

**Negativas, assumidas.** Consistência eventual entre o `201 Created` e o saldo
refletido (ADR [0006](0006-consistencia-eventual-aceita.md)). Complexidade
operacional maior: dois deploys, um broker, uma DLQ e um runbook. E a
necessidade de idempotência em dois pontos — na escrita e no consumo — que num
monolito seria simplesmente uma transação.

**O que torna a decisão defensável em vez de moda:** o custo acima só se paga
porque o requisito de isolamento é explícito no enunciado. Sem ele, a resposta
correta seria o monolito modular.
