# Sucessos semânticos: proposta adiada

Decisão de 02/11/2026. Fora do escopo atual da 0.2.0; reavaliar antes da 1.0 somente se o uso real e a discussão com a comunidade justificarem a funcionalidade.

## Motivo

Ainda não encontramos um caso de uso concreto no qual transportar um `Success` separado resolva uma dificuldade que os tipos do domínio em `TValue` não resolvam melhor. Informação necessária para interpretar ou utilizar o resultado deve aparecer no contrato do valor retornado.

Um `RailFor<User>` acompanhado de `UserCreated` ou `UserAlreadyExists` não informa no tipo do retorno quais variantes existem. Se essa distinção determina o próximo passo, um `RailFor<RegistrationResult>` pode expressá-la diretamente. A biblioteca não precisa definir esse tipo: ele pertence ao domínio consumidor.

A fronteira entre metadados opcionais de execução e informação essencial do resultado continua ambígua. Além disso, sucessos semânticos exigem políticas de neutralidade, composição, substituição e preservação. A correção dessas políticas foi testada, mas sua utilidade ainda não foi demonstrada.

## Onde a implementação está preservada

- Branch: [`archive/semantic-success`](https://github.com/Alguem5381/FlowOnRails/tree/archive/semantic-success).
- Snapshot completo: [`b2474ed`](https://github.com/Alguem5381/FlowOnRails/commit/b2474ed).
- Proposta original: [issue #2 — Add a success record](https://github.com/Alguem5381/FlowOnRails/issues/2).

O snapshot contém todo o estado anterior à separação: implementação, testes e documentos `docs/operation-semantics.md`, `docs/revisao-0.2.0.md` e `docs/analise-sucessos-e-operacoes.md`. Os números de testes e conclusões desses documentos descrevem o experimento arquivado, não o desenvolvimento atual.

Para consultar um arquivo sem trocar a branch de trabalho:

```bash
git show archive/semantic-success:src/FlowOnRails/Success.cs
git show archive/semantic-success:docs/analise-sucessos-e-operacoes.md
```

Não integrar essa branch inteira à linha de desenvolvimento: ela também contém versões anteriores de mudanças que continuaram evoluindo após a separação. Se a proposta voltar, selecionar e adaptar apenas os trechos necessários.

## Contrato do experimento

`Outcome` era a raiz de `Error` e `Success`. `Rail` e `RailFor<T>` podiam transportar um sucesso semântico opcional e independente do valor computacional.

- Um sucesso neutro era representado internamente por `null` e materializado somente quando observado.
- `Map` preservava o significado enquanto transformava o valor.
- `Bind` conservava o significado anterior se o próximo fosse neutro; um próximo sucesso rico substituía o anterior.
- Uma falha interrompia a cadeia e descartava o sucesso semântico anterior.
- Records de domínio eram opacos: um agregado como `SuccessList` não era interpretado nem concatenado automaticamente.

A matriz proposta, parcialmente ainda sem implementação, era:

| Valor final | Preservar o sucesso anterior | Compor com o próximo sucesso |
|---|---|---|
| Valor anterior | `Check` | `BindKeepValue` |
| Próximo valor | `BindKeepSuccess` | `Bind` |

`MapSuccess` substituiria o significado explicitamente. Essas operações sobre sucessos não fazem parte do contrato atual.

## O que continua no desenvolvimento

As correções independentes da proposta foram preservadas: rejeição de rails não inicializados e entradas nulas, construção centralizada, contratos de callbacks, recuperação por valor ou por outro rail, integração LINQ, cadeias `Task`/`ValueTask` e testes pela API pública.

`Success`, `Outcome`, seu singleton neutro, a composição semântica e as sobrecargas específicas foram retirados. `Error` volta a ser um record base independente. As possibilidades de resultado de domínio devem ser expressas pelos tipos escolhidos para `TValue`.

## Perguntas para uma discussão futura

1. Qual fluxo real fica difícil de expressar apenas com `TValue` e `Error`?
2. Quem produz e quem consome a informação adicional? Ela pode ser ignorada com segurança?
3. Por que essa informação precisa atravessar a cadeia, em vez de ser atribuída no resultado final?
4. A composição padrão preserva o significado esperado quando o tipo do valor muda?
5. O benefício justifica ampliar o contrato público antes da 1.0?

A proposta está adiada por falta de evidência de uso, não rejeitada como impossibilidade técnica.
