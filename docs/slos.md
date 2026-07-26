# SLIs, SLOs e análise de capacidade

O enunciado pede "definir métricas e metas claras". Repetir "50 req/s e 5% de
perda" não é definir métrica — é repetir o requisito. Este documento traduz os
requisitos em indicadores mensuráveis, declara a meta de cada um e — o mais
importante — diz **quais estão comprovados nesta entrega e quais são meta de
produção**.

---

## 1. Tabela de SLI/SLO

| # | SLI | SLO | Como é medido | Status nesta entrega |
|---|---|---|---|---|
| 1 | Disponibilidade do `POST /api/lancamentos` | 99,5% mensal na topologia atual; 99,9% com réplica do `lancamentos_db` | % de respostas não-5xx | 🎯 meta de produção |
| 2 | Disponibilidade do `GET /api/consolidado` no pico | ≥ 95% sob 50 rps (error budget de 5%) | % de respostas não-5xx/429 sob carga | 🎯 meta — script k6 não executado |
| 3 | Latência p95 do `GET /api/consolidado` | < 100 ms | histograma sob carga | 🎯 meta — script k6 não executado |
| 4 | Latência p99 do `GET /api/consolidado` | < 300 ms | histograma sob carga | 🎯 meta — script k6 não executado |
| 5 | Latência p95 do `POST /api/lancamentos` | < 150 ms | histograma sob carga | 🎯 meta — script k6 não executado |
| 6 | **Lag de consistência eventual (p95)** | < 5 s | `now() - evento.criadoEm` no consumer, no instante do `UPSERT` | ✅ instrumentado (`cashflow.consolidado.lag_consistencia`) |
| 7 | Perda de eventos | 0 | comparação write × read model | ✅ teste automatizado |
| 8 | Divergência de saldo (integridade) | 0 | query de reconciliação | ⚠️ verificação manual — ver [runbook](runbook.md) |
| 9 | RPO do Consolidado | 0 | outbox retida permite replay total | ✅ teste de resiliência |
| 10 | RTO do Consolidado | < 5 min | tempo de drenagem do backlog | ✅ teste de resiliência |

A última coluna é deliberada. Publicar dez SLOs sem dizer quais têm
instrumentação seria o mesmo tipo de promessa vazia que a matriz de falhas evita
ao declarar os SPOFs. Os SLOs 2 a 5 dependem de um teste de carga com k6, que
está planejado e **não** foi executado nesta entrega — o número que apareceria
aqui seria estimativa, não medição.

### 1.1 Por que a métrica nº 6 é a mais importante

Ela quantifica o preço da decisão arquitetural central. Ao escolher consistência
eventual, aceitou-se que o saldo pode estar defasado; o SLO é o compromisso de
**quanto**. A decomposição da janela (o termo dominante é o intervalo de
varredura do publisher, 2 s) está no
[ADR 0006](adr/0006-consistencia-eventual-aceita.md).

Detalhe de medição que o caminho ingênuo erra: comparar `lancamentos.criado_em`
com `saldo_diario.atualizado_em` é impossível — vivem em bancos diferentes, e o
`atualizado_em` é sobrescrito por cada evento seguinte. Por isso o evento carrega
`criadoEm` no payload.

### 1.2 Sobre o error budget de 5%

Os 5% de perda tolerada não são licença para falhar aleatoriamente. São o
orçamento que autoriza falhar **rápido e de forma especificada** — `429` com
`Retry-After` quando o rate limiter atua, `503` com `Retry-After` quando o
circuit breaker abre, `504` quando o timeout de 2 s estoura — em vez de degradar
até a indisponibilidade total. É o que torna circuit breaker e rate limiting
decisões defensáveis em vez de over-engineering.

O rate limiter, aliás, tem teto de **200 req/s por comerciante** contra um SLO
de 50 rps. É proposital: se ele participasse do orçamento de erro, viraria a
**causa** da perda em vez da proteção contra ela.

---

## 2. Análise de capacidade

### 2.1 A conta

| Grandeza | Valor |
|---|---|
| Pico declarado de leitura | 50 req/s |
| Equivalente diário se sustentado 24 h | ~4,3 milhões de requisições |
| Orçamento de erro | 5% de 50 rps = **2,5 req/s podem falhar** |
| Carga de escrita | não especificada; um comerciante realista lança dezenas a centenas de vezes por dia |
| Formato da consulta | lookup por chave primária `(comerciante_id, data, moeda)` |
| Tamanho do read model com 10 mil comerciantes × 5 anos × 1 moeda | ~18 milhões de linhas, ~2 GB com índice |

### 2.2 A conclusão

**50 req/s é uma carga baixa.** Uma única instância .NET com Minimal API
servindo um lookup por chave primária opera na casa dos **milhares** de req/s,
não das dezenas. Com o cache à frente, boa parte das requisições nem chega ao
banco. Uma tabela de 18 milhões de linhas é irrelevante para um índice B-tree —
são quatro níveis de árvore, com as páginas quentes em memória.

Fazendo a conta pelo outro lado: 50 rps × ~2 ms por consulta = **0,1 conexão
ocupada em média**. A pool padrão do Npgsql tem 100.

Isso **não invalida o design — reposiciona o problema**. O desafio não é de
throughput, é de **isolamento de falha**. E dizer isso tem três efeitos:

1. Demonstra dimensionamento em vez de aplicação de receita por reflexo.
2. **Muda a justificativa do cache e das réplicas.** Eles não existem para dar
   conta do volume (não precisam): existem para proteger a latência de cauda e
   manter o serviço dentro do error budget quando o banco degrada. É a
   justificativa correta, não a de manual.
3. **Autoriza explicitamente o que não foi feito.** Sharding, particionamento,
   read replicas e Kafka seriam soluções para um problema que este sistema não
   tem — e dizer isso é mais forte do que implementá-las.

**Premissa declarada:** "o serviço de consolidado diário recebe 50 requisições
por segundo" está sendo interpretado como 50 rps de *consulta ao relatório*.
Assumir e declarar a premissa é melhor do que deixá-la implícita.

### 2.3 Tetos do caminho assíncrono

| Componente | Teto estimado | Como foi obtido |
|---|---|---|
| Publisher da outbox | ~1.000 eventos/s por réplica | lote de 50 com confirms sequenciais segurando travas de linha, ~50 ms por ciclo com broker local |
| Consumer | centenas de eventos/s por réplica | um `UPSERT` por mensagem, prefetch de 20 |
| Escalabilidade horizontal | publisher e consumer escalam por réplica | `FOR UPDATE SKIP LOCKED` no publisher e `UPSERT` atômico no consumer tornam a concorrência segura sem lock explícito |

Ambos ficam ordens de grandeza acima da carga de escrita estimada. O gargalo
prático da ingestão é o intervalo de varredura, não a vazão.

### 2.4 Escalabilidade horizontal — o que é design e o que é ambiente

A camada de API do Consolidado é **stateless**: sem estado de sessão, sem
afinidade, sem cache local. Escala com múltiplas réplicas atrás de um load
balancer, e o Redis compartilhado mantém o cache coerente entre elas.

O ambiente local sobe **uma instância de cada serviço**. A capacidade descrita
acima é do **design**, não do que o `docker compose` entrega — e vale dizer isso
em vez de desenhar um balanceador que ninguém vai executar.

---

## 3. Que alertas existiriam em produção

Os SLOs acima já definem os limiares; falta a instrumentação (OpenTelemetry +
Prometheus/Grafana está em evoluções futuras). Os alertas seriam:

| Alerta | Condição | Por quê |
|---|---|---|
| Lag de consistência acima do SLO | p95 de `cashflow.consolidado.lag_consistencia` > 5 s por 5 min | o read model está atrasando; provável backlog ou consumer degradado |
| Backlog da outbox crescendo | `COUNT(*) WHERE processado_em IS NULL` > 1.000 e subindo | publisher ou broker com problema |
| Mensagem na DLQ | qualquer mensagem em `consolidado.lancamento-realizado.dlq` | evento envenenado — exige o procedimento do [runbook](runbook.md) |
| Error budget consumido | > 5% de não-2xx no `GET /api/consolidado` em janela de 1 h | o orçamento do mês está sendo gasto rápido demais |
| Circuit breaker aberto | transição para *open* no pipeline de consulta | `consolidado_db` degradado |
| Readiness em falha | `/health/ready` de qualquer serviço falhando > 1 min | o Postgres do serviço está inacessível |

Definir *qual alerta sobre qual métrica* é o que fecha o item de observabilidade
mesmo sem a stack de monitoramento montada.
