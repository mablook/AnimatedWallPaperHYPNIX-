# Oceano — ambiente volumétrico, primeira implementação

28/09/2026. Base preservada em `11b5fe0`, branch `codex/ocean-and-day-cycle`.
Referência: [estudo de transporte, nuvens e neblina](OCEAN_VOLUMETRIC_ENVIRONMENT_STUDY.md).

Revisão posterior: [correção da resposta rasante e acompanhamento dos reflexos solar/lunar](OCEAN_REFLECTION_TRACKING_FIX.md), com energia refletida compatível com a rugosidade e testes de projeção do reflexo ao longo do ciclo.

Próximo plano, ainda não implementado: [movimento e evolução apenas das nuvens](OCEAN_CLOUD_MOTION_PLAN.md), preservando os controles existentes e os demais componentes do ambiente.

## Entrega

A prévia nativa tem um novo caminho para atmosfera, nuvens, névoa e iluminação da água. O controle **Ambiente volumétrico** permite alternar com o renderer aprovado. A geometria, os espectros de ondas, os deslocamentos e o cálculo das normais continuam iguais. A aparência da água muda pela iluminação incidente, pelas sombras e pelo ar entre superfície e câmera.

O novo modo começa ativo na prévia e exige **Ciclo do céu** + **Céu refinado**. A galeria e o wallpaper instalado não são substituídos: esta continua sendo a prévia experimental do oceano, conforme as etapas anteriores. `Weather = null` mantém o caminho anterior para comparação e regressão.

Controles acrescentados:

- Estratocúmulos, cúmulos e estratos: perfis verticais e formas diferentes.
- Sem neblina adicional, bruma marítima, névoa baixa e bancos de neblina.
- Cobertura do campo de nuvens: 0, 25, 48, 75 ou 95%; o valor controla o gerador, não mede a porcentagem final da imagem.
- Vento parado, 9 ou 18 m/s. O estado interno admite 0–30 m/s e seed determinística.
- Comparação A/B imediata pelo controle **Ambiente volumétrico**.

**Ver Sol**, **Ver Lua**, ciclos acelerados, mapa lunar real, fases, refração, exposição e brilho óptico permanecem disponíveis. Nuvens podem ocultar os astros; **Sem nuvens** permite inspecionar os discos isoladamente. **Sem neblina** retira apenas as gotículas extras: a atmosfera molecular/aerossóis continua presente.

## Transporte e composição

Unidades internas do novo volume: quilômetros, coeficientes de extinção por quilômetro e RGB linear antes da exposição. A água continua usando metros; a conversão ocorre na fronteira do novo código.

Cada segmento acumula `S` (radiância dispersada) e `T` (transmissão RGB):

```text
S += T * fonte * (1 - exp(-extincao * distancia)) / extincao
T *= exp(-extincao * distancia)
cor_final = S + T * cor_da_superficie
```

No limite de extinção pequena, uma série polinomial evita cancelamento numérico em FP32. O teste no GPU encontrou perda de precisão na subtração direta antes dessa correção. A diferença de raios usada para altitude esférica também foi racionalizada para preservar alturas próximas da superfície.

Rayleigh, aerossóis/Mie, absorção de ozônio, gotículas de névoa e nuvens somam extinção **dentro do mesmo passo**. A composição não aplica dois efeitos completos e independentes sobre um trecho sobreposto. A bruma marítima pode alcançar a base dos estratos; esse caso permanece dentro da integração conjunta.

Os trajetos são distintos:

1. **Céu visto pela câmera:** integração até o limite atmosférico de 100 km de altitude, segmentada pela camada baixa e pela camada de nuvens. O cache exclui os discos celestes.
2. **Astro → superfície:** campo espacial com dois canais de transmissão, um solar e outro lunar. Substitui a antiga atenuação angular da luz direta sobre o mar. A transmissão atmosférica RGB continua separada da transmissão extra de nuvens/gotículas.
3. **Ambiente → superfície:** nove probes de radiância incidente, calculados a partir de posições na superfície. A reflexão não recebe o céu final já enevoado da câmera.
4. **Superfície → câmera:** consulta de `S/T` à distância real do fragmento. Substitui a antiga mistura de fog fixa `1-exp(-distancia*.0007)` no novo modo.

A fase usa Henyey–Greenstein normalizada e combinação de dois lobos para nuvens. Espalhamento múltiplo e iluminação difusa são aproximações limitadas; o campo difuso do crepúsculo também ilumina gotículas para evitar silhuetas pretas sob um céu ainda claro. Isto não é solução espectral nem referência de transporte de ordem arbitrária.

## Campo de nuvens e neblina

O ruído 3D é gerado no GPU: campo periódico próprio de value noise + distância celular, com mipmaps. Não foram incorporados assets novos de terceiros. O mapa lunar e suas atribuições anteriores permanecem.

A densidade combina cobertura meteorológica de baixa frequência, massas em várias escalas, deformação do domínio, perfil vertical e erosão. O mesmo campo em coordenadas de mundo alimenta câmera, auto-sombra, sombra na água e probes. Os mipmaps filtram frequências que um passo longo não consegue resolver. A quadratura determinística concentra amostras na parte próxima do segmento; foi preferida ao jitter por pixel, que produziu ruído visível no horizonte nesta implementação sem reprojeção.

| Perfil | Base/topo | Característica |
| --- | --- | --- |
| Estratocúmulos | 1,0–2,1 km | Massas baixas, perfil mais achatado |
| Cúmulos | 1,1–3,1 km | Maior desenvolvimento vertical |
| Estratos | 0,65–1,25 km | Camada mais uniforme |

| Névoa extra | Extinção ao nível do mar | Altura exponencial | Variação horizontal |
| --- | --- | --- | --- |
| Sem neblina | 0/km | — | — |
| Bruma marítima | 0,065/km | 180 m | Uniforme |
| Névoa baixa | 0,60/km | 85 m | Uniforme |
| Bancos | 1,8/km, antes do fator espacial | 65 m | Campo advectado pelo vento |

Esses valores são presets de renderização, não previsões meteorológicas nem distâncias de visibilidade MOR calibradas. A densidade desvanece entre três e cinco alturas exponenciais. A nuvem usa ganho máximo nominal de extinção de 5/km sobre o campo de densidade.

## Recursos, qualidade e atualização

| Recurso por estado | Leve | Equilibrado | Alto |
| --- | --- | --- | --- |
| Céu RGBA16F, com mipmaps | 768×192 | 1536×384 | 3072×768 |
| Passos nuvens/ar/névoa | 24/12/8 | 40/20/12 | 64/28/20 |
| `S` e `T`, duas texturas 3D RGBA16F | 64×32×32 | 128×64×48 | 192×96×64 |
| Subpassos por fatia de perspectiva aérea | 2 | 2 | 3 |
| Transmissão solar/lunar RG16F | 64×64×16 | 128×128×16 | 192×192×16 |
| Passos de sombra de nuvem | 8 | 12 | 20 |
| Probes RGBA16F, nove camadas com mipmaps próprios | 128×64×9 | 256×128×9 | 384×192×9 |
| Intervalo entre estados | 1 s | 0,5 s | 0,5 s |
| Texturas novas, três estados + ruído, estimativa | 11,64 MiB | 49,14 MiB | 154,14 MiB |

O cache de perspectiva aérea usa coordenadas angulares e distâncias quadráticas até 32 km. A interpolação entre fatias usa distância física, incluindo a fatia zero com `S=0/T=1`. Não há novo depth buffer nem froxels ligados à resolução da tela.

O campo de luz cobre ±16 km horizontalmente e da superfície ao topo das nuvens, com 16 alturas quadráticas. Os probes estão em `x = -2/0/2 km`, `z = 0/2/8 km`. A radiância incidente é interpolada espacialmente entre quatro probes e filtrada pela rugosidade/pegada do reflexo. Fora dos limites há clamp: isso é uma aproximação explícita, não transporte exato de todo ponto do oceano.

**Três estados, sem histórico de imagem:** dois estados completos são interpolados para apresentação; o terceiro é preparado antecipadamente em quatro passes: transmissão, céu, perspectiva aérea, probes. Cada passe acontece em um quadro diferente quando o tempo avança. Só um estado completamente pronto pode ser apresentado. Após seek, mudanças de parâmetros ou recriação, estados ausentes são calculados integralmente. Pausa não continua calculando o estado futuro. O teste compara a imagem obtida por preparação incremental com a obtida por reconstrução direta no mesmo instante.

O tempo meteorológico tem relógio próprio a 1×; acelerar as ondas ou comprimir um dia não multiplica o vento. Os três relógios pausam juntos. A grade de estados continua expressa em segundos meteorológicos; em um ciclo celeste acelerado, cada estado usa a data correspondente daquele ciclo.

O compilador mantém bytecode em memória, por hash do HLSL expandido + entrada + perfil. Resize e recriação no mesmo processo reutilizam o bytecode. A primeira abertura ainda compila shaders; o prazo de inicialização da prévia passou de 15 para 45 segundos. Não há cache persistente em disco.

As resoluções de saída seguem a política anterior da água. Em Equilibrado, uma saída 3840×2160 é renderizada internamente em 1920×1080; uma saída 3440×1440 usa 2225×931. Captura de saída 4K não significa render nativo 4K nesse nível.

## Verificação

- Build Release sem erros ou avisos de compilação.
- 420 testes unitários passaram, incluindo composição de segmentos, integral da fase, normalização dos presets e independência dos relógios.
- Kernel de referência executa o passo real de transporte no GPU e compara com solução analítica em double: vácuo, fontes constantes, extinções 0 a 10/km, canais RGB e subdivisão em 1/17 passos. Tolerância absoluta de 1e-6 ou relativa de 2e-5.
- Hashes dos campos espectrais/deslocamentos/derivadas preservados nos cenários de nuvens, fog e vento.
- Pausa exata, busca A→B→A, recriação de recursos e preparação incremental produzem imagens determinísticas.
- Leitura integral dos volumes de perspectiva aérea verifica radiância/transmissão finitas, não negativas, `T≤1` e identidade na fatia zero.
- Capturas de amanhecer, meio-dia, estratos ao entardecer, Lua baixa/mais alta, céu limpo, bruma, névoa baixa e bancos; três níveis de qualidade; retrato, ultrawide e saída 4K.
- Regressão do ciclo anterior executada com `Weather=null`.
- Teste da janela finalizou normalmente após 185 quadros: pausa, resize/recriação, botões Sol/Lua, troca de ciclo/presets, comparação A/B, bancos de neblina e tipos de nuvem.

As capturas e JSON de testes ficam em `artifacts/ocean-volumes/`, ignorado pelo Git. O relatório de desempenho reproduzível acompanha este documento em `docs/validation/`. A validação automática não representa aprovação visual do usuário nem prova de fotorrealismo.

## Desempenho medido

Protocolo final: RTX 5070 Ti, saída 1920×1080, Equilibrado, 30 FPS, ciclo 144×, 30 s de aquecimento + três execuções de 120 s. Queries cobrem todos os passes, inclusive preparação antecipada. Apresentação inclui readback e GDI em HWND oculto; não inclui a composição de uma janela visível, potência ou temperatura.

| Execução | FPS | CPU média da máquina | GPU p95 / p99 | GPU máximo | Intervalo p95 |
| --- | --- | --- | --- | --- | --- |
| 1 | 29,72 | 0,68% | 8,32 / 11,37 ms | 22,66 ms | 33,95 ms |
| 2 | 29,71 | 0,71% | 8,23 / 11,25 ms | 24,86 ms | 33,95 ms |
| 3 | 29,72 | 0,71% | 8,69 / 11,48 ms | 28,86 ms | 33,94 ms |

Foram 10.700 quadros medidos, sem queries perdidas. A preparação antecipada foi incluída em 960 quadros de cada execução. O campo histórico `cloudUpdateSamples` no JSON conta agora qualquer passe de atualização ambiental, não apenas um estado completo de nuvens. Primeira apresentação com compilação fria: 15,03 s. Estimativa de texturas do renderer: 148,25 MiB; working set do processo no fim de cada execução: aproximadamente 175–177 MiB. Bytes lógicos não medem VRAM residente ou alinhamento do driver. Alternar A/B pode manter recursos de ambos os caminhos alocados até fechar/recriar o renderer.

O critério de cadência (≥28 FPS e intervalo p95 ≤40 ms) passou. As metas exploratórias de GPU p95 ≤8 ms e pico ≤20 ms **não foram integralmente atingidas**; p99 permaneceu abaixo de 16 ms. A cena ficou estável perto de 30 FPS nesta máquina, mas há espaço para reduzir o custo dos passes mais caros. Esta foi uma execução local com outros aplicativos abertos, sem isolamento térmico ou medição de energia; não se atribui a diferença inteira entre execuções ao shader.

Dados completos: [benchmark](validation/ocean-volumes-benchmark-2026-09-28.json) e [verificações nativas](validation/ocean-volumes-checks-2026-09-28.json). A sondagem curta de 8 s mediu p95 menor (5,76 ms), mas os números longos acima são a referência desta entrega.

## Limites desta primeira versão e próximo refinamento

- As formas ainda podem revelar camadas de amostragem em luz rasante, sobretudo no nível Leve. Mais resolução não valida automaticamente a aparência; refinamento de densidade, filtragem e comparação temporal continuam necessários.
- Auto-sombra usa um campo de transmissão com resolução limitada; fora dele, a consulta é aproximada ou usa marcha direta. Não há sombras precisas de todas as cristas do mar sobre a névoa.
- O espalhamento múltiplo e o ambiente difuso são aproximações de baixa frequência. Não há path tracing nem integral completa do céu por ponto.
- Os nove probes reduzem o erro de refletir o céu final da câmera, mas não resolvem parallax/reflexão volumétrica exatos. O hemisfério inferior continua um proxy escurecido; o domínio da água e a câmera fixa aprovados foram preservados.
- A camada de névoa e os bancos são volumes amostrados; não simulam condensação, fluido, spray ou umidade real.
- Inicialização, seek e mudança de qualidade têm custo síncrono maior do que a animação contínua. A preparação antecipada reduz picos durante playback, não elimina o custo de construir uma cena nova.
- Permanecem para uma etapa posterior: comparação contra referência de alta amostragem com erro reportado, filmes críticos de 60 s e inspeção quadro a quadro, medição de potência/temperatura, duas saídas simultâneas e teste de remoção física do dispositivo. Recriar recursos não equivale a testar perda real de dispositivo.

Em relação ao estudo, esta entrega implementa a base funcional de V0–V4 e o cache distribuído de V5. V6 continua sendo revisão visual iterativa; froxels, reprojeção temporal e adaptação por erro não foram necessários para este primeiro caminho e não são declarados implementados.

## Reproduzir

Na raiz do repositório, PowerShell:

O atalho habitual `./scripts/show-ocean-preview.ps1` também recompila e abre esta versão. Com `-Validate`, executa a verificação anterior e a nova matriz volumétrica antes de abrir.

```powershell
dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release -p:OutDir=D:/desktopapp/AnimatedWallPaper/artifacts/ocean-volumes/bin/
dotnet artifacts/ocean-volumes/bin/Hypnix.NativeSmoke.dll artifacts/ocean-volumes/final --ocean-volumes
dotnet artifacts/ocean-volumes/bin/Hypnix.NativeSmoke.dll artifacts/ocean-volumes/legacy-cycle --ocean-cycle
dotnet artifacts/ocean-volumes/bin/Hypnix.NativeSmoke.dll artifacts/ocean-volumes/benchmark-final --ocean-volume-benchmark
dotnet artifacts/ocean-volumes/bin/Hypnix.NativeSmoke.dll artifacts/ocean-volumes/window-check --ocean-window-check
dotnet test Tests/Hypnix.Tests/Hypnix.Tests.csproj -c Release
```

Para abrir a prévia sem console:

```powershell
dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release -p:OceanPreview=true -p:OutDir=D:/desktopapp/AnimatedWallPaper/artifacts/ocean-volumes/preview/
Start-Process -FilePath artifacts/ocean-volumes/preview/Hypnix.NativeSmoke.exe -ArgumentList 'artifacts/ocean-volumes/preview-captures','--show-ocean' -WorkingDirectory D:/desktopapp/AnimatedWallPaper -WindowStyle Normal
```

Arquivos centrais: `OceanWeatherSettings.cs`, `OceanVolumetrics.cs`, `OceanVolumeCommon.hlsl`, `OceanVolume.hlsl`, `OceanVolumeSampling.hlsl`, integração em `OceanGpuRenderer.cs`/`Ocean.hlsl` e controles em `OceanProofWindow.cs`. Nenhuma biblioteca nova é exigida por esta implementação.
