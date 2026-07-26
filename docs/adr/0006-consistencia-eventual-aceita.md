# ADR 0006 — Consistência eventual aceita, com janela quantificada

**Status:** aceito · **Data:** 26/07/2026 · **Deriva de:** ADR [0001](0001-separacao-em-microsservicos.md), [0002](0002-comunicacao-assincrona-via-broker.md), [0003](0003-cqrs-e-transactional-outbox.md)

> Este é o ADR central do projeto. Os anteriores descrevem *o que* foi
> construído; este declara *o preço* e quanto ele custa em segundos.

## Contexto

Separar write e read model em bancos diferentes, ligados por fila, tem uma
consequência inevitável: existe um intervalo em que o lançamento já foi aceito
(`201 Created`) e o saldo consolidado ainda não o reflete. Um comerciante que
registre uma venda e consulte o saldo no instante seguinte pode ver o valor
anterior.

Aceitar isso sem número é aceitar sem saber o que se aceitou. "Usei CQRS" e
"escolhi CQRS sabendo o custo" se distinguem exatamente aqui.

## Decisão

A consistência eventual é **aceita como decisão de negócio**, com um limite
declarado como SLO:

> **Lag de consistência (p95) < 5 segundos**, medido no consumer como
> `now() - evento.criadoEm` no instante do `UPSERT`.

E com uma garantia que a acompanha: **convergência sem perda**. O saldo pode
estar atrasado; não pode estar errado depois que a fila drena.

## Decomposição da janela

| Etapa | Contribuição típica | Pior caso normal |
|---|---|---|
| `COMMIT` do lançamento → linha visível na outbox | imediato | imediato |
| Espera até a próxima varredura do publisher | ~1 s (metade do intervalo) | **2 s** (`IntervaloVarredura`) |
| Reserva do lote, publish e publisher confirm | poucos ms | dezenas de ms |
| Entrega pelo broker ao consumer | poucos ms | dezenas de ms |
| Dedupe + `UPSERT` + `COMMIT` | poucos ms | dezenas de ms |
| `SET` no Redis com o valor novo | poucos ms | dezenas de ms |
| **Total** | **~1 s** | **~2,2 s** |

O termo dominante é o intervalo de varredura do publisher, que é configuração
(`RabbitMq:IntervaloVarredura`), não característica do desenho. A folga entre
~2,2 s e o SLO de 5 s existe para absorver contenção e backlog.

**Sob backlog** (Consolidado ou broker de volta após uma queda) a janela é o
tempo de drenagem, não os 2 s acima. O publisher não dorme quando o lote sai
cheio: ele segue direto para o próximo, drenando na velocidade do banco e do
broker. É o SLO nº 10 — RTO abaixo de 5 minutos.

## Por que a medição é no consumer

O caminho ingênuo não funciona. Comparar `lancamentos.criado_em` com
`saldo_diario.atualizado_em` é impossível: os dois campos vivem em bancos
diferentes, e o `atualizado_em` é sobrescrito por cada evento seguinte, sem
corresponder a lançamento nenhum. Por isso o evento **carrega `criadoEm` no
payload** e o consumer calcula o lag no instante em que aplica o `UPSERT`. É um
campo a mais no contrato que transforma um SLO decorativo num número real.

O registro acontece **só no evento efetivamente aplicado**: em duplicata, o
número mediria a reentrega do broker, não a janela de consistência.

## Alternativas consideradas

**Consistência forte (transação distribuída ou banco compartilhado).**
Eliminaria a janela e violaria o RNF-01 no mesmo movimento: qualquer forma de
consistência forte torna a escrita dependente da disponibilidade do read model.
É a troca que o enunciado explicitamente não quer.

**Atualização síncrona do read model dentro da transação de escrita.** Mesma
objeção, com a agravante de reintroduzir contenção de lock entre escrita e
leitura.

**Read-your-writes com leitura do write model no `GET`.** O Consolidado
consultaria o `lancamentos_db` para complementar o saldo quando o lag fosse
detectado. Descartado: recria a dependência entre os serviços justamente no
caminho que precisa continuar respondendo quando o outro lado cai — e sob 50 rps
colocaria carga de leitura no banco que precisa aceitar escrita.

## Consequências

**Positivas.** O read model pode ser reconstruído a partir da outbox sem tocar
no write model. A consulta é um lookup por chave primária numa tabela
pré-agregada, o que sustenta o SLO de p95 abaixo de 100 ms. E a projeção é
imune à ordem de chegada: somar créditos e débitos é comutativo, então
reentrega e entrega fora de ordem convergem para o mesmo saldo.

**Negativas, assumidas.**

- **Não há read-your-writes no consolidado.** Um cliente que precise de
  confirmação imediata deve usar a resposta do próprio `POST` (que devolve o
  lançamento criado) ou o `GET /api/lancamentos`, que lê o write model e é
  fortemente consistente.
- **Resíduo conhecido no cache.** Com múltiplas réplicas do consumer, dois
  eventos do mesmo dia processados em paralelo podem gravar no Redis em ordem
  invertida, deixando o valor anterior na chave. O erro é limitado pelo TTL
  (5 s no dia corrente, 5 min em dias passados) e não afeta o banco, que é a
  fonte da projeção. O caminho de leitura **não** popula o cache justamente para
  não ampliar essa janela.
- **O sistema não detecta divergência sozinho.** O SLI nº 8 (divergência de
  saldo = 0) é verificado por query manual de reconciliação, documentada no
  [runbook](../runbook.md). Promovê-la a job periódico com alerta está em
  evoluções futuras.

## O que torna a decisão verificável

A convergência não é afirmada, é testada:
`ConsolidadoForaDoArNaoImpedeLancamentoEOSaldoConvergeNaVolta` derruba o read
side, exige `201` em todos os lançamentos, confere por SQL que a projeção não
avançou e assere o saldo correto depois que o serviço volta.
