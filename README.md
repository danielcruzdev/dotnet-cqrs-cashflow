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

> Instruções detalhadas de execução, testes e configuração serão adicionadas conforme o desenvolvimento avança.

## Documentação

As decisões arquiteturais estão registradas como ADRs em [`docs/adr/`](docs/adr/), com o contexto, as alternativas avaliadas e as consequências de cada escolha.

## Licença

[MIT](LICENSE)
