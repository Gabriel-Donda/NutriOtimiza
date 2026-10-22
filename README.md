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
| Limpeza | Fase 2 |
| Otimizações (com desfazer) | Fase 3 |
| Reparo (SFC, DISM, rede, BSOD) | Fase 4 |
| Instalador, atualização automática, assinatura | Fase 5 |

## Estrutura

```
src/
├── OtimizaPC.Core      → modelos, log, formatação, exportação de relatório
├── OtimizaPC.Hardware  → WMI, registro, Win32, sensores (LibreHardwareMonitor), verificações de saúde
└── OtimizaPC.App       → interface WPF (WPF-UI, MVVM com CommunityToolkit.Mvvm)
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
