# Oceano — implementação do refinamento do céu

Data: 2026-09-27. Base preservada **antes de implementar**, conforme pedido: commit `5977ebe` (`feat(ocean): preserve approved water and initial atmospheric preview`). Implementação do [plano do céu](OCEAN_SKY_REALISM_PLAN.md). A aprovação estética final continua sendo feita na prévia, pelo utilizador.

**Checkpoint solicitado pelo utilizador:** “salvemos o trabalho, estamos no caminho certo”. Preservar este estado como base da próxima iteração: água e nuvens aprovadas, mapa lunar real mais legível e aumento aparente dos astros junto ao horizonte. Última validação: 398 testes unitários, 24 cenas nativas, continuidade dos caches, ocultação parcial, preservação da água e janela interativa sem erro. As capturas e vídeos locais permanecem em `artifacts/ocean-r1/` e `artifacts/ocean-r2/`; os comandos e critérios para os reproduzir estão neste documento.

## Ajuste posterior aprovado — textura lunar e tamanho no horizonte

Pesquisa posterior, sem alteração desta implementação: [estudo do ciclo solar/lunar](OCEAN_CELESTIAL_CYCLE_STUDY.md), com efemérides NASA/JPL, curvas de refração/extinção, tamanho físico versus ampliação artística e plano para evolução contínua de cor, brilho, aura e movimento.

Após aprovar água e nuvens, o utilizador pediu uma lua reconhecível com crateras e um tamanho aparente maior junto ao horizonte, também para o sol. A prévia passa a iniciar com “Astros maiores no horizonte” ligado; desligar permite comparar a escala angular anterior.

O mesmo mapa real NASA/LROC, sem edição do asset, é projetado sobre o disco lunar. A filtragem agora acompanha o diâmetro aparente maior, preservando mais detalhe em vez de ampliar uma textura já desfocada. Um contraste local discreto (expoente 1,18) melhora a leitura das manchas e crateras no disco visível. Não foram inventadas crateras procedurais nem substituído o mapa por uma esfera cinzenta. O detalhe legível continua limitado pela resolução de saída e pelo diâmetro em píxeis.

A ampliação é uma escolha perceptiva/artística: máximo de 3,2× no horizonte, diminuindo por smoothstep até 1× a 25° de elevação. Nos presets atuais resulta em cerca de 2,65× para a lua e 3,03× para o pôr do sol. A curva está pronta para outras elevações, mas este ajuste não acrescenta um ciclo animado de nascer/pôr dos astros aos presets existentes. O efeito de lua maior no horizonte é uma ilusão perceptiva, não crescimento físico: [NASA — Moon Illusion](https://science.nasa.gov/solar-system/moon/the-moon-illusion-why-does-the-moon-look-so-big-sometimes/).

O raio visual é separado do raio da fonte de iluminação. O diâmetro aumentado não multiplica a energia, nem altera o campo de ondas, o céu volumétrico ou a reflexão aprovada. É uma representação perceptiva, portanto não uma simulação de fonte extensa fisicamente ampliada. O bloom continua sendo óptico e pode espalhar brilho nas vizinhanças do disco.

Validação em `artifacts/ocean-r2/`: **398 testes unitários aprovados**; inspeção das capturas cheia/gibosa e do sol em 1080p/4K; upload NASA exato; cache/pausa/ocultação parcial aprovados. O teste isolado com bloom desligado verificou igualdade exata de todos os píxeis da metade inferior da imagem ao alternar a ampliação, tanto para Lua como para Pôr do sol, sem reconstruir os caches. A área luminosa do disco passou de 168 para 1.129 píxeis na lua e de 180 para 1.453 no sol, no critério do teste a 1080p. As medições prolongadas apresentadas adiante referem-se à implementação anterior deste documento; não foram repetidas como novo benchmark prolongado deste ajuste localizado.

As secções seguintes preservam a descrição e medições da entrega inicial. A regra anterior de raio visual sempre fixo foi substituída pelo controle perceptivo descrito acima; o raio físico da iluminação permanece fixo.

## O que mudou

| Etapa | Implementação |
| --- | --- |
| R1 — resposta óptica | Extração de altas luzes em 1/4 da resolução, filtragem separável e composição antes do tone mapping. Controlo “Brilho óptico” para comparação. Antialiasing do disco ajustado, sem aumentar o raio angular. |
| R2 — material lunar | Mapa NASA/LROC real de 2048×1024, sRGB, mipmaps gerados em espaço linear, projeção esférica da face visível. Lua cheia e gibosa, com terminador e energia ambiente dependente da fase. |
| R3 — nuvens | Densidade procedural 3D numa camada de 1,2–3,2 km; cobertura meteorológica, perfil vertical e erosão. Integração da transmissão, auto-sombra na direção da luz e iluminação ambiente aproximada. |
| R4 — integração | O mesmo campo de radiância/transmissão alimenta céu e reflexão. Ocultação por direção do disco e de cada amostra da fonte direta. Ambiente refletido usa mipmaps, sem modificar normais. |
| R5 — movimento | Vento lento, tempo lógico partilhado com o oceano, dois instantes completos em cache e interpolação. Pausa congela imagem e cache. Recriar/voltar ao instante reconstrói a mesma formação. |
| R6 — qualidade | Três orçamentos distintos de resolução, amostras, auto-sombra e frequência. Equilibrado continua padrão. Medição inclui atualizações do cache e cópia/apresentação GDI. |

Na prévia, “Céu estudo 03” permite comparar com o estado anterior; “Iluminação anterior” mantém também a comparação P2. A vista, a agitação e a escolha de ondas continuam disponíveis. O seletor de fase atua no modo Lua.

## Superfície preservada

Os hashes de `OceanFrameClock`, `OceanWaveModel`, `OceanSpectrumSeed`, `OceanSpectrum` e `OceanSpectrum.hlsl` continuam iguais à [referência aprovada P2](references/ocean/p2-approved-source.sha256). A comparação textual com `5977ebe` confirma igualdade integral de `WaterVS` e do trecho de `WaterPS` até ao cálculo de `n`.

A validação nativa compara por SHA-256 os oito campos espectrais das três bandas, no mesmo instante, através de todas as iluminações. A alteração dos píxeis refletidos é esperada: a superfície é a mesma, mas o céu que ela reflete mudou.

## Mapa lunar e exposição

O ficheiro original, sem edição, está em `Assets/Effects/Ocean/lroc_color_2k.jpg`, acompanhado de `CREDITS.txt`. SHA-256: `F7130A1822681FA7512D7DCFD40DB8C10B9BA4F06777910348698260ED7A2170`. Fonte: [NASA Scientific Visualization Studio — CGI Moon Kit](https://svs.gsfc.nasa.gov/4720/); visualização de Ernie Wright, dados LROC/Arizona State University. A NASA identifica este mapa como adaptado para visualização, não como albedo científico bruto.

O shader mantém o raio de 0,00465 rad e o FOV vertical de 42°. Por isso a lua é pequena num enquadramento amplo; em 4K há mais amostras para as manchas lunares. Não foi acrescentado relevo geométrico lunar: nesta escala o mapa de cor filtrado fornece o detalhe visível útil.

O material mistura Lommel–Seeliger e Lambert. A fase gibosa usa ângulo de 0,85 rad; a integral projetada da resposta é aproximadamente 0,714779 da fase cheia, usada pela atmosfera e pela iluminação das nuvens. A reflexão direta amostra a mesma distribuição lunar; a sua integral com oito amostras é uma aproximação, não fotometria de alta precisão.

**Decisão artística explícita:** o disco lunar visível recebe exposição local para preservar as manchas em SDR. A iluminação da água mantém a escala pré-exposta do iluminante aprovado. Não se trata de uma única exposição física de uma câmara calibrada. Isto resolve o disco totalmente branco da P3 sem escurecer toda a cena noturna. A cor lunar vem do mapa; o equilíbrio de branco noturno continua deliberado.

O bloom atua sobre a cena completa, incluindo reflexos fortes. Tem joelho suave, energia de extração limitada a 18 e ganho de 0,14. Evita espalhar a radiância solar muito elevada por todo o enquadramento. É uma resposta óptica artística económica; não simula uma lente particular nem transforma pontos de brilho em estrelas artificiais.

## Cache, movimento e limites da aproximação

`OceanAtmosphere` calcula apenas a atmosfera limpa quando o refinamento está ativo. O cache continua guardando o estado corrente; A→B→A reconstrói, e a recriação do renderer também. O movimento das nuvens não refaz a integração atmosférica.

`OceanClouds` guarda radiância RGB e transmissão em duas texturas RGBA16F com mipmaps. Para `t`, calcula os estados `floor(t×Hz)` e o seguinte. Em continuidade normal constrói apenas um novo estado por fronteira; pausa não gera trabalho novo. A GPU termina o dispatch e os mipmaps antes de usar o recurso no desenho. Não se mistura um mapa parcialmente atualizado com outro completo.

O vento usa deslocamentos de 9 e 3 m/s. As coordenadas são reduzidas em double a um período comum da função de ruído, evitando perder precisão com horas de funcionamento. A interpolação a 2 Hz desloca a formação poucos metros entre amostras. O ruído de integração é estável por direção, sem variar aleatoriamente entre frames.

| Qualidade | Cache de nuvens por instante | Passos de vista / sombra | Atualização | Bloom |
| --- | --- | --- | --- | --- |
| Leve | 1024×256 | 20 / 2 | 1 Hz | Desligado |
| Equilibrado | 2048×512 | 48 / 4 | 2 Hz | Opcional, ligado por padrão |
| Alto | 4096×1024 | 64 / 6 | 2 Hz | Opcional, ligado por padrão |

Leve usa o mesmo volume com menos amostras, em vez de uma formação 2.5D completamente diferente. A decisão mantém coerência ao trocar qualidade. Alto privilegia resolução e integração, mantendo 2 Hz; a proposta inicial de 5 Hz não é requisito para este vento lento.

O par de caches ocupa aproximadamente 5,33 / 21,33 / 85,33 MiB. Acrescentam-se 10,67 MiB para atmosfera, 10,67 MiB para mapa lunar com mips, bloom, cena, apresentação e recursos espectrais existentes. `EstimatedTextureBytes` informa a soma lógica aproximada; não mede residência real ou despesas do driver.

Limitações deliberadas: ambiente angular distante, sem paralaxe de nuvens para uma câmara em deslocamento; sem sombras posicionais de nuvens projetadas sobre pontos individuais do mar; dispersão múltipla aproximada por iluminação de preenchimento; filtragem isotrópica aproximada do ambiente. A ocultação/reflexão é coerente com este campo angular. Não há DLSS, ray tracing de hardware ou cálculo volumétrico por píxel da água a cada frame.

## Validação e medições

Resultados reproduzíveis ficam em `artifacts/ocean-r1/`:

- `test-results/`: 397 testes unitários aprovados, incluindo integração independente da resposta da fase lunar.
- `captures/`: mar calmo/moderado/agitado nas quatro iluminações, vistas próximas, retrato, ultrawide, 4K, comparação anterior e instante de dez horas.
- `refinement/`: matriz de qualidades em 1080p, cheia/gibosa em 1080p e 4K, comparação P3 e bloom desligado; readback do mapa lunar igual a todos os bytes decodificados da NASA; HDR/transmissão das nuvens válidos; cache, movimento, pausa e retorno ao instante determinísticos.
- O teste controlado de ocultação parcial conservou 99,5% da luminância num lado do disco e 18,7% no outro; o mesmo campo também alterou a iluminação da água. Isto verifica amostragem por direção, em vez de uma única atenuação uniforme.
- `motion/ocean-refinement-60s.mp4`: 1.800 frames a 30 FPS, pôr do sol e lua; pausa de dois segundos verificada por igualdade exata dos píxeis e retoma do tempo lógico. O vídeo é uma sequência renderizada para inspeção, não uma gravação de desempenho em tempo real.
- `window-check/`: janela nativa abriu, apresentou 53 frames, pausou, mudou iluminação, redimensionou, alternou comparadores, retomou, capturou e encerrou sem erro.
- `benchmark/`: protocolo de 30 s de aquecimento + três repetições de 120 s; registo de cada frame na amostragem GPU, sem excluir reconstruções. Inclui cópia GPU→CPU e GDI numa HWND oculta. Não mede custo visual do compositor/desktop, temperatura ou energia atribuível ao efeito.

O primeiro ensaio com espera comum do Windows falhou a meta: 23,35 FPS, apesar de o p95 de desenho/apresentação ser 10,87 ms. A espera acrescentava atraso suficiente para p95 de intervalo de 50,32 ms. O resultado original foi guardado em `benchmark/before-frame-pacer.json`.

A correção usa um waitable timer de alta resolução, limitado à prévia do oceano e ao seu benchmark. Não usa espera ocupada, não modifica a resolução global dos temporizadores e não altera os workers de outros wallpapers. O recurso é encerrado ao fechar a prévia. API: [Microsoft — CreateWaitableTimerExW](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-createwaitabletimerexw) e [SetWaitableTimer](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-setwaitabletimer).

Resultados corrigidos, **RTX 5070 Ti, 1920×1080 de saída e interna, Equilibrado, alvo 30 FPS**, um renderer no ensaio:

| Repetição de 120 s | FPS | GPU p95 | GPU máximo de frame com atualização | Frame com GDI p95 | Intervalo p95 | CPU / máquina |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 29,685 | 5,774 ms | 16,618 ms | 9,927 ms | 34,014 ms | 0,702% |
| 2 | 29,678 | 5,813 ms | 20,392 ms | 9,916 ms | 34,029 ms | 0,734% |
| 3 | 29,679 | 5,775 ms | 17,378 ms | 9,887 ms | 34,041 ms | 0,734% |

As três repetições passaram as metas de ≥28 FPS e intervalo p95 ≤40 ms. Foram medidos 10.687 frames e 720 frames de atualização, sem perder amostras GPU. Abertura fria até à primeira apresentação: 1.295 ms. Texturas lógicas: aproximadamente 120,2 MiB; working set CPU terminou em aproximadamente 154,5 MiB. Esses valores não são uma medição de toda a VRAM do processo.

Uma consulta pontual do `nvidia-smi` durante o ensaio anterior indicou 22% de GPU, 28,14 W e 46°C para a placa inteira. Não há baseline energético/controlos suficientes para atribuir esses valores ao oceano; por isso não são usados como meta ou promessa de consumo. A sondagem curta de GPU sem apresentação, a 960×540, também não é comparável diretamente ao benchmark acima.

O ensaio de 30 minutos e a validação noutros adaptadores não estão certificados por esta entrega. Os resultados desta máquina não garantem consumo equivalente noutras GPUs.

## Reproduzir

```powershell
dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release --no-restore -p:OutDir=D:/desktopapp/AnimatedWallPaper/artifacts/ocean-r1/bin/ -p:UseSharedCompilation=false
dotnet artifacts/ocean-r1/bin/Hypnix.NativeSmoke.dll artifacts/ocean-r1/captures --ocean-only
dotnet artifacts/ocean-r1/bin/Hypnix.NativeSmoke.dll artifacts/ocean-r1/refinement --ocean-refinement
dotnet artifacts/ocean-r1/bin/Hypnix.NativeSmoke.dll artifacts/ocean-r1/benchmark --ocean-benchmark
dotnet artifacts/ocean-r1/bin/Hypnix.NativeSmoke.dll artifacts/ocean-r1/window-check --ocean-window-check
./scripts/show-ocean-preview.ps1
```

Para gravar a sequência determinística de 60 segundos, passar `--ocean-video` seguido do caminho de um FFmpeg já instalado. O teste inclui dois segundos com todos os píxeis congelados e retoma sem recuperar o tempo pausado.
