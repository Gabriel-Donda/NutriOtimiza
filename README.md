# NutriOtimiza

Aplicativo de otimização, limpeza e diagnóstico para Windows da **Nutriport**, pensado para a equipe técnica e as máquinas da empresa.

> Os projetos e namespaces do código mantêm o nome interno `OtimizaPC.*`; o nome exibido, o executável (`NutriOtimiza.exe`) e o instalador usam **NutriOtimiza**.

**Compatibilidade:** Windows 8.1, 10, 11 e Windows Server 2012 R2 ou superior, em x64 (e x86 para Windows 32 bits).
Requer o .NET Framework 4.8, que já vem no Windows 10 (1903+) e 11 e pode ser instalado no 8.1 / 2012 R2.
O instalador verifica e, se faltar, oferece o link oficial de download.

## Módulos

| Módulo | O que faz |
|---|---|
| Painel | Métricas ao vivo e nota de saúde do sistema |
| Hardware | Relatório completo, exportável em HTML |
| Sensores | Temperaturas, clocks e ventoinhas com mínimo e máximo |
| Limpeza | Temporários, Windows Update, navegadores, apps, DISM e lixeira, com análise prévia |
| Espaço em disco | Mapa de pastas e maiores arquivos |
| Otimizações | 37 ajustes curados, com desfazer e ponto de restauração |
| Inicialização | Ativar/desativar programas como o Gerenciador de Tarefas |
| Apps pré-instalados | Remoção de bloatware com reinstalação |
| Reparo | SFC, DISM, CHKDSK, rede, Windows Update, erros do sistema, telas azuis e drivers |
| Configurações | Tema, atualização automática, limpeza semanal agendada, driver de sensores e histórico |

## Estrutura

```
src/
├── OtimizaPC.Core      → modelos, log, histórico, configurações, atualização, exportação de relatório
├── OtimizaPC.Hardware  → WMI, registro, Win32, sensores (LibreHardwareMonitor), verificações de saúde
├── OtimizaPC.Cleaning  → motor de limpeza, limpeza agendada e analisador de disco
├── OtimizaPC.Tweaks    → otimizações reversíveis, ponto de restauração, inicialização, apps
├── OtimizaPC.Repair    → ferramentas de reparo com saída ao vivo, eventos, telas azuis e drivers
└── OtimizaPC.App       → interface WPF (WPF-UI, MVVM com CommunityToolkit.Mvvm)
tests/OtimizaPC.Tests   → testes automatizados (xUnit)
installer/              → script e imagens do instalador (Inno Setup 6)
tools/                  → build dos pacotes e geração dos recursos da marca
branding/               → logo original da Nutriport
```

## Regras de segurança da limpeza

- Só remove arquivos dentro de pastas de cache/temporários conhecidas (`CleanupCatalog`).
- `SafePath` recusa raiz de unidade, pastas do sistema e pastas pessoais (Documentos, Desktop, Downloads…), mesmo se um alvo for configurado errado.
- Nunca segue junctions/links simbólicos.
- Arquivos em uso são mantidos, nunca forçados. Temporários com menos de 24 h são preservados.
- Caches de apps (navegadores, Discord…) só são limpos com o app fechado.
- Sempre há análise prévia e confirmação antes de apagar. A limpeza agendada usa só itens seguros e recomendados.

## Regras das otimizações

- Antes de alterar qualquer valor, o original é salvo em `%LocalAppData%\NutriOtimiza\tweaks-undo.json` (gravação atômica).
- Reverter restaura o valor original; se o app criou a chave, remove só o que ficou vazio (preserva valores de outros programas).
- Sem registro de desfazer (ajuste feito fora do app), reverter volta ao padrão do Windows.
- Cada otimização declara risco, versões do Windows suportadas e se exige reiniciar.
- Todo o histórico de alterações fica em `%LocalAppData%\NutriOtimiza\history.log` e aparece em Configurações.

## Identidade visual (Nutriport)

| Cor | Hex | Uso |
|---|---|---|
| Grafite | `#202020` | Fundo do tema escuro (padrão) e do ícone |
| Dourado | `#D2C068` | Títulos no tema escuro, "Nutri" da marca |
| Laranja | `#F58634` | Cor de destaque: botões, seleção, gráficos |

O ícone do app e as imagens do instalador são gerados a partir de `branding/nutriport-logo.png`:

```bash
pwsh tools/generate-brand-assets.ps1
```

## Desenvolvimento

Requer o .NET SDK 8 ou superior.

```bash
dotnet build
```

```bash
dotnet test
```

O executável de desenvolvimento fica em `src/OtimizaPC.App/bin/Debug/net48/win-x64/NutriOtimiza.exe`.

- **Debug** roda sem pedir administrador. Temperaturas e itens do sistema ficam indisponíveis.
- **Release** pede elevação (UAC), necessária para limpeza do sistema, otimizações e reparos.

## Gerar os pacotes de distribuição

Requer também o [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

```bash
pwsh tools/build.ps1
```

Roda os testes e gera em `artifacts/`, para x64 e x86:

- `NutriOtimiza-Setup-{versão}-{arq}.exe`: instalador (Program Files, menu Iniciar, desinstalação).
- `NutriOtimiza-Portable-{versão}-{arq}.zip`: versão portátil para pendrive de técnico.
- Um `.sha256` para cada arquivo.

A versão vem de `<Version>` em `Directory.Build.props`.

## Publicar uma versão (atualização automática)

As máquinas com o app instalado verificam o GitHub Releases ao abrir e a cada 6 horas. Quando há versão nova, o app baixa o instalador em segundo plano, confere o SHA-256 e instala ao fechar.

Para publicar:

1. Aumente `<Version>` em `Directory.Build.props`.
2. Faça commit e push.
3. Rode `pwsh tools/release.ps1` (opcional: `-Notes "texto"`; use `-DryRun` para simular sem alterar nada).

O script roda o build, valida os `.sha256`, cria a tag `v{versão}` e a release com os instaladores, portáteis e checksums. O repositório precisa ser **público** para a atualização funcionar sem senha.

## Observações

- **Assinatura digital:** os executáveis não são assinados. Até haver um certificado de assinatura de código, o Windows SmartScreen mostra o aviso "O Windows protegeu o computador" na primeira execução, e alguns antivírus podem desconfiar, já que o app altera registro e serviços.
- **Temperatura da CPU:** exige executar como administrador **e** ter o driver [PawnIO](https://pawnio.eu). Use **Instalar PawnIO** no Painel ou **Configurações → Sensores → Instalar driver**. O app instala pelo winget, acompanha a conclusão e reabre os sensores sem fechar o aplicativo. A solicitação de administrador do Windows pode aparecer. Sem winget, use **Site oficial do PawnIO**, instale o driver e clique em **Verificar sensores**. Driver instalado sem leitura gera um aviso específico: alguns equipamentos não expõem a temperatura e podem exigir reiniciar o Windows.
- **Versão x86:** só é instalada em Windows 32 bits. Em Windows 64 bits, os sensores exigem o processo 64 bits.
- **Logs:** ficam em `%LocalAppData%\NutriOtimiza\logs`.
