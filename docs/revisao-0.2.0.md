# Revisão da 0.2.0

Atualizado em 02/11/2026. Este checklist descreve o desenvolvimento sem sucessos semânticos. A avaliação anterior completa está preservada no [snapshot do experimento](https://github.com/Alguem5381/FlowOnRails/tree/archive/semantic-success/docs).

## Decisão de escopo

`Success` foi adiado por falta de um caso de uso concreto e pela ambiguidade em relação a `TValue`. A justificativa, a implementação arquivada e as perguntas para reavaliação estão na [nota de design](design/semantic-success.md).

A pendência de composição de sucessos ricos em `Combine` deixa de se aplicar. Seu contrato atual é reunir os valores em ordem ou interromper no primeiro erro.

## Correções e contratos preservados

- [x] Rejeitar `default(Rail)` e `default(RailFor<T>)` ao observar ou operar.
- [x] Rejeitar valores e erros nulos na construção.
- [x] Centralizar construção e validação de estado.
- [x] Rejeitar rails não inicializados devolvidos por `Bind` e `RecoverWith`.
- [x] Separar recuperação por valor (`Recover`) de fallback por outro rail (`RecoverWith`).
- [x] Preservar a família `ValueTask` ao longo das extensões.
- [x] Remover ambiguidade de lambdas `async` com a ponte explícita `BindValueTaskAsync`.
- [x] Testar espera real, propagação de exceções, cancelamento e consumo único de ValueTask.
- [x] Verificar callbacks e preservação de valores e erros em sucesso, falha e estado não inicializado.
- [x] Verificar LINQ, enumeração adiada e descarte do enumerador na primeira falha de `Combine`.
- [x] Preservar testes das leis de `Bind` e `Map` para callbacks puros.
- [x] Manter o exemplo `GetUser` do README compilado em um projeto sem acesso a membros internos.
- [x] Documentar alterações incompatíveis com a API da 0.1.0 no README.

## Antes da publicação

- [x] Implementar `Check` no núcleo síncrono para executar uma operação falível, preservar o valor anterior em sucesso e propagar o erro em falha. `BindKeepValue` seria redundante sem sucessos semânticos.
- [ ] Avaliar novas operações e versões assíncronas a partir de usos reais.
- [ ] Concluir revisão dos comentários XML da API.
- [ ] Confirmar execução da integração contínua para a versão que será publicada.
- [ ] Publicar a versão somente após revisão do pacote e da documentação final.

Os registros antigos de cobertura e de 760 testes pertencem à versão com sucessos semânticos. A contagem atual muda porque os cenários específicos dessa proposta foram arquivados e os contratos restantes foram adaptados.

## Validação desta separação

Em 02/11/2026, a suíte em Release aprovou **506 cenários**: 76 no projeto original e 430 no projeto consumidor da API pública, sem falhas nem testes ignorados.

O pacote local `FlowOnRails.0.2.0.nupkg` também foi gerado e executado em um consumidor independente, sem referência ao projeto. Foram verificados tipos de domínio em `TValue`, identidade do erro, lambdas Task sem casts, encadeamento ValueTask, recuperação, rejeição de `default` e ausência dos tipos `Success` e `Outcome` no pacote. Isso não representa publicação no NuGet.

A [issue #2](https://github.com/Alguem5381/FlowOnRails/issues/2) foi fechada como `not_planned`, com a justificativa do adiamento e referência à branch de arquivo publicada.

## Adição de Check

Ainda em 02/11/2026, foram adicionadas quatro sobrecargas síncronas de `Check`, cruzando origens e callbacks com ou sem valor. A validação passou com **547 cenários** (76 no projeto original e 471 no consumidor), incluindo 41 novos cenários de preservação do valor, identidade do erro, interrupção, estado não inicializado, exceções e inferência de tipos com method groups.
