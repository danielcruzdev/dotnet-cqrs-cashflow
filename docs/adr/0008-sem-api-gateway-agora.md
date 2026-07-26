# ADR 0008 — Sem API Gateway nesta versão

**Status:** aceito · **Data:** 26/07/2026

## Contexto

A arquitetura tem dois serviços expostos ao cliente, em portas diferentes
(5001 e 5002). O reflexo comum em microsserviços é colocar um gateway na
frente — YARP, Ocelot, Kong — para centralizar autenticação, rate limiting,
roteamento e TLS.

## Decisão

**Nenhum gateway.** Os dois serviços são expostos diretamente. Autenticação JWT,
autorização por recurso, rate limiting, CORS e security headers ficam em cada
serviço, com o middleware nativo do ASP.NET Core.

## Justificativa

Um gateway resolve problemas que aparecem em escala: dezenas de serviços com
políticas divergentes, necessidade de roteamento dinâmico, agregação de
respostas, terminação TLS única. Com **dois** serviços, ele adiciona um salto
de rede na latência, um componente a operar e — o ponto decisivo — **um ponto
único de falha na frente de um sistema cujo requisito principal é isolamento
de falha**.

Um gateway fora do ar derruba os dois serviços simultaneamente, incluindo o de
Lançamentos, que existe para continuar aceitando escrita quando o outro cai.
Reintroduzir um SPOF compartilhado justamente aqui contradiz o RNF-01.

Há ainda um argumento de coerência: as políticas que o gateway centralizaria
não são idênticas nos dois serviços. Rate limiting existe **só** no Consolidado —
limitar a escrita contradiz o requisito âncora. CORS e security headers também
são só do lado de leitura. Um gateway com regras diferentes por rota é
configuração distribuída em outro lugar, não simplificação.

## Alternativas consideradas

**YARP como reverse proxy no compose.** Daria porta única e um lugar só para
TLS. Descartado pelo SPOF e porque, no ambiente local, portas distintas são mais
transparentes para quem avalia — fica visível que são dois serviços de verdade.

**Ingress do Kubernetes.** É a resposta certa quando o deploy for para
Kubernetes, e aí o gateway deixa de ser componente próprio e passa a ser
infraestrutura da plataforma, com alta disponibilidade dada. Fora do escopo
desta entrega.

## Consequências

**Positivas.** Menos infraestrutura, menos latência, nenhum SPOF adicional. Cada
serviço é autossuficiente em segurança, o que também significa que continua
seguro se for exposto por outro caminho.

**Negativas, assumidas.** A configuração de JWT é duplicada nos dois serviços
(mesmo issuer, audience e chave). O cliente precisa conhecer dois endereços.
E, quando um terceiro serviço aparecer, a duplicação começa a doer — é o
gatilho natural para revisitar esta decisão.

**Quando reabrir:** ao chegar em três ou mais serviços expostos, ou ao precisar
de política de acesso centralizada (por exemplo, revogação de token em tempo
real), o gateway passa a se pagar.
