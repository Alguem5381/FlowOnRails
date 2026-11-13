# Correções da auditoria de análise e API

Os quatro defeitos confirmados na [auditoria independente](2026-11-12-analysis-api-audit.md)
foram corrigidos. A auditoria original permanece como registro do estado anterior;
sua saída foi preservada em
`tests/FlowOnRails.Analysis.AuditExample/run-output.before-fixes.txt`.
Nenhum pacote foi publicado nesta etapa.

## Correções do motor e do editor

| Achado | Comportamento corrigido | Cobertura de regressão |
| --- | --- | --- |
| Efeitos no receptor da escrita | Receptores e índices são avaliados antes do valor atribuído. Uma escrita simples não executa o getter. Escritas compostas e incrementos leem o destino. A desestruturação esquece apenas os destinos sobrescritos. | Propriedades, campos, arrays, indexadores, incremento, atribuição composta, tuplas e ordem entre os efeitos dos dois lados; fonte e metadados |
| Teste de tipo genérico | Relações com parâmetros ainda não substituídos preservam as alternativas. A condição fica explicitamente indeterminada. | Definição genérica, chamada local especializada e método construído de assembly externo |
| Task nulo e metadados incompatíveis | Um Task sabidamente nulo não produz resultado após aguardá-lo; a forma awaitable é preservada. O retorno raiz é validado contra o tipo declarado. InvalidDataException ao ler metadados degrada para resumo aberto. | null/default de Task, Error nulo dentro de Task válido, metadados com forma incompatível, API e analisador no consumidor |
| Cobertura de switch por interface | A cobertura usa conversões de referência do Roslyn, incluindo interfaces e variância. Só acrescenta casos ainda não cobertos e preserva compilação/fallback. | Switch statement e expression, interface comum e interface genérica covariante; compilação após aplicar a ação |

Um resumo externo de método genérico ainda pode ser mais amplo que uma chamada
local especializada. O formato atual não guarda um predicado completo sobre
argumentos de tipo: por isso preserva as alternativas viáveis e informa a condição
indeterminada. Esta correção evita a omissão; não promete a mesma precisão em todos
os pontos de consulta.

Os comentários novos explicam a ordem de avaliação dos destinos e a diferença
entre não provar uma relação genérica e provar que os tipos são incompatíveis.
A relação de classes usada internamente deixou de ser usada para cobertura de
padrões do editor.

## API e mensagens

`ErrorFlowAnalysis.CanAnalyze(IMethodSymbol?)` permite testar a origem do símbolo
e o suporte ao retorno sem interpretar o corpo ou capturar exceções. Null, símbolos
de outra compilação e retornos não suportados dão false. Interfaces e corpos
indisponíveis continuam consultáveis e produzem resumos conservadores.
A documentação pública agora declara que consultas concorrentes no mesmo contexto
são suportadas; o contexto continua pertencendo a uma compilação imutável.

O analisador de exemplo usa CanAnalyze e deixou de duplicar o desembrulho dos
retornos. Seu contrato experimental aceita métodos públicos com retornos suportados
pela API, incluindo Error e wrappers Task/ValueTask. A implementação de endpoints
IActionResult/IResult continua fora do experimento.

As mensagens passaram a distinguir “This call” de “This method”, eliminar a
segunda frase redundante nos conjuntos fechados vazios e identificar a ausência
de resultado normal. O aviso de Rail não inicializado usa “also” somente quando
há outra alternativa. Duas limitações de chamadas foram reescritas para descrever
o efeito observado pelo usuário, em vez do mecanismo interno.

Na etapa das correções acima, ficaram como oportunidades: TryCreate/TryAnalyze, classificação estável
das limitações, origem estruturada do resumo e consulta contextual pública de
invocações. Elas não fazem parte da correção dos quatro defeitos e não foram
adicionadas à API naquela etapa. TryCreate/TryAnalyze, códigos e origem foram
implementados posteriormente; o contrato atual está no README de FlowOnRails.Analysis.

## Verificação

- Build Release da solução: zero avisos e zero erros.
- Suíte completa: **1.295 testes aprovados**, incluindo **337 do analisador/API**.
- Foram acrescentados **22 casos de regressão** à suíte da solução.
- Experimento público: seis avisos de mapeamento ausente e um de contrato aberto;
  casos mapeados, recuperados e Error como sucesso continuam sem aviso indevido.
- As quatro reproduções originais agora validam a semântica corrigida e terminam
  com código zero; nenhum AD0001 e nenhum CS8510.
- Consumidor do compilador real: build aprovado com os dois AUDIT001 intencionais.
- Quatro consumidores isolados dos pacotes locais aprovados: base, ASP.NET,
  analisador com produtor externo e API pública incluindo CanAnalyze. Foram
  verificados diagnósticos, exportação, leitura, opt-out e ausência de dependência
  de execução Roslyn nas aplicações.

A saída atual está em `tests/FlowOnRails.Analysis.AuditExample/run-output.txt`.
Os testes da solução estão em
`tests/FlowOnRails.Analyzers.Tests/ApiAuditRegressionTests.cs`, além da atualização
das expectativas de mensagens nas suítes existentes.

Comandos principais, a partir da raiz (a pasta temporária de CLI acomoda as
permissões deste ambiente):

```sh
DOTNET_CLI_HOME=/tmp/flowonrails-audit-cli dotnet build FlowOnRails.slnx -c Release --no-restore --disable-build-servers -m:1
DOTNET_CLI_HOME=/tmp/flowonrails-audit-cli dotnet test FlowOnRails.slnx -c Release --no-build --no-restore --disable-build-servers -m:1
DOTNET_CLI_HOME=/tmp/flowonrails-audit-cli dotnet build tests/FlowOnRails.Analysis.AuditExample/FlowOnRails.Analysis.AuditExample.csproj --no-restore --disable-build-servers -m:1
DOTNET_CLI_HOME=/tmp/flowonrails-audit-cli dotnet tests/FlowOnRails.Analysis.AuditExample/bin/Debug/net10.0/FlowOnRails.Analysis.AuditExample.dll
DOTNET_CLI_HOME=/tmp/flowonrails-audit-cli dotnet build tests/FlowOnRails.Analysis.AuditExample/CompilerConsumer/CompilerConsumer.csproj --no-restore --disable-build-servers -m:1
DOTNET_CLI_HOME=/tmp/flowonrails-audit-cli DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1 python3 scripts/verify-packages.py ./artifacts/error-flow-0.3.0 --tooling-dependency-feed /tmp/flowonrails-tooling-dependencies
```

A pasta de dependências do último comando contém links para arquivos .nupkg
disponíveis no cache local, usados como feed offline. Os quatro pacotes
FlowOnRails foram recompostos localmente em Release; o verificador usa um cache
novo para cada execução. O empacotamento com referências de projeto exigiu
`-m:1` neste ambiente, como o build da solução.

Essa verificação não é uma nova auditoria independente, nem prova de exaustividade.
Permanecem os limites conservadores documentados do motor e o requisito de manter
fallback para erros desconhecidos. Publicação e adaptação do satélite HTTP não
foram executadas.
