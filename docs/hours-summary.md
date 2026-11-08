# Resumo de horas da coordenação

GET `/api/v1/organization/time-control/hours-summary?from=AAAA-MM-DD&to=AAAA-MM-DD`, somente OrganizationAdmin ou SystemAdmin com contexto organizacional. Intervalo inclusivo de até 90 dias, sem datas futuras. Leituras de pessoas, registros importados, tolerância e vínculos usam o mesmo snapshot PostgreSQL. Não consulta as fontes, não executa jobs e não cria análise ou notificação.

O resultado inclui pessoa-dias, totais conhecidos por fonte, quantidades disponíveis por fonte, saldo e divergência absoluta para dias comparáveis e contagem acima da tolerância. Dia sem registro aparece com valores indisponíveis e não prova ausência de jornada. A API reutiliza a regra diária do histórico e não prolonga cronômetro importado. Totais da organização incluem todas as associações autorizadas sem limite de 200 e não derivam de totais de times.

Por time ativo, entram as pessoas com vínculo vigente hoje e, para cada pessoa, apenas os dias do intervalo que coincidem com seus vínculos nesse time. Sobreposições de papéis no mesmo time não duplicam dias. Times podem compartilhar pessoas. A resposta discrimina os dias incluídos no subtotal de cada membro. O histórico individual preserva o intervalo inteiro.

Contrato aditivo, sem migration ou alteração de escopo em endpoints existentes. A exportação web usa essa resposta, sem recálculo ou nova leitura. Coordenação: [Issue #62](https://github.com/luisotvbim-sudo/CEP-ORQUESTRADOR/issues/62).

GET `/api/v1/organization/time-control/teams/{teamId}/hours-summary` aplica o mesmo período e cálculo ao time ativo solicitado. Permite consulta ao líder com vínculo vigente no time e à coordenação da mesma organização. Líder sem vínculo de gestão vigente recebe 404. A resposta contém o time e apenas as pessoas e os dias considerados no total desse time.
