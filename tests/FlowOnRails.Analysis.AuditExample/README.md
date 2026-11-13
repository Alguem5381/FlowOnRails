# Experimento da auditoria de 2026-11-12

Este diretório é isolado da solução e não implementa a futura integração ASP.NET.

`Analyzer/` contém um `DiagnosticAnalyzer` real, `netstandard2.0`, compilado com
as regras estendidas do Roslyn. Usa somente `ErrorFlowAnalysis`, o resultado
público dessa API e símbolos Roslyn. Não lê textos dos diagnósticos, propriedades
dos Infos, payloads nem tipos internos. O projeto não é assembly amigo do motor.

Contrato mínimo: `[AuditController]` seleciona a classe; `[MapsError(typeof(T))]`
declara um mapeamento de T e seus descendentes. Actions são métodos públicos
ordinários com retorno suportado pela API: Rail/RailFor/Error e Task/ValueTask.
`TryCreate` ignora projetos sem a biblioteca; `TryAnalyzeMethod` faz a seleção e
a consulta sem repetir as regras dos wrappers no satélite. O exemplo
consulta o retorno da action; esta pode delegar a um handler local ou externo.
Não acompanha conversão para `IActionResult`, filtros, herança dos atributos,
roteamento ou uma política HTTP real. Não valida o atributo de mapeamento como
um produto completo faria. Famílias abertas mantêm o diagnóstico de fallback,
mesmo quando seu limite conhecido está mapeado.

`Program.cs` executa o analisador com `Compilation.WithAnalyzers`, reutilizando
o contexto por compilação e habilitando concorrência. Gera um assembly produtor
com o gerador real e o consome por referência de metadados. Verifica seis avisos
de mapeamento ausente e um aviso de contrato aberto; casos mapeados, recuperados
e Error como valor de sucesso não geram aviso de mapeamento.

`AuditProbes.cs` verifica a semântica esperada após as correções dos quatro
achados: efeitos nos receptores, alternativas genéricas preservadas, Task nulo
sem resultado aguardado e casos de switch sem braços inalcançáveis. AD0001 é
rejeitado em todos os cenários. A cobertura de metadados inválidos também está
na suíte `ApiAuditRegressionTests.cs` da solução.

`CompilerConsumer/` usa o analisador no compilador de verdade, sem referenciar
Roslyn ou a API como dependência da aplicação. As duas referências com
`OutputItemType="Analyzer"` registram tanto o satélite quanto sua dependência
no carregador. Referenciar apenas o projeto satélite causou AD0001 por DLL da
análise ausente; colocar a DLL entre os assets de analyzer resolve o carregamento.
Em um pacote NuGet, ambas devem estar em `analyzers/dotnet/cs`.

Executar a partir da raiz do repositório:

```sh
dotnet run --project tests/FlowOnRails.Analysis.AuditExample/FlowOnRails.Analysis.AuditExample.csproj
dotnet build tests/FlowOnRails.Analysis.AuditExample/CompilerConsumer/CompilerConsumer.csproj
dotnet test tests/FlowOnRails.Analyzers.Tests/FlowOnRails.Analyzers.Tests.csproj --no-restore --verbosity quiet
```

Quando a pasta de inicialização do SDK estiver somente para leitura, prefixar
os comandos com `DOTNET_CLI_HOME=/tmp/flowonrails-audit-cli`. Após o primeiro
restore, `--no-restore` evita trabalho de restauração adicional.
Se o MSBuild terminar sem diagnóstico nesse ambiente restrito, compilar com
`--disable-build-servers -m:1` e executar a DLL de saída diretamente. Os comandos
completos da execução final estão registrados no relatório.

A saída após as correções está em `run-output.txt`; a evidência histórica dos
defeitos está preservada em `run-output.before-fixes.txt`. O consumidor do
compilador deve produzir exatamente os dois AUDIT001 intencionais, nenhum
AD0001 e nenhum erro. A revisão completa está em
`docs/reviews/2026-11-12-analysis-api-audit.md`.
