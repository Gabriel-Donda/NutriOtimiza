# OtimizaPC

Aplicativo de otimização, limpeza e diagnóstico para Windows, pensado para técnicos e usuários avançados.

**Compatibilidade:** Windows 8.1, 10, 11 e Windows Server 2012 R2 ou superior (x64).
Requer o .NET Framework 4.8, que já vem no Windows 10 (1903+) e 11 e pode ser instalado no 8.1 / 2012 R2.

## Módulos

| Módulo | Status |
|---|---|
| Painel (métricas ao vivo + nota de saúde) | ✅ Fase 1 |
| Hardware (relatório completo + exportação HTML) | ✅ Fase 1 |
| Sensores (temperaturas, clocks, ventoinhas, mín/máx) | ✅ Fase 1 |
| Limpeza (temporários, Windows Update, navegadores, apps, DISM, lixeira) | ✅ Fase 2 |
| Espaço em disco (mapa de pastas + maiores arquivos) | ✅ Fase 2 |
| Otimizações (37 ajustes curados, com desfazer e ponto de restauração) | ✅ Fase 3 |
| Inicialização (ativar/desativar como o Gerenciador de Tarefas) | ✅ Fase 3 |
| Apps pré-instalados (remoção de bloatware com reinstalação) | ✅ Fase 3b |
| Reparo (SFC, DISM, rede, BSOD) | Fase 4 |
| Instalador, atualização automática, assinatura | Fase 5 |

## Estrutura

```
src/
├── OtimizaPC.Core      → modelos, log, formatação, exportação de relatório
├── OtimizaPC.Hardware  → WMI, registro, Win32, sensores (LibreHardwareMonitor), verificações de saúde
├── OtimizaPC.Cleaning  → motor de limpeza (catálogo, varredura, remoção segura) e analisador de disco
├── OtimizaPC.Tweaks    → otimizações reversíveis (registro, serviços, energia), ponto de restauração, inicialização
└── OtimizaPC.App       → interface WPF (WPF-UI, MVVM com CommunityToolkit.Mvvm)
tests/
└── OtimizaPC.Tests     → testes do motor de limpeza (xUnit)
```

## Regras de segurança da limpeza

- Só remove arquivos dentro de pastas de cache/temporários conhecidas (`CleanupCatalog`).
- `SafePath` recusa raiz de unidade, pastas do sistema e pastas pessoais (Documentos, Desktop, Downloads…), mesmo se um alvo for configurado errado.
- Nunca segue junctions/links simbólicos.
- Arquivos em uso são mantidos, nunca forçados. Temporários com menos de 24 h são preservados.
- Caches de apps (navegadores, Discord…) só são limpos com o app fechado.
- Sempre há análise prévia e confirmação antes de apagar.

## Regras das otimizações

- Antes de alterar qualquer valor, o original é salvo em `%LocalAppData%\OtimizaPC\tweaks-undo.json` (gravação atômica).
- Reverter restaura o valor original; se o app criou a chave, remove só o que ficou vazio (preserva valores de outros programas).
- Sem registro de desfazer (ajuste feito fora do app), reverter volta ao padrão do Windows.
- Cada otimização declara risco, versões do Windows suportadas e se exige reiniciar.
- Todo o histórico de alterações fica em `%LocalAppData%\OtimizaPC\history.log`.

```bash
dotnet test
```

## Compilar e rodar

Requer o .NET SDK 8 ou superior.

```bash
dotnet build
```

O executável fica em `src/OtimizaPC.App/bin/Debug/net48/win-x64/OtimizaPC.exe`.

- **Debug** roda sem pedir administrador. Temperaturas e alguns sensores ficam indisponíveis.
- **Release** pede elevação (UAC), necessária para os módulos de limpeza e otimização.

```bash
dotnet publish src/OtimizaPC.App -c Release -r win-x64 -o publish/x64
```

Para Windows 8.1 32 bits, use `-r win-x86`.

## Observações

- A temperatura da CPU exige executar como administrador **e** ter o driver [PawnIO](https://pawnio.eu) instalado (exigência da LibreHardwareMonitor 0.9.5+).
- Logs ficam em `%LocalAppData%\OtimizaPC\logs`.
