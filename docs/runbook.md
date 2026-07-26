# Runbook operacional

Procedimentos para os modos de falha previstos. Cada seção diz **como detectar**,
**o que fazer** e **o que não fazer**.

Conexões usadas nos exemplos (ambiente local do `docker compose`):

```bash
# banco de Lançamentos
docker exec -it cashflow-lancamentos-db psql -U cashflow -d lancamentos_db

# banco de Consolidado
docker exec -it cashflow-consolidado-db psql -U cashflow -d consolidado_db

# console do RabbitMQ: http://localhost:15672  (cashflow / cashflow_dev)
```

---

## 1. Diagnóstico rápido

| Sintoma | Primeiro lugar para olhar |
|---|---|
| `POST /api/lancamentos` falhando | `/health/ready` do Lançamentos — se falhar, é o `lancamentos_db` (SPOF assumido) |
| Saldo parado no tempo | backlog da outbox (§2) e depois a fila no console do RabbitMQ |
| `GET /api/consolidado` devolvendo `503` | circuit breaker aberto — o `consolidado_db` está lento ou fora |
| `GET` devolvendo `504` | timeout de 2 s no banco de leitura |
| `GET` devolvendo `429` | rate limiter — mais de 200 req/s para o mesmo comerciante |
| Saldo divergente do esperado | reconciliação (§4) |

Rastreamento ponta a ponta: toda resposta ecoa `X-Correlation-Id`, e o mesmo id
aparece em **todas** as linhas de log da requisição e do processamento da
mensagem correspondente no consumer. Ele é o ponto de partida de qualquer
investigação.

---

## 2. Backlog da outbox

**Detectar.** No `lancamentos_db`:

```sql
SELECT count(*) AS pendentes,
       min(criado_em) AS mais_antigo,
       max(tentativas) AS pior_tentativa
FROM outbox_messages
WHERE processado_em IS NULL;
```

Backlog é **normal e esperado** enquanto o broker está fora — é exatamente o que
a outbox existe para fazer. Vira incidente quando o broker está no ar e o número
não cai.

**Se o broker está fora.** Não fazer nada: o publisher retenta com backoff
exponencial (teto de 30 s) e falha de conexão **não** consome o orçamento de
tentativas por mensagem. Assim que o RabbitMQ volta, a fila drena sozinha — o
publisher não dorme entre ciclos quando o lote sai cheio.

**Se o broker está no ar e o backlog não cai.** Investigar mensagens que
esgotaram tentativas:

```sql
SELECT id, event_id, tipo_evento, tentativas, criado_em
FROM outbox_messages
WHERE processado_em IS NULL AND tentativas >= 10
ORDER BY criado_em
LIMIT 20;
```

O motivo da falha não fica na tabela — está no log do serviço de Lançamentos.
Falha de publicação é logada com o `event_id`; tipo de evento desconhecido é
logado com o `id` da linha da outbox. As duas colunas estão no `SELECT` acima,
então vale procurar pelos dois valores.

O teto é de 10 tentativas (`OutboxRepository.MaximoTentativas`); acima dele o
publisher para de tentar. Tentativa esgotada indica falha da mensagem em si (payload inválido, tipo de
evento desconhecido), não do ambiente. Corrigida a causa, zerar o contador
republica:

```sql
UPDATE outbox_messages SET tentativas = 0 WHERE id = '<id>';
```

---

## 3. Mensagens na DLQ

Uma mensagem chega em `consolidado.lancamento-realizado.dlq` quando o consumidor
faz `nack` sem requeue — o que só acontece para payload que **não melhora
sozinho** (evento malformado, tipo desconhecido, valor não positivo). Falha de
ambiente (banco fora) volta para a fila original com requeue.

**Detectar.** Console do RabbitMQ → Queues → `consolidado.lancamento-realizado.dlq`,
ou alerta sobre profundidade da fila.

**Procedimento.**

1. **Inspecionar sem consumir.** No console, *Get messages* com
   `Ack Mode: Nack message requeue true`. O payload é JSON legível — foi por isso
   que o formato binário foi descartado no
   [ADR 0005](adr/0005-rest-json-vs-grpc-protobuf.md).
2. **Classificar.** Bug do produtor, bug do consumidor, ou dado que nunca deveria
   ter sido aceito? A resposta muda o passo seguinte.
3. **Corrigir a causa** e fazer o deploy antes de republicar. Republicar contra o
   mesmo código produz o mesmo resultado.
4. **Republicar.** O caminho preferido é a própria outbox, que continua sendo a
   fonte da verdade — zerar `tentativas` da linha correspondente (§2) e deixar o
   publisher reenviar. Republicar direto da DLQ pelo console exigiria habilitar
   o plugin `rabbitmq_shovel` (a imagem `-management` não o traz), e ainda
   passaria por cima do registro de tentativas — por isso a outbox é o caminho.
5. **Purgar a DLQ** só depois de confirmar que o saldo convergiu (§4).

**Não fazer:** purgar a DLQ antes de inspecionar. A mensagem é a única evidência
do defeito.

### Dívida conhecida — redelivery sem teto

Uma falha **permanente** do banco de leitura (por exemplo, uma constraint
violada por dado inesperado) faz o consumidor devolver a mensagem à fila
indefinidamente, porque falha de banco é classificada como transitória. O
`x-death` da mensagem daria a contagem de reentregas e permitiria mandar para a
DLQ depois de N tentativas. Não implementado nesta versão; se o sintoma aparecer
(uma mensagem circulando sem parar, log de erro repetindo o mesmo `eventId`), o
contorno é parar o consumidor, corrigir a causa e religar.

---

## 4. Reconciliação — verificar integridade do saldo

É o SLI nº 8 ([slos.md](slos.md)): divergência de saldo deve ser zero. Como os
dois lados vivem em bancos diferentes, a verificação são duas consultas
comparadas.

**No `lancamentos_db`** (a verdade):

```sql
SELECT data_competencia AS data,
       moeda,
       SUM(CASE WHEN tipo = 'DEBITO'  THEN valor ELSE 0 END) AS total_debitos,
       SUM(CASE WHEN tipo = 'CREDITO' THEN valor ELSE 0 END) AS total_creditos,
       SUM(CASE WHEN tipo = 'CREDITO' THEN valor ELSE -valor END) AS saldo,
       COUNT(*) AS qtd_lancamentos
FROM lancamentos
WHERE comerciante_id = '<comerciante>'
  AND data_competencia BETWEEN '<de>' AND '<ate>'
GROUP BY data_competencia, moeda
ORDER BY data_competencia, moeda;
```

**No `consolidado_db`** (a projeção):

```sql
SELECT data, moeda, total_debitos, total_creditos, saldo, qtd_lancamentos
FROM saldo_diario
WHERE comerciante_id = '<comerciante>'
  AND data BETWEEN '<de>' AND '<ate>'
ORDER BY data, moeda;
```

As duas devem coincidir linha a linha. Diferença em `qtd_lancamentos` aponta
evento perdido ou duplicado; diferença só nos valores aponta bug de projeção.

**Antes de concluir que há divergência:** conferir se a fila está vazia e se o
backlog da outbox é zero. Com evento em trânsito, a diferença é a janela de
consistência eventual funcionando como projetado, não um defeito.

Promover isso a job periódico com alerta está em evoluções futuras. Como query
documentada, já responde ao requisito de integridade.

---

## 5. Reconstruir o read model

Necessário quando a projeção foi corrompida por um bug de consumo, ou quando o
`consolidado_db` foi perdido. É possível porque **as linhas da outbox não são
deletadas** após publicadas — ela é o log de eventos do sistema.

> **O passo 1 não é opcional.** A reconstrução apaga as marcas de dedupe do
> período. Se o mesmo evento estiver simultaneamente no lote de replay e sendo
> entregue ao vivo pelo broker, ele é somado **duas vezes**: o `UPSERT` é
> idempotente por `eventId`, não por conteúdo — apagar o dedupe remove
> exatamente a proteção que impediria isso.

1. **Parar o consumidor de tráfego ao vivo.**

   ```bash
   docker compose stop consolidado-api
   ```

2. **Apagar a projeção do período**, no `consolidado_db`:

   ```sql
   DELETE FROM saldo_diario
   WHERE comerciante_id = '<comerciante>'
     AND data BETWEEN '<de>' AND '<ate>';
   ```

   `DELETE` com predicado, não `TRUNCATE` — que no PostgreSQL não aceita `WHERE`.

3. **Apagar as marcas de dedupe dos eventos do período.** A tabela só tem o
   `event_id`, então a lista vem da outbox, no `lancamentos_db`:

   ```sql
   SELECT event_id
   FROM outbox_messages
   WHERE comerciante_id = '<comerciante>'
     AND data_competencia BETWEEN '<de>' AND '<ate>';
   ```

   ```sql
   -- no consolidado_db, com a lista obtida acima
   DELETE FROM eventos_processados WHERE event_id IN (...);
   ```

   **O recorte é por `data_competencia`, não por `criado_em`.** Filtrar pela data
   física deixaria de fora exatamente os casos que mais precisam ser recuperados:
   o lançamento retroativo e o estorno de um dia antigo, ambos gravados hoje mas
   pertencentes ao período em reconstrução. O resultado seria um saldo menor que
   o correto, **sem nenhum erro visível**. É por isso que a outbox promove
   `comerciante_id` e `data_competencia` a colunas indexadas em vez de deixá-las
   apenas dentro do JSONB.

4. **Republicar**, marcando as linhas como pendentes no `lancamentos_db`:

   ```sql
   UPDATE outbox_messages
   SET processado_em = NULL, tentativas = 0
   WHERE comerciante_id = '<comerciante>'
     AND data_competencia BETWEEN '<de>' AND '<ate>';
   ```

5. **Religar o consumidor** e aguardar a drenagem:

   ```bash
   docker compose start consolidado-api
   ```

6. **Validar** com a reconciliação (§4).

**Alternativa sem janela de indisponibilidade:** reconstruir em uma tabela
*shadow* e trocar as duas atomicamente. Mais elegante, e o caminho certo se isso
virar procedimento rotineiro em produção. Está em evoluções futuras.

---

## 6. Cache

O Redis guarda `cashflow:consolidado:{comercianteId}:{moeda}:{data}`, escrito
**apenas pelo consumer** após cada `UPSERT`, com TTL de 5 s no dia corrente e
5 min em dias passados. O prefixo `cashflow:` vem do `InstanceName` do
`IDistributedCache` e não aparece na chave que o código monta — quem for
inspecionar o Redis à mão precisa dele.

- **Cache fora do ar não é incidente de disponibilidade.** Leitura e escrita
  degradam para o banco com log de `Warning`; por isso o Redis está fora do
  `/health/ready`.
- **Invalidação manual** (após uma reconstrução, por exemplo):

  ```bash
  docker exec -it cashflow-redis redis-cli --scan --pattern 'cashflow:consolidado:<comerciante>:*' \
    | xargs -r docker exec -i cashflow-redis redis-cli DEL
  ```

  Apagar é seguro: a chave ausente cai no banco.
- **Resíduo conhecido:** com múltiplas réplicas do consumer, dois eventos do
  mesmo dia processados em paralelo podem gravar em ordem invertida, deixando o
  valor anterior na chave até o TTL. O banco nunca fica errado.

---

## 7. Queda de dependências — o que esperar

| Componente fora | Escrita | Leitura | Ação |
|---|---|---|---|
| `consolidado-api` (API + consumer) | **normal** | indisponível | subir; o saldo converge sozinho |
| RabbitMQ | **normal** | serve dado até o último evento | subir; a outbox drena sozinha |
| `consolidado_db` | **normal** | cache até o TTL, depois `503` | subir; o consumer reconecta |
| Redis | **normal** | normal, mais lenta | subir quando puder — não é urgente |
| `lancamentos_db` | **indisponível** ⚠️ | normal | **SPOF assumido** — a mitigação real é réplica com failover, fora do escopo desta entrega |

Nenhuma linha da coluna "Escrita" depende de outro serviço — é o RNF-01 expresso
em procedimento operacional, e é o que os testes de resiliência verificam.
