# Oceano vivo no HYPNIX — implementação e correções de integração

Data: 28/09/2026. Implementa o [plano de experiência](OCEAN_USER_EXPERIENCE_PLAN.md). Aceitação visual fica com o proprietário; os testes automatizados não certificam realismo nem o comportamento do sino do Windows.

## Experiência entregue

Um item Ocean alive/Oceano vivo na biblioteca abre uma página própria dentro do HYPNIX. Também pode ser aplicado diretamente pela biblioteca. Selecionar um cartão da biblioteca continua a ser uma seleção para prévia; Aplicar confirma a alteração do desktop.

- Momentos: amanhecer, dia, pôr do sol e luar. As miniaturas são capturas do renderer, não imagens geradas externamente. Escolher novamente o mesmo momento reposiciona o relógio celestial.
- Manter o momento ou ciclo do dia, com duração de 10/20/60/120 minutos. Manter o momento fixa apenas o céu; água e nuvens continuam em movimento. Pausar a prévia fixa todos os seus relógios.
- Mar calmo/moderado/agitado, quantidade de nuvens de 0 a 100%, forma das nuvens e ajustes adicionais de vento, névoa, horizonte, velocidade da água, brilho, destaque dos astros e qualidade.
- Textos em inglês e português, de acordo com a cultura da aplicação. Não altera o idioma do restante shell.
- Rascunho por monitor, Descartar, Aplicar ao monitor e Aplicar a todos. Falhas mantêm o wallpaper anterior; aplicar a todos registra os sucessos e erros de cada saída. Nenhuma reação a áudio é criada para o oceano.

## Relógios, persistência e isolamento

`OceanPreferences` é separado de `VisualizerSettings`. As preferências normalizadas são guardadas por identidade estável do monitor em `DisplayWallpapers.*.Oceans`. Configurações antigas continuam válidas e monitores desconectados mantêm suas preferências.

`OceanRuntime` mantém tempo da água, tempo meteorológico, instante celestial e trajetória de vento. Prévia, desktop e cada monitor usam estados independentes. A troca de renderer usa um proprietário explícito do relógio, evitando que a limpeza de uma sessão antiga pause a nova. Redimensionar a prévia preserva a fase lógica. Durante Aplicar, a configuração submetida é uma cópia; ajustes feitos durante a preparação permanecem no rascunho.

Ao salvar preferências e ao encerrar normalmente, o instante celestial ativo é registrado. A fase da água e o histórico de nuvens não são persistidos entre processos. Não há sincronização entre monitores por frame nem integração com horário/clima local real. Os momentos curados usam 27/09/2026 e a localização astronômica padrão; Luar começa às 19:08 UTC para enquadrar a Lua.

## Regressões comunicadas pelo proprietário

### 1. Primeiro Apply parece depender de selecionar outro wallpaper

O log da sessão `20260928-110033-37112` registra a preparação do primeiro desktop Ocean às 12:03:04 e a revelação às 12:03:16 (hora de Lisboa), com preparação simultânea de prévias e sem exceção nesse intervalo. A hipótese sustentada pelo log é atraso de inicialização, não uma dependência deliberada da seleção de outro cartão. Não foi reproduzida visualmente nesta revisão.

A aplicação agora suspende as prévias enquanto prepara o desktop, mostra “Preparing ocean…” no botão da biblioteca e registra pedido e conclusão com identidade do monitor/revisão. Erros também aparecem na biblioteca, mesmo se o editor dedicado estiver fechado. O limite de preparação permanece em 45 segundos; a primeira execução pode demorar devido à inicialização/compilação dos shaders. O wallpaper anterior só é substituído depois da preparação e revelação bem-sucedidas. Um teste com conclusão assíncrona confirma que a primeira chamada se revela sem um segundo comando de seleção.

### 2. Prévia sobre Buy license e fragmentos durante scroll

A implementação inicial usava `HwndHost` dentro do conteúdo rolável. Um HWND não participa da mesma composição e recorte WPF dos controles: podia sobrepor a faixa de licença e deixar fragmentos em posições anteriores.

A prévia Ocean agora usa `OceanPreviewControl`, uma imagem WPF com `WriteableBitmap`. O Direct3D renderiza offscreen com HWND zero, sem swap chain de janela; `GpuPreviewSurface.CopyPixels` reutiliza staging e buffer BGRA. O dispatcher copia apenas frames completas, com no máximo uma entrega pendente por sessão. O WPF controla scroll, recorte, sobreposição e escala. A prévia fica limitada a 1280 pixels de largura; o desktop mantém a resolução e qualidade escolhidas.

`WallpaperPreviewControl` escolhe esse backend em todos os locais onde se mostra Ocean: biblioteca, múltiplos monitores e editor. Os demais wallpapers mantêm o backend nativo anterior em `NativeWallpaperPreviewControl`. GPU e recursos são liberados no worker proprietário; a interface não aguarda a finalização de shaders ao trocar de prévia.

### 3. Sino do Windows voltou a alternar

A aceitação anterior na 1.2.2 permanece válida para aquela build. O novo relato reabre a investigação para a integração Ocean. O log da build relatada já dizia `Preview=True; Presentation=OffscreenGdi; WindowSwapChain=False`; portanto, não há evidência de que uma swap chain de prévia tenha sido reintroduzida.

A alteração acima elimina também o HWND/GDI da prévia Ocean. O log novo identifica `Ocean WPF preview ready; PreviewHwnd=False; WindowSwapChain=False`. Nenhuma configuração de notificações/Não incomodar do Windows foi alterada. Essa é uma mitigação a verificar pelo proprietário; **o sino não é considerado resolvido apenas porque os testes passaram**.

## Verificação automatizada

- Build Release da aplicação e do smoke: zero avisos/erros de compilação.
- 447 testes unitários passaram, incluindo composição/recorte em várias posições de scroll, troca de backend sem filhos antigos e primeira aplicação com preparação assíncrona. O teste de recorte verifica pixels do cabeçalho/rodapé e do conteúdo, sem abrir janela.
- `--ocean-preview-offscreen`: igualdade byte a byte de dois frames animados de 213×121 entre o novo buffer reutilizável e leitura GPU independente, incluindo stride não trivial. Passou.
- O mesmo comando valida o editor sem mostrar janelas: rascunhos por monitor, aplicação, falha preservando a sessão anterior e persistência. Usa controladores e configurações isolados, sem alterar o desktop do proprietário.
- Resultado guardado em [ocean-product-offscreen-2026-09-28.json](validation/ocean-product-offscreen-2026-09-28.json).
- Os testes nativos anteriores da integração verificaram desktop swap chain, pausa e retomada. Não foram repetidos depois do pedido de testes visuais manuais, pois exibem janelas.
- O runner unitário avisou que a consulta de vulnerabilidades NuGet estava indisponível na rede; isso não é falha de compilação/testes nem auditoria de dependências concluída.

Comandos repetíveis:

```powershell
dotnet build AnimatedWallPaper.csproj --no-restore -c Release -o artifacts/ocean-product-build -v minimal
dotnet test Tests/Hypnix.Tests/Hypnix.Tests.csproj --no-restore -c Release -v minimal
dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj --no-restore -c Release -v minimal
Tests/Hypnix.NativeSmoke/bin/Release/net8.0-windows10.0.19041.0/Hypnix.NativeSmoke.exe artifacts/ocean-product-regression --ocean-preview-offscreen
```

## Aceitação manual pendente

Usar a build `artifacts/ocean-product-build/HYPNIX.exe` depois de encerrar normalmente a instância antiga. A revisão não substitui/reinicia a janela do proprietário automaticamente.

1. Primeira execução: selecionar Ocean e Aplicar uma vez; aguardar a preparação sem escolher outro cartão. Conferir monitor de destino e transição do botão.
2. Abrir o editor, rolar repetidamente e redimensionar. Conferir faixa de licença, cabeçalho e rodapé, incluindo escala DPI do sistema.
3. Observar o sino com prévia aberta, oculta e app minimizado, com/sem desktop Ocean ativo. Registrar qual combinação reproduz se ainda ocorrer.
4. Conferir qualidade/movimento, pausa/retomada, presets, Aplicar e Descartar em cada monitor. Comparar consumo com a build anterior: não foi feita nova medição de consumo nesta revisão.

## Correção seguinte — prévia inicial e pausa em modo janela

28/09/2026, após teste manual da build `93a2f99`.

**Prévia:** o primeiro backend WPF herdava diretamente de `Image`. Sem `Source`, esse controle organizava seu tamanho como zero, mesmo dentro de um painel com dimensões válidas. O guard de inicialização aguardava dimensões positivas para gerar a primeira imagem, formando um ciclo sem progresso. Agora um `Grid` define o viewport e contém a imagem. Um teste verifica dimensões antes de existir qualquer frame. Outro defeito corrigido: uma edição recebida enquanto a frame anterior aguardava o dispatcher podia ficar sem novo sinal quando a animação estava pausada. Ao concluir a entrega ao WPF, uma repintura pendente agora acorda o worker.

**Pausa:** conforme esclarecimento do proprietário, as opções são Never, Fullscreen apps only e Fullscreen or windowed apps. A terceira inclui janelas normais, encaixadas e maximizadas; não exige que cubram todo o monitor. Aplicativos minimizados, ocultos, em outro desktop virtual, overlays auxiliares e o próprio HYPNIX continuam excluídos. Janelas visíveis que atravessam monitores ocupam ambos. Usa-se a moldura visível do DWM para evitar que bordas invisíveis pausem o monitor vizinho. O escopo por monitor/todos os monitores continua configurável. O valor salvo 2 passa a ter a nova semântica, sem migração de schema.

**Validação:** 454 testes unitários passaram. O teste `--ocean-preview-offscreen` foi ampliado para exercitar o ciclo real WPF/Direct3D: inicialização sem imagem, animação, pausa, mudança de pixels ao trocar Sol por Lua pausado, edição enquanto há uma entrega pendente e retomada. Passou com a escala DPI real do Windows. Para disparar `Loaded`, usa um proprietário WPF invisível, nunca mostrado ou anexado ao desktop; a prévia continua sem HWND de apresentação GPU. Nenhuma janela do usuário foi reiniciada. A aceitação visual das novas opções de pausa permanece com o proprietário.

Resultado: [ocean-preview-lifecycle-2026-09-28.json](validation/ocean-preview-lifecycle-2026-09-28.json). Build atualizada no mesmo caminho `artifacts/ocean-product-build/HYPNIX.exe`.
