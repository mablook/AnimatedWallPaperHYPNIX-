# Oceano P3 — primeira revisão de luz e céu

Data: 2026-09-27. Estado: implementada e testada. **Feedback recebido: cores da água melhores e consumo observado satisfatório; sol/lua e nuvens precisam de refinamento.** A aprovação da superfície [P2](OCEAN_P2_IMPLEMENTATION.md) permanece válida. O [novo plano de realismo do céu](OCEAN_SKY_REALISM_PLAN.md) responde às capturas e preserva essa água. Os resultados abaixo pertencem à primeira iteração P3.

## Abrir e comparar

Executar `./scripts/show-ocean-preview.ps1`. A janela **HYPNIX · Oceano — estudo 03 · luz e céu** oferece **Luz e céu novos** e **Iluminação anterior**. Pausar antes da troca compara a mesma água no mesmo instante. O seletor de ondas permanece independente.

Rever Pôr do sol, Dia, Lua e Nublado, com horizonte e perto da água. As cores, o céu e a iluminação mudaram; a geração e a filtragem da superfície aprovada foram preservadas.

## O que mudou

- **Ambiente compartilhado:** o céu visível e o ambiente refletido consultam o mesmo mapa HDR. O disco solar/lunar fica fora desse mapa e é integrado separadamente no material, evitando duplicar a fonte direta.
- **Atmosfera:** integração RGB de dispersão simples Rayleigh/Mie, com absorção aproximada por ozono. Há 32 intervalos de integração na direção da vista e 12 para transmissão até à fonte. Os intervalos são quadráticos para concentrar amostras perto do observador. O modelo usa quilómetros e não altera as unidades da água.
- **Cache:** mapa RGBA16F de 2048 × 512 com mipmaps, regenerado ao trocar o preset de luz. A parametrização vertical concentra resolução junto ao horizonte. A animação das ondas não recalcula a atmosfera a cada frame.
- **Nuvens:** camada procedural estática, com densidade suave e transmissão armazenada no alfa. A transmissão na direção do astro afeta tanto o disco visível como a luz direta refletida. O céu nublado usa uma aproximação difusa própria, sem reflexo solar direto.
- **Sol:** a cor da fonte considera a transmissão atmosférica; disco e reflexão usam a mesma radiância e raio angular de 0,00465 rad. A posição do sol nos presets anteriores foi mantida. No preset Dia, o astro pode ficar fora do enquadramento, continuando a iluminar a água.
- **Lua:** fonte mais alta que na P2, exposição noturna e balanço de cor aplicado ao iluminante antes da dispersão/reflexão. O reflexo passa a prateado. Continua uma escolha artística pré-exposta, sem simulação de fases, relevo lunar ou calibração absoluta em unidades fotométricas.
- **Cor:** compressão das cores fora da gama em direção à luminância mapeada, preservando gradação nas altas luzes. A saída continua com uma única conversão linear→sRGB. Não foram acrescentados bloom, TAA ou DLSS.

Na primeira captura de desenvolvimento, o céu apresentou um tom esverdeado, o reflexo lunar ficou demasiado amarelo e as nuvens perderam definição. Foram refinadas a absorção por ozono, a resolução/distribuição do mapa, a densidade das nuvens e a cor da fonte noturna antes desta entrega.

O modelo é uma implementação própria simplificada. A fundamentação de transmissão e dispersão está nas [funções atmosféricas de Bruneton](https://ebruneton.github.io/precomputed_atmospheric_scattering/atmosphere/functions.glsl.html); a [demonstração do autor](https://ebruneton.github.io/precomputed_atmospheric_scattering/atmosphere/demo/demo.cc.html) também documenta um perfil aproximado de ozono. Esta prova não implementa o modelo completo de múltipla dispersão dessa referência.

## Preservação da água

Comparação com `artifacts/ocean-p2/approved-baseline-20260927` e o [manifesto aprovado](references/ocean/p2-approved-source.sha256):

1. `OceanFrameClock`, `OceanWaveModel`, `OceanSpectrumSeed`, `OceanSpectrum` e `OceanSpectrum.hlsl` permanecem idênticos por SHA-256.
2. O corpo de `WaterVS` e o cálculo de derivadas/filtragem de normais em `WaterPS` permanecem textualmente idênticos. Câmara, bandas e ganhos da superfície foram conservados.
3. Readback GPU dos oito campos espectrais nas três bandas é idêntico ao alternar entre as quatro luzes e a iluminação anterior, no mesmo instante lógico.
4. A captura `ocean-sunset-previous-light.png` da P3 é **idêntica byte por byte** ao PNG `ocean-sunset-moderate.png` da P2 aprovada, a 960 × 540, t=10 s e agitação 0,65.

A aparência das ondas pode mudar com a luz incidente, mas a geometria e o movimento não foram recalibrados.

## Verificações efetuadas

Build Release: zero erros e zero avisos. **396 testes unitários passaram**. Além dos testes da P2, foram verificados limites e comportamento da transmissão atmosférica e coerência entre a energia do disco e o parâmetro de irradiância usado na dispersão.

Testes nativos na **NVIDIA GeForce RTX 5070 Ti**:

| Verificação | Resultado |
| --- | --- |
| IFFT contra DFT independente | 320 comparações continuam a passar |
| Preservação da superfície nas trocas de luz | Campos GPU idênticos |
| Mapa HDR | Todos os texels dos quatro presets finitos, RGB não negativo e transmissão entre 0 e 1 |
| Cache do céu | Não reconstrói por avanço de tempo, alteração de câmara ou qualidade/resolução interna no mesmo renderer; resize da janela recria o renderer e o cache |
| Retorno a um preset | Mesma imagem para os mesmos parâmetros e instante |
| 12 combinações de luz/mar | Capturas e conteúdo não vazio verificados; casos representativos inspecionados visualmente |
| Comparação de iluminação | Capturas da iluminação anterior para os quatro presets |
| Vista próxima | Capturas das quatro iluminações |
| GDI / DXGI / resize / recriação | Correspondência da imagem e continuidade verificadas |
| Retrato / ultrawide / 4K | Capturas geradas |
| Janela interativa | Pausa, resize, troca de lua/modelo/iluminação, retoma, captura e encerramento; 58 frames no ensaio |

Evidências locais: `artifacts/ocean-p3/captures/ocean-checks.json`, PNGs e sequência de 180 frames em `captures/frames`, TRX em `artifacts/ocean-p3/test-results` e ensaio da janela em `artifacts/ocean-p3/window-check`.

## Custo medido e limites

Sondagem curta de GPU a 960 × 540, céu já calculado:

| Configuração na mesma build | Mediana | p95 | Amostras |
| --- | --- | --- | --- |
| Iluminação P3 | 0,220 ms | 0,245 ms | 35 |
| Iluminação anterior | 0,212 ms | 0,215 ms | 24 |

Os números são indicativos, sujeitos a variação de execução, e não isolam individualmente todos os passes. Excluem apresentação/readback e o custo inicial de calcular o céu. Não representam consumo em watts, benchmark prolongado ou desempenho de iGPU. O mapa do céu acrescenta aproximadamente 10,67 MiB de texturas lógicas; o renderer da sondagem soma cerca de 56,55 MiB. Isso não equivale à residência total de VRAM.

Após o feedback, a melhoria das cores e o consumo observado são referências a preservar. Sol/lua e nuvens continuam sem aprovação final; seguir o [plano de refinamento](OCEAN_SKY_REALISM_PLAN.md). Também permanecem pendentes a comparação temporal dos reflexos com maior amostragem, medição prolongada/ponta a ponta e integração no produto. As nuvens são estáticas e aproximadas; não há múltipla dispersão física completa, relevo/fases lunares, estrelas ou oclusão entre ondas. Os presets usam RGB e exposição artística, sem afirmar calibração fotométrica final.

## Reprodução

```powershell
dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release --no-restore -p:OutDir=D:/desktopapp/AnimatedWallPaper/artifacts/ocean-p3/bin/ -p:UseSharedCompilation=false
dotnet artifacts/ocean-p3/bin/Hypnix.NativeSmoke.dll artifacts/ocean-p3/captures --ocean-only --ocean-frames
dotnet artifacts/ocean-p3/bin/Hypnix.NativeSmoke.dll artifacts/ocean-p3/window-check --ocean-window-check
dotnet test Tests/Hypnix.Tests/Hypnix.Tests.csproj -c Release --no-restore -p:OutDir=D:/desktopapp/AnimatedWallPaper/artifacts/ocean-p3/unit/ -p:UseSharedCompilation=false
```

Implementação: [OceanLightingModel](../Services/OceanLightingModel.cs), [OceanAtmosphere](../Services/OceanAtmosphere.cs), [shader atmosférico](../Shaders/OceanAtmosphere.hlsl), [renderer](../Services/OceanGpuRenderer.cs), [material e composição](../Shaders/Ocean.hlsl) e [verificações de iluminação](../Tests/Hypnix.NativeSmoke/OceanLightingChecks.cs).
