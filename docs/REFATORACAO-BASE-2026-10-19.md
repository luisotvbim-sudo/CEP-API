# Base da refatoração CEP-API — 19/10/2026

Escopo: CEP Horas; contratos de identidade dos plugins Revit/ZWCAD permanecem compatíveis. Esta etapa atualiza dependências e reconcilia a documentação da telemetria. Não altera endpoint, regra de horas, autorização, integração externa ou schema.

`origin/main` `fff8958` era a base. O restore completo bloqueou o build por avisos NU1902/NU1903 em `System.Security.Cryptography.Pkcs` 10.0.0 e, antes da atualização, por avisos da versão 10.0.10 de `Pkcs` e `Xml`. `Directory.Packages.props` agora mantém os pacotes Microsoft .NET 10 no patch 10.0.13 e fixa `Pkcs` transitivo no mesmo patch. Não foi suprimido aviso de vulnerabilidade. Referências: [NuGet Pkcs 10.0.13](https://www.nuget.org/packages/System.Security.Cryptography.Pkcs/10.0.13), [NuGet Xml 10.0.13](https://www.nuget.org/packages/System.Security.Cryptography.Xml/10.0.13).

Validação local: `dotnet build CEP-API.sln -c Release -m:1` passou com zero avisos/erros; `dotnet test tests/CepApi.UnitTests/CepApi.UnitTests.csproj -c Release --no-build --no-restore` passou 136/136. Docker Desktop Linux não respondeu nesta estação, então integração com PostgreSQL, migration idempotente e stack HTTPS ainda requerem CI do SHA final. Nenhuma implantação foi feita nesta etapa.

A [Issue central #54](https://github.com/luisotvbim-sudo/CEP-ORQUESTRADOR/issues/54) acompanha a revisão de domínio/isolamento, a validação independente e a operação. O fato de a suite unitária passar não fecha o aceite do programa.
