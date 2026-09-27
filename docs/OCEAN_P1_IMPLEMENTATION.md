# Oceano P1 — primeira animação nativa

Relatório histórico. A prévia atual abre a [segunda prova P2, com superfície espectral e comparação](OCEAN_P2_IMPLEMENTATION.md); os resultados abaixo pertencem à P1.

Data: 2026-09-27. Estado: prova técnica implementada e testada. Esta é a primeira etapa do [plano](OCEAN_WALLPAPER_PLAN.md), não a aprovação visual final nem uma atualização do pacote instalado.

## Abrir e experimentar

Na raiz do projeto:

```powershell
./scripts/show-ocean-preview.ps1
```

Para gerar também capturas e executar os testes gráficos dirigidos:

```powershell
./scripts/show-ocean-preview.ps1 -Validate
```

A janela oferece Pôr do sol, Dia, Lua e Nublado; mar calmo, moderado e agitado; enquadramento com horizonte ou próximo da água; velocidade 0,5×/1×/1,5×; níveis Leve/Equilibrado/Alto; pausa e captura PNG. Espaço pausa/retoma; Esc fecha. As capturas são guardadas na pasta de execução indicada pelo script.

O script compila para uma pasta própria por execução em `artifacts/ocean-preview`, preservando outras builds. Usa `OceanPreview=true` para gerar um executável gráfico WinExe: a janela interativa abre normalmente, sem consola. A prévia usa a mesma ponte offscreen/GDI do HYPNIX. Não altera configurações, não captura áudio, não instala um pacote e não se prende ao Explorer. A inclusão na biblioteca, personalização persistida e ciclo completo do wallpaper são etapas futuras de produto.

## Implementação

- [OceanGpuRenderer](../Services/OceanGpuRenderer.cs): D3D11/HLSL dedicado, malha indexada, profundidade D32, cor RGBA16F, composição SDR e duas saídas: offscreen/GDI ou DXGI.
- [OceanWaveModel](../Services/OceanWaveModel.cs): 32 componentes analíticas direcionais, 16 usadas no deslocamento de geometria; dispersão de águas profundas, fases calculadas em double e reduzidas antes do envio à GPU. A intensidade de ondas curtas e longas varia separadamente entre estados de mar.
- [Ocean.hlsl](../Shaders/Ocean.hlsl): normais por derivadas com deslocamento horizontal, filtragem de frequência pelo píxel e variância não resolvida incorporada num lóbulo Beckmann. O sol/lua é um disco finito com oito amostras determinísticas, separado do céu refletido. Material sem espuma, bloom ou TAA.
- [OceanFrameClock](../Services/OceanFrameClock.cs): tempo lógico fora do renderer, pausa sem recuperação do intervalo suspenso e alteração de velocidade sem multiplicar novamente o tempo passado.
- [OceanProofWindow](../Tests/Hypnix.NativeSmoke/OceanProofWindow.cs): controles e janela de avaliação, renderização numa thread própria, recriação após resize e encerramento assíncrono para manter a thread da janela responsiva.

O campo é a referência Gerstner da fase P1; não é FFT, JONSWAP ou uma implementação espectral estatística já aprovada. Todos os níveis usam as mesmas ondas/fases. Nesta prova, a qualidade altera apenas o orçamento de resolução: 720p, 1080p ou 4K equivalentes em área, preservando proporção. Não são ainda níveis calibrados para todas as GPUs.

O shader trabalha em valores lineares pré-expostos escolhidos artisticamente. A lua não usa uma calibração fotométrica física. O filtro de inclinações não resolvidas é uma aproximação isotrópica, embora a amostragem considere a projeção do píxel. A integração com oito amostras do disco ainda deve ser comparada com uma referência de maior amostragem em P2/P3.

## Refinamentos já efetuados nesta etapa

1. Corrigida uma borda próxima da malha que aparecia ao inclinar a câmara: a cobertura agora começa antes da região visível e mantém o alcance distante.
2. Reduzida a energia das ondulações mais pequenas, que inicialmente produziam um padrão demasiado listrado.
3. Ajustados o gradiente do céu e a rugosidade residual; mantida uma única conversão linear→sRGB.
4. Validada a continuidade do tempo em pausa/velocidade e após reconstrução das texturas ou do renderer.

## Validação realizada

Build Release de `Hypnix.NativeSmoke`: **zero erros e zero avisos**. Suite `Hypnix.Tests`: **389 testes passaram**, incluindo nove casos novos de relógio, derivadas completas, limite de dobragem, fases longas e orçamento/proporção.

Testes D3D11 reais na **NVIDIA GeForce RTX 5070 Ti**:

| Verificação | Resultado |
| --- | --- |
| HLSL e os cinco programas gráficos | Compilação e desenho bem-sucedidos |
| 12 cenas: 3 estados de mar × 4 iluminações | Capturas geradas; conteúdo não vazio e contraste verificado |
| Movimento e pausa | Frames mudam com o tempo; mesmo tempo/settings reproduz exatamente os mesmos píxeis na sessão |
| Apresentação GDI a 213×121 | RGB idêntico ao target GPU em todos os píxeis, incluindo padding de linha |
| Saída DXGI em janela de teste oculta | Imagem igual à saída offscreen e Present bem-sucedido |
| Retrato, ultrawide e 4K nativo | Capturas em 540×960, 1280×360 e 3840×2160 |
| Mudança de qualidade e recriação | Limites internos aplicados; retorno a Alto e novo renderer preservam a imagem no mesmo tempo lógico |
| Janela interativa | Pausa, troca para lua, resize, retoma, captura e encerramento exercitados |

Evidência local: `artifacts/ocean-p1/captures/ocean-checks.json`, capturas PNG na mesma pasta, 180 frames a 30 FPS em `captures/frames`, relatórios TRX em `artifacts/ocean-p1/test-results` e captura da janela em `artifacts/ocean-p1/window-check`.

Foi feita uma sondagem curta de GPU a 960×540 com queries assíncronas e rejeição de amostras disjoint. Esta sondagem exclui apresentação/readback e **não constitui o benchmark de aceitação de 120 s**, uma medição de watts ou validação em iGPU. Os valores brutos de resumo e o escopo estão no JSON. `EstimatedTextureBytes` conta bytes lógicos das texturas, incluindo staging; não representa toda a VRAM/residência ou buffers.

Reprodução técnica, com outputs separados da aplicação instalada:

```powershell
dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release --no-restore -p:OutDir=D:/desktopapp/AnimatedWallPaper/artifacts/ocean-p1/bin/ -p:UseSharedCompilation=false
dotnet artifacts/ocean-p1/bin/Hypnix.NativeSmoke.dll artifacts/ocean-p1/captures --ocean-only --ocean-frames
dotnet artifacts/ocean-p1/bin/Hypnix.NativeSmoke.dll artifacts/ocean-p1/window-check --ocean-window-check
dotnet test Tests/Hypnix.Tests/Hypnix.Tests.csproj -c Release --no-restore -p:OutDir=D:/desktopapp/AnimatedWallPaper/artifacts/ocean-p1/unit/ -p:UseSharedCompilation=false
```

Não foi efetuado o teste de anexação ao desktop físico, mudança do wallpaper do utilizador, recuperação do Explorer ou instalação de pacote. O teste DXGI usa uma janela oculta, sem afirmar que valida esses cenários.

## Próximo refinamento

Usar esta animação para avaliar enquadramento, escala, velocidade e contraste. A regularidade das ondas e a resposta dos reflexos em movimento continuam como pontos de refinamento. P2 compara a base analítica com espectro/IFFT em bandas e avalia a filtragem contra referências espacial/temporal. P3 aprova a aparência; P4 integra capacidades, catálogo, preferências e ciclo de vida no produto. DLSS continua fora dos requisitos.
