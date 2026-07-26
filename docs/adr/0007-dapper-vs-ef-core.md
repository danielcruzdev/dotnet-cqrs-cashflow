# ADR 0007 — Dapper em vez de EF Core

**Status:** aceito · **Data:** 26/07/2026

## Contexto

Os dois serviços têm padrões de acesso a dados bem definidos e pouco numerosos.
No Lançamentos: um `INSERT` duplo transacional, uma consulta por chave de
idempotência, uma listagem paginada e a reserva da outbox com
`FOR UPDATE SKIP LOCKED`. No Consolidado: um `INSERT ... ON CONFLICT DO NOTHING`,
um `INSERT ... ON CONFLICT DO UPDATE ... RETURNING` e dois `SELECT`.

Três dessas construções — `SKIP LOCKED`, `ON CONFLICT DO UPDATE` e `RETURNING` —
são específicas do PostgreSQL e centrais para as garantias do sistema, não
detalhes de otimização.

## Decisão

**Dapper + Npgsql**, com SQL escrito à mão, em ambos os serviços. Repository +
Unit of Work sobre uma `NpgsqlDataSource` compartilhada.

## Alternativas consideradas

**EF Core.** Traz migrations, change tracking, LINQ tipado e produtividade real
em CRUD. Nada disso é o que este projeto precisa, e duas coisas atrapalham:

- O `UPSERT` atômico é a garantia de idempotência do read model. Em EF Core ele
  vira `ExecuteSql` cru ou um pacote de terceiros — ou seja, o ORM é
  contornado justamente no ponto mais importante.
- O `FOR UPDATE SKIP LOCKED` do publisher não tem equivalente em LINQ. Sem ele,
  duas réplicas reservariam o mesmo lote e publicariam duplicado.

Adotar EF Core significaria usá-lo para o trivial e escapar dele para o crítico
— a pior combinação, porque paga o custo da abstração sem receber o benefício
onde ele importaria.

**ADO.NET puro.** Elimina até a dependência do Dapper, ao custo de mapeamento
manual de `DataReader` em todo repositório. Dapper é uma camada fina sobre
exatamente esse código, com o mesmo modelo mental.

## Consequências

**Positivas.** O SQL é legível, revisável e testável de forma independente — o
que permitiu validar cada comando por execução contra um PostgreSQL real antes
mesmo de a aplicação compilar. Nenhuma query surpresa: o que está no arquivo é o
que vai para o banco. Consultas parametrizadas eliminam injeção de SQL sem
depender de disciplina.

**Negativas, assumidas.**

- **Sem migrations.** O schema é aplicado por `init/01-schema.sql` montado no
  `/docker-entrypoint-initdb.d/` de cada Postgres, o que funciona para subir do
  zero e **não** cobre evolução de schema em produção. DbUp ou Flyway está em
  evoluções futuras.
- Mapeamento por convenção exige configuração explícita: `MatchNamesWithUnderscores`
  está ligado, sem o qual `data_competencia` não encontraria `DataCompetencia` e
  o valor chegaria `default` **sem nenhum erro** — falha silenciosa, a pior
  categoria.
- O SQL fica acoplado ao PostgreSQL. É uma troca deliberada: as construções
  específicas do dialeto são a razão da escolha, não um efeito colateral dela.
