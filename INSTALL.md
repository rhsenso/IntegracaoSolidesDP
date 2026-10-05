# Instalação — IntegracaoSolidesDP (serviço Windows)

## Pré-requisitos

- Windows Server 2016+ com acesso de rede ao SQL Server do RHSenso e a `https://employer.tangerino.com.br`.
- O pacote é self-contained: não precisa instalar o .NET.
- Token de integração do Sólides DP: Empregador → Integrações. Peça ao suporte da Sólides se o menu não aparecer.

## 1. Login no SQL Server (privilégio mínimo)

O worker **lê** as tabelas do RHSenso (`dbo`) e só **escreve** no schema próprio `solidesdp`.

```sql
CREATE LOGIN integracao_solidesdp WITH PASSWORD = '<senha forte>';
GO
USE bd_rhu_adn;
CREATE USER integracao_solidesdp FOR LOGIN integracao_solidesdp;
ALTER ROLE db_datareader ADD MEMBER integracao_solidesdp;
EXEC('CREATE SCHEMA solidesdp AUTHORIZATION integracao_solidesdp');
GRANT CREATE TABLE TO integracao_solidesdp;
```

As tabelas de estado (`solidesdp.*`) são criadas pelo próprio worker na primeira execução.

## 2. Instalar o serviço

1. Extraia o zip do release (ex.: `IntegracaoSolidesDP-<versão>-win-x64.zip`).
2. Edite o `appsettings.json` (passo 3).
3. Num PowerShell **como Administrador**, de dentro da pasta extraída:

   ```powershell
   .\install-service.ps1               # instala em C:\Services\IntegracaoSolidesDP
   ```

O script:
- copia os arquivos, preservando o `appsettings.json` que já estiver lá;
- registra o serviço com reinício automático em caso de falha;
- roda `--check-config`, e só inicia o serviço se a validação passar.

## 3. Configuração (`appsettings.json`)

```jsonc
{
  "ConnectionStrings": {
    "Rhu": "Server=<servidor>;Database=bd_rhu_adn;User Id=integracao_solidesdp;Password=<senha>;Encrypt=True;TrustServerCertificate=True"
  },
  "SolidesDP": { "Token": "<token do Sólides DP>" },
  "Execution": { "Interval": "00:30:00", "TimeZone": "America/Bahia" },   // ou "TimesOfDay": ["07:00", "13:00"]
  "Sync": {
    "DryRun": true,                 // comece em true
    "GoLiveDate": "2026-11-01",     // primeiro dia de uso do ponto no DP
    "WorkScheduleExternalId": "",   // vazio = escala padrão da conta (veja --discover)
    "PunchRuleExternalId": ""       // vazio = regra padrão da conta
  }
}
```

Segredos também podem ir em variáveis de ambiente do serviço, em vez do arquivo: `ConnectionStrings__Rhu`, `SolidesDP__Token`.

## 4. Primeiro contato com a API real (não existe homologação)

Qualquer chamada vai para a conta de **produção** da ADN no Sólides DP. Siga a ordem:

1. **Validar o acesso.** `IntegracaoSolidesDP.exe --check-config` precisa mostrar o banco e o token `[OK]`.
2. **Descobrir os ids.** `IntegracaoSolidesDP.exe --discover` lista empresas, escalas, regras de ponto e motivos de ajuste. Preencha `WorkScheduleExternalId` e `PunchRuleExternalId` e confira:
   - se os CNPJs das empresas batem com as filiais;
   - se existe o motivo **FÉRIAS**.
3. **Simular.** `IntegracaoSolidesDP.exe --dry-run` gera `reports\run-*.csv`. O RH da ADN revisa esse relatório.
4. **Piloto com 2 ou 3 pessoas.**
   - Configure `"Sync": { "DryRun": false, "ExternalIdAllowList": ["14-00901482", "..."] }` e rode `IntegracaoSolidesDP.exe --run-once`.
   - Confira no app.tangerino.com.br.
   - Valide o **checklist de calibração**:
     - [ ] O colaborador foi criado com cargo, local, empresa, escala e regra corretos.
     - [ ] Uma segunda execução **não** gera escritas: tudo `unchanged`.
     - [ ] Ao alterar um campo no RHSenso, a atualização não apaga campos que o RH preencheu no DP, nem troca a escala.
     - [ ] As férias aparecem no período certo: o último dia está incluído e não sobra um dia a mais. Se não estiver certo, ajuste `Sync:FeriasEndDateMode`.
     - [ ] O cancelamento de férias (reprogramação) some do DP.
     - [ ] O desligamento aparece com a data e o motivo corretos.
     - [ ] CPF já cadastrado manualmente no DP: anote o comportamento (erro ou vínculo).
     - [ ] Readmissão de alguém já desligado no DP: anote o comportamento.
   - O que divergir deve ser registrado e o fake (`src/SolidesDP.Fake`) ajustado para refletir o comportamento real.
5. **Rollout por empresa.** Use `Sync:EmpresasIncluidas` e, depois, retire os filtros.
6. **Ligar o serviço.** Deixe `DryRun=false` no `appsettings.json` e reinicie o serviço.

## Operação

- **Logs:** `C:\Services\IntegracaoSolidesDP\logs`.
- **Relatórios:** `reports\run-*.csv`, um por execução, com os itens `failed`/`blocked`/`skipped` e o motivo.
- **Histórico no banco:** `SELECT * FROM solidesdp.runs ORDER BY started_at DESC`.
- **Reconciliação semanal:** `IntegracaoSolidesDP.exe --reconcile`. Lista colaboradores enviados que não existem mais no DP. Com `--repair`, eles são marcados para reenvio.
- **Atualizar a versão:** extraia o zip novo e rode `.\install-service.ps1` de novo. A configuração é preservada.

## Problemas conhecidos nos dados do RHSenso (ADN)

- **12 colaboradores ativos têm o CPF gravado com máscara** (`###.###.###`) em `func1.nocpf`, que é `varchar(11)`. Isso cortou os dígitos verificadores. Eles são enviados sem CPF e aparecem no relatório como `cpf_truncado_no_rhsenso`. **Corrigir no RHSenso.**
- **Dois pares de matrículas ativas na empresa 7 com o mesmo CPF.** Ficam fora (`skipped_duplicate_cpf`) até alguém decidir.
- **Alguns PIS falham no dígito verificador.** São enviados sem PIS e aparecem com o aviso `invalid_pis_check_digit`.
