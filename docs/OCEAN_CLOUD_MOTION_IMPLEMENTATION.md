# Oceano — movimento e evolução das nuvens

28/09/2026. Implementação do [plano restrito às nuvens](OCEAN_CLOUD_MOTION_PLAN.md), sobre `c45863f`, branch `codex/ocean-and-day-cycle`.

## Comportamento entregue

As nuvens volumétricas agora têm transporte contínuo, deformação diferencial ao longo da altura e evolução lenta de suas bordas. Cúmulos evoluem mais rapidamente; estratocúmulos usam ritmo intermediário; estratos conservam evolução mais discreta. Os controles continuam sendo os mesmos: três tipos, cobertura 0/25/48/75/95% e vento parado/9/18 m/s.

Somente o campo das nuvens e o encaminhamento de seu estado foram alterados. Ondas, geometria, normais, BRDF/LUT dos reflexos, Sol, Lua, texturas lunares, atmosfera base, neblina, exposição, bloom e câmera mantêm seus modelos. A oclusão, as sombras e a luz refletida variam como consequência das nuvens em movimento, através dos mesmos quatro consumidores ópticos existentes.

`VCoordinates` e `VFogDensity` permanecem iguais. As nuvens usam `VCloudCoordinates`, com estado próprio. O caminho anterior `Weather=null` também foi preservado.

## Tempo, vento e continuidade

`OceanCloudMotion` pertence à prévia lógica, fora dos recursos GPU. Cada alteração de vento publica uma `OceanCloudTrajectory` imutável, com segmentos de velocidade e deslocamento integrados em double. O renderer recebe essa trajetória junto ao estado do quadro; todos os passes avaliam exatamente a mesma trajetória em cada tick.

O vento é integrado, em vez de multiplicar o tempo total pela nova velocidade. Para uma rampa de duração `R=3 s`:

```text
d(t) = d0 + v0*t + (v1-v0)*t²/(2R), para 0 ≤ t ≤ R
d(t) = d0 + (v0+v1)*R/2 + v1*(t-R), após R
```

Eventos rápidos substituem uma mudança ainda pendente; mudanças durante uma rampa começam da velocidade efetivamente alcançada. Eventos passados continuam disponíveis para avaliação determinística, inclusive dos snapshots futuros. Não há integração por frame.

**Detalhe necessário para o cache:** a rampa começa na próxima fronteira inteira do relógio meteorológico, produzindo atraso de resposta de até 1 s. Os intervalos dos três níveis de qualidade dividem um segundo. Assim, os endpoints já apresentados permanecem exatamente iguais; a nova previsão só muda estados posteriores. Isso evita que uma trajetória matematicamente contínua provoque um salto visual ao substituir o endpoint futuro de uma interpolação. A posição nunca é reiniciada. O tooltip do vento explica o atraso e a rampa.

A revisão da trajetória invalida todos os estados do cache. Estados completos são reconstruídos quando necessários; nunca se apresenta uma parte de um estado novo com outra parte do antigo. A recriação do renderer conserva a trajetória mantida pelo chamador.

O relógio meteorológico permanece a 1×. `Avançar 1 h`, a velocidade do dia e os botões Sol/Lua não adiantam as nuvens. Pausar/minimizar/recriar interrompe os relógios existentes sem recuperar o tempo parado depois. Vento zero interrompe gradualmente a advecção e a renovação induzida pelo vento; a evolução intrínseca continua lenta. **Pausar** congela ambas.

O vento ainda é um controle compartilhado do ambiente: o comportamento legado dos bancos de neblina ao alterá-lo permanece como antes. A nova integração contínua foi aplicada exclusivamente às nuvens.

## Modelo procedural

### Transporte principal

A velocidade selecionada agora corresponde ao módulo do transporte de referência. A direção visual predominante foi mantida: `(-3,-1)/sqrt(10)` no plano XZ. As coordenadas de consulta usam o sinal oposto. O deslocamento é reduzido em double pelo período espacial de 1.600 km; os campos novos usam também fases próprias, reduzidas antes da conversão para float.

A envoltória meteorológica é amostrada nas coordenadas materiais do transporte médio. Ela mantém a distribuição geral de cobertura. Dentro dessa envoltória, o warp e a erosão evoluem lentamente; a densidade continua limitada pelo perfil vertical e pela extinção máxima de 5/km. Cobertura zero retorna densidade de nuvem exatamente zero.

Uma otimização evita avaliar estrutura/erosão quando a envoltória tem cobertura nula. Isso compensa parte do custo das consultas adicionais, especialmente nos presets esparsos; não implica custo adicional nulo em céu fechado.

### Diferenças por altura e renovação local

O deslocamento diferencial cresce suavemente com `2h−1`, onde `h` é a altura normalizada entre base e topo. Ele contém um componente paralelo e outro transversal ao vento médio:

| Tipo | Altura existente | Coeficiente paralelo | Coeficiente transversal | Distância de renovação | Ritmo de evolução |
| --- | --- | --- | --- | --- | --- |
| Estratos | 0,65–1,25 km | 0,10 | 0,025 | 9 km | 0,30× |
| Estratocúmulos | 1,0–2,1 km | 0,15 | 0,050 | 6 km | 0,55× |
| Cúmulos | 1,1–3,1 km | 0,20 | 0,085 | 3 km | 1× |

São parâmetros de renderização, não medições meteorológicas. Antes da renovação, a componente paralela varia aproximadamente entre 0,8× e 1,2× no perfil de cúmulos; a componente transversal também altera o módulo e a direção resultantes.

Para evitar alongamento ilimitado, duas deformações com idades diferentes coexistem localmente:

```text
a = frac(distância_integrada / distância_de_renovação + 2*campo_meteorológico)
b = frac(a + 0,5)
w = sin²(pi*a)
densidade = w*estrutura(a) + (1-w)*estrutura(b)
```

Cada estrutura usa deslocamento diferencial proporcional a `(idade−0,5)`. No instante em que uma idade reinicia, seu peso e a derivada desse peso são zero. O campo meteorológico varia suavemente no espaço, portanto as regiões renovam em fases diferentes. Não há hash discreto criando uma fronteira entre nuvens, troca global de seed ou crossfade de imagens do céu.

**Ajuste em relação à candidata do plano:** as idades de cisalhamento são expressas pela distância integrada do vento, em vez de tempo desde um nascimento explícito. Isso incorpora o histórico das rampas no transporte médio e diferencial, sem usar `idade × vento atual` nem precisar enviar a lista de eventos ao shader. Parar o vento congela essa renovação sem reposicionar os volumes. A 9 m/s, 3/6/9 km correspondem aproximadamente a 5,6/11,1/16,7 minutos por volta da fase, com sobreposição local das duas estruturas.

Essa combinação é uma aproximação procedural de mudança de forma. Durante a renovação, a velocidade aparente de cada detalhe não é uma solução exata da equação de advecção. A massa principal conserva o transporte médio, enquanto suas estruturas se inclinam e se desfazem gradualmente.

### Evolução intrínseca

As consultas de warp/erosão recebem offsets materiais limitados com três períodos independentes, 601/887/997 s multiplicados pelo ritmo do tipo. Os senos são calculados na CPU em double sobre fases reduzidas. A combinação não reinicia todo o campo em intervalos curtos. As variações são espacialmente coerentes: regiões diferentes não crescem ou desaparecem ao mesmo tempo.

O campo de erosão muda independentemente do transporte; as bordas se desfazem e reaparecem, e a deformação muda o contorno dos lobos. O núcleo e a envoltória são preservados estatisticamente. Não se simula temperatura, umidade, conservação de água ou sublimação física. As formas ainda dependem do gerador e da resolução volumétrica anteriores.

## Recursos e integração

Nenhuma textura ou passe novo de produção foi criado. O constant buffer volumétrico ganhou três `float4` (48 bytes). A seed e o volume de ruído existentes são reutilizados. Não houve aumento dos passos de marcha, das resoluções ou da frequência do cache. O histórico CPU cresce somente quando se altera o vento, não a cada frame.

Arquivos centrais: `OceanCloudMotion.cs`, `OceanVolumetrics.cs`, `OceanVolumeCommon.hlsl`; encaminhamento da trajetória em `OceanGpuRenderer.cs` e `OceanProofWindow.cs`. O parâmetro opcional de trajetória permite aos testes e chamadores sem alterações interativas usar um vento constante reproduzível. Para preservar mudanças interativas entre recriações, o chamador deve manter a mesma trajetória fora do renderer, como faz a prévia.

## Validação reproduzível

- Build Release sem erros/avisos de compilação; **429 testes unitários passaram**. A consulta de vulnerabilidades do NuGet apresentou aviso de rede indisponível; a execução dos testes terminou com sucesso.
- Nove casos unitários novos cobrem integração da rampa, troca durante a rampa, coalescência de eventos pendentes, histórico imutável, módulo da velocidade, ordem de avaliação, vento parado e constantes em tempos longos.
- Matriz nativa de 45 combinações: três tipos × cinco coberturas × três ventos. Amostra a função de densidade usada em produção numa região de 96×96 km, em oito alturas, sem confundir iluminação com forma.
- Após compensar a advecção média, a densidade mudou em 120 s; cobertura zero permaneceu zero. O maior desvio de ocupação dessa matriz foi aproximadamente **0,354 ponto percentual**, abaixo do limite exploratório de 3 pontos. Ocupação significa densidade média vertical >0,05/km na grade de diagnóstico, não porcentagem da tela.
- A amostragem de dez minutos em 18 combinações (três tipos, seeds 0/27/101 e coberturas 25/75%, a 18 m/s) teve amplitude máxima de ocupação de **0,336 ponto percentual**. São amostras por minuto no espaço material; não uma medição contínua da porcentagem da tela.
- A densidade da neblina foi exatamente igual ao variar somente a trajetória das nuvens. A repetição da mesma avaliação de densidade foi exata.
- Alterar o vento em 10,25 s conservou as amostras de densidade dos endpoints atuais e a **imagem apresentada, pixel a pixel**, com neblina extra desligada para isolar a alteração. Cache incremental e recriação direta reproduziram a imagem usando o histórico da rampa.
- Foram amostradas emendas de renovação, um wrap de coordenada e tempos de 1 h, 24 h e 7 dias. Isso testa instantes sintéticos; não representa sete dias de execução contínua do aplicativo.
- Regressão volumétrica passou: água preservada por hash, transporte analítico, transmissão limitada, pausa/busca/recriação, preparação distribuída, vento independente, três qualidades e formatos de saída.
- A matriz de 20 poses de Sol/Lua nos dois modos de composição também passou, preservando o acompanhamento do reflexo.
- As **60 capturas sem nuvens** dessa matriz coincidiram exatamente por hash com a versão anterior salva. As funções de coordenadas/densidade da neblina também foram comparadas ao Git e permaneceram idênticas.

Os relatórios finais de cobertura de dez minutos, filmes e desempenho acompanham esta entrega em `docs/validation/`. Capturas e vídeos ficam em `artifacts/cloud-motion/`, ignorado pelo Git. Testes automáticos e amostras de frames não equivalem a uma certificação de fotorrealismo.

Dados: [densidade, continuidade e cobertura](validation/ocean-cloud-motion-checks-2026-09-28.json), [diferenças entre quadros](validation/ocean-cloud-motion-temporal-2026-09-28.json).

### Filmes e inspeção temporal

São três vídeos de 120 s/30 FPS, 960×540, um por tipo, com 48% e 9 m/s. A água permanece no mesmo instante; o céu avança a 1×, sem timelapse. São sequências geradas offline: sua duração de reprodução não mede FPS de execução.

O teste final mede diferenças RGB em uma amostragem espacial fixa **antes da codificação** e registra média, p95, máximo e índice do quadro. A primeira codificação CRF 19 mostrou diferenças periódicas nos keyframes do encoder; por isso a versão de inspeção usa CRF 0. Isso evita confundir compressão do vídeo com mudança de nuvem. A conversão YUV420 ainda não é preservação bit a bit do RGB original.

Esse teste detecta saltos globais, mas não substitui inspeção de contornos locais nem cálculo de fluxo óptico. A matriz completa de filmes para todas as coberturas, ventos e iluminação lunar permanece um refinamento de validação futura; os controles foram cobertos numericamente e a iluminação por regressão nativa.

Nos 10.800 quadros, o maior valor da diferença média RGB amostrada entre quadros consecutivos foi 0,0169 numa escala de 0–255, abaixo do limite de teste de 0,5. Foram inspecionados frames de início, meio e fim; a avaliação do movimento na prévia continua parte do refinamento visual com o usuário.

### Desempenho

Protocolo: RTX 5070 Ti, Equilibrado, saída 1920×1080, 30 FPS, ciclo 144×; 30 s de aquecimento + 3×120 s. Queries cobrem todos os passes volumétricos, inclusive preparação do próximo estado. A apresentação usa HWND oculto/readback/GDI; não mede consumo elétrico, temperatura nem custo do compositor visível.

Os resultados são registrados em [benchmark das nuvens](validation/ocean-cloud-motion-benchmark-2026-09-28.json). A sondagem curta inicial mediu 29,69 FPS e GPU p95 de 6,36 ms; o relatório longo é a referência da entrega. A estimativa de texturas permanece 155.521.648 bytes no caso padrão; não é medição de VRAM residente.

| Execução longa | FPS | CPU média da máquina | GPU p95 / p99 | GPU máximo | Intervalo p95 |
| --- | --- | --- | --- | --- | --- |
| 1 | 29,72 | 0,41% | 7,47 / 9,51 ms | 14,82 ms | 33,91 ms |
| 2 | 29,72 | 0,56% | 8,22 / 11,05 ms | 25,98 ms | 33,92 ms |
| 3 | 29,72 | 0,49% | 7,92 / 10,84 ms | 22,57 ms | 33,93 ms |

Foram 10.702 quadros, com 960 atualizações ambientais amostradas em cada execução e nenhuma query perdida. A cadência passou; existem picos de atualização acima de 20 ms, portanto não se declara uma garantia de GPU sempre abaixo desse valor. Primeira apresentação fria: 19,11 s; working set aproximadamente 185–187 MB. Os valores variam com compilação, driver e carga dos demais aplicativos.

Comparar com os ensaios anteriores permite observar tendência, mas não isola causalidade: os ensaios não foram intercalados A/B com controle térmico. Não afirmar a meta de acréscimo ≤15% como comprovada apenas com esses registros. Céu muito fechado pode custar mais porque quase não aproveita a saída antecipada da envoltória vazia; há também uma sondagem específica para cúmulos a 95% e 18 m/s.

A [sondagem de céu fechado](validation/ocean-cloud-motion-stress-2026-09-28.json), de 8 s após 2 s de aquecimento, manteve 29,71 FPS, GPU p95 de 6,36 ms e máximo de 13,75 ms. Isso cobre um caso curto, não uma garantia para todo céu fechado; extinção alta também pode encerrar marchas mais cedo.

## Reproduzir e usar

Na raiz do repositório:

```powershell
dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release -p:OutDir=D:/desktopapp/AnimatedWallPaper/artifacts/cloud-motion/bin/
dotnet artifacts/cloud-motion/bin/Hypnix.NativeSmoke.dll artifacts/cloud-motion/checks --ocean-cloud-motion
dotnet artifacts/cloud-motion/bin/Hypnix.NativeSmoke.dll artifacts/cloud-motion/volumes --ocean-volumes
dotnet artifacts/cloud-motion/bin/Hypnix.NativeSmoke.dll artifacts/cloud-motion/reflection --ocean-reflection
dotnet artifacts/cloud-motion/bin/Hypnix.NativeSmoke.dll artifacts/cloud-motion/benchmark --ocean-volume-benchmark
dotnet artifacts/cloud-motion/bin/Hypnix.NativeSmoke.dll artifacts/cloud-motion/stress --ocean-cloud-stress-benchmark --quick
dotnet artifacts/cloud-motion/bin/Hypnix.NativeSmoke.dll artifacts/cloud-motion/window --ocean-window-check
dotnet test Tests/Hypnix.Tests/Hypnix.Tests.csproj -c Release
./scripts/show-ocean-preview.ps1
```

O script com `-Validate` inclui os novos testes antes de abrir. Para gerar os filmes, usar `--ocean-cloud-video` seguido do caminho de um encoder FFmpeg disponível localmente; nenhuma instalação é exigida pelo renderer.

Na prévia, manter **Ambiente volumétrico** ligado. Usar o tipo e a porcentagem já existentes; 9 ou 18 m/s torna o deslocamento mais fácil de acompanhar. Comparar o contorno por 30–120 s. **Vento parado** e **Pausar** têm os comportamentos distintos descritos acima. O próximo refinamento deve continuar dentro das nuvens, sem reabrir a água ou os astros aprovados.
