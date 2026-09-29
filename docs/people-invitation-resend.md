# Pessoas — reenvio de convite

A tela Pessoas oferece **Reenviar convite** para convites pendentes, tanto válidos quanto expirados. Não é necessário aguardar 48 horas. A ação não se aplica a contas já vinculadas nem a convites aceitos, revogados ou ausentes. Se a consulta complementar de convites não confirmar o estado, atualizar os dados antes de oferecer a ação.

## Contrato existente (sem nova rota ou migration)

- Consultar pessoas por `GET /api/v1/organization/time-control/people` e convites por `GET /api/v1/organization/invitations`; relacionar `person.invitationId` com `invitation.id`.
- Enviar **POST /api/v1/organization/invitations/{invitationId}/resend**, sem corpo. Requer autenticação administrativa. `SystemAdmin` informa `organizationId` na query; `OrganizationAdmin` fica limitado à própria organização. Manter o mesmo escopo nas consultas e no envio.
- Sucesso: **204**, sem código ou token na resposta. Gera novo código, invalida o anterior, renova a validade por 48 horas, zera as tentativas do convite e enfileira o e-mail transacional. Registra `invitation.resent` na auditoria.
- E-mail contém o link de ativação e as instruções atuais, inclusive para convites originalmente enviados sem link. Não criar outra pessoa nem modificar vínculos Monday/VR.
- Erros: `invitation_not_found` (404), `invitation_not_pending` (409), `email_domain_not_allowed` (400), além de autenticação/autorização e rate limiting. Mostrar mensagem e código de suporte quando disponível.

## Comportamento da interface

Exibir o botão na linha da pessoa quando há `invitationId`, o convite correspondente foi consultado e não há `person.userId`, `person.invitationAcceptedAt`, `invitation.acceptedAt` ou `invitation.revokedAt`.

Antes de enviar, confirmar destinatário, substituição do código e renovação da validade. Desabilitar confirmação enquanto houver envio e impedir clique duplo. Não reenviar automaticamente ao carregar a página nem repetir POST silenciosamente após erro de rede.

Após 204, mostrar **Novo convite com link de ativação colocado na fila de envio. Isso não confirma a entrega do e-mail.** Atualizar pessoas e convites. Em caso de conflito, preservar a mensagem de suporte e permitir atualizar o estado; o servidor decide se o convite ainda pode ser reenviado.

O endpoint já existe no OpenAPI; não há alteração de schema ou tipos. A lista complementar retorna até 200 convites: estado ausente não deve ser interpretado como pendente.
