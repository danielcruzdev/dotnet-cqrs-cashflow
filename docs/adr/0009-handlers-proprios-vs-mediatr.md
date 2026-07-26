# ADR 0009 — Handlers próprios em vez de MediatR

**Status:** aceito · **Data:** 26/07/2026

## Contexto

O projeto usa CQRS na camada Application: cada caso de uso é um comando ou uma
query com seu handler. O caminho padrão da comunidade .NET para isso é o
MediatR, que fornece as interfaces, o despacho e um pipeline de behaviors.

Duas coisas pesam na decisão. A primeira é de licenciamento: as versões recentes
do MediatR passaram a ter licenciamento comercial, com faixa gratuita
condicionada ao porte da organização. A segunda é de propósito: num teste
técnico para arquiteto, implementar o padrão demonstra entendimento melhor do
que importar o pacote que o implementa.

## Decisão

Interfaces próprias na camada Application:

```csharp
public interface ICommandHandler<in TComando, TResultado>
{
    Task<TResultado> ExecutarAsync(TComando comando, CancellationToken cancellationToken = default);
}
```

O endpoint injeta `ICommandHandler<CriarLancamentoCommand, ResultadoCriarLancamento>`
diretamente e o contêiner de DI nativo resolve. **Não há mediador.**

## Por que sem mediador, e não "mediador próprio"

A tentação seria escrever um `IMediator.Send(object)` de trinta linhas. Injetar o
handler direto já *é* o padrão Command Handler — o mediador acrescenta uma
indireção cujo custo é concreto: perde-se a checagem em tempo de compilação (o
`Send` recebe `object`), a resolução passa a ser por reflexão no caminho quente,
e o contêiner de DI vira um service locator.

O que se ganharia são os pipeline behaviors. Aqui eles seriam usados para
logging e validação — os dois já cobertos por middleware da plataforma
(`UsarCorrelacao`, `AddValidation`, `IExceptionHandler`).

## Alternativas consideradas

**MediatR.** Padrão de mercado, ecossistema grande, behaviors prontos. Além do
licenciamento, o argumento contra é que a maior parte do seu valor aparece
quando há muitos casos de uso com preocupações transversais repetidas. Este
projeto tem seis.

**Chamar os serviços de aplicação direto do endpoint, sem interface.** Menos
código ainda, e perderia o ponto: o handler nomeado por comando é o que torna o
CQRS visível na estrutura de pastas e o que permite trocar a implementação nos
testes.

## Consequências

**Positivas.** Zero dependência externa na Application (que, como o Domain, não
referencia nenhum pacote NuGet). Resolução verificada em tempo de compilação: um
handler não registrado quebra na inicialização, porque `ValidateOnBuild` está
ligado. Rastreabilidade direta — o endpoint declara exatamente qual caso de uso
executa.

**Negativas, assumidas.** Se surgir uma preocupação transversal que precise
envolver *todos* os handlers (auditoria de comando, por exemplo), será preciso
implementar decoração manualmente — algo que o MediatR daria pronto. Com seis
casos de uso, a conta ainda fecha a favor da simplicidade; com sessenta, não
fecharia.
