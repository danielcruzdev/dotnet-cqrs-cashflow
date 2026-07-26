# Cashflow — Controle de Fluxo de Caixa

Sistema de controle de fluxo de caixa diário para comerciantes, composto por dois serviços independentes: um para registro de lançamentos (débitos e créditos) e outro para consulta do saldo diário consolidado.

> 🚧 Projeto em desenvolvimento. Este README será expandido conforme as etapas forem concluídas.

## Contexto

O sistema atende dois casos de uso simples — registrar um lançamento e consultar o saldo do dia — sob uma restrição arquitetural que define todo o desenho da solução:

> **O serviço de controle de lançamentos não pode ficar indisponível se o serviço de consolidado diário cair.**

É essa restrição, e não a complexidade dos casos de uso, que justifica a separação em dois serviços com bancos independentes e comunicação assíncrona. Sem ela, uma única aplicação com duas tabelas resolveria o problema.

Um segundo requisito complementa o desenho: em dias de pico, o serviço de consolidado recebe 50 requisições por segundo, tolerando até 5% de perda. Essa tolerância é tratada como um *error budget* explícito, que autoriza degradação controlada em vez da busca por disponibilidade total.

## Arquitetura

Microsserviços com CQRS e comunicação orientada a eventos. O serviço de Lançamentos é o *write model* e a fonte da verdade; o Consolidado mantém uma projeção agregada (*read model*) atualizada de forma assíncrona.

```
Cliente ──POST──> [Lançamentos API] ──> lancamentos_db (+ outbox)
                                              │
                                    [Outbox Publisher]
                                              │
                                          RabbitMQ
                                              │
                                        [Consumer] ──> consolidado_db
                                              │              │
Cliente ──GET───> [Consolidado API] ────── Redis ────────────┘
```

A única ligação entre os dois domínios é a fila de mensagens. Não existe chamada síncrona do serviço de Lançamentos para o de Consolidado — é isso que torna o isolamento de falha estrutural, e não apenas uma convenção de código.

## Stack

.NET 10 (C# 14) com Minimal API · Dapper · PostgreSQL · RabbitMQ · Redis · Polly · Docker Compose · xUnit com Testcontainers

## Estrutura do repositório

```
/src
  /lancamentos-service      Serviço de lançamentos (write model)
  /consolidado-service      Serviço de consolidado diário (read model)
  /tests-e2e                Testes ponta a ponta e de resiliência
/docs
  /adr                      Architecture Decision Records
  arquitetura.md            Diagramas C4 e fluxos de dados
  slos.md                   Métricas, metas e análise de capacidade
  runbook.md                Procedimentos operacionais
/load                       Testes de carga (k6)
```

Cada serviço segue a mesma organização em camadas — `Api`, `Application`, `Domain` e `Infrastructure` — com a regra de dependência apontando sempre para o domínio.

## Como rodar

```bash
docker compose up
```

## Como testar

```bash
# Testes de unidade do domínio — não precisam de Docker
dotnet test src/lancamentos-service/Lancamentos.slnx
dotnet test src/consolidado-service/Consolidado.slnx

# Testes ponta a ponta — sobem Postgres, RabbitMQ e Redis via Testcontainers
dotnet test src/tests-e2e/Cashflow.E2E.slnx
```

Os testes E2E não dependem do `docker compose up`: eles sobem a própria
infraestrutura, aplicam os mesmos arquivos de schema e a mesma topologia de
broker que o compose usa e hospedam as duas APIs em processo. Só é preciso ter
um Docker em execução.

### O teste de resiliência

`ResilienciaTests` é o teste que prova o requisito âncora em vez de argumentar
sobre ele. Ele derruba o serviço de Consolidado — API e consumer caem juntos,
porque o consumer roda no mesmo processo —, registra cinco lançamentos e exige
`201` em todos, confere por SQL que a projeção não avançou, sobe o serviço de
volta e espera o saldo convergir. A segunda variante faz o mesmo com o RabbitMQ
fora do ar: os eventos ficam retidos na outbox e são publicados quando o broker
volta, sem perda.

```bash
dotnet test src/tests-e2e/Cashflow.E2E.slnx --filter FullyQualifiedName~ResilienciaTests
```

> Instruções detalhadas de execução e configuração serão adicionadas conforme o desenvolvimento avança.

## Documentação

As decisões arquiteturais estão registradas como ADRs em [`docs/adr/`](docs/adr/), com o contexto, as alternativas avaliadas e as consequências de cada escolha.

## Licença

[MIT](LICENSE)
