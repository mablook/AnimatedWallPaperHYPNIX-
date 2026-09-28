# Oceano — estudo de nuvens, neblina e integração volumétrica

Data: **28/09/2026**. Base auditada: **`ed376a5`**, branch `codex/ocean-and-day-cycle`. Escopo desta entrega: **pesquisa, auditoria e plano; nenhuma alteração ao renderizador**. Preferência mantida: equilibrar realismo e consumo, com níveis de qualidade.

## 1. Decisão recomendada

O próximo ganho de realismo deve vir da **coerência da luz entre céu, nuvens, ar e água**, seguido da morfologia e iluminação das nuvens. A base já possui densidade volumétrica 3D; aumentar resolução ou passos isoladamente não resolve as aproximações de composição.

Recomendação para a câmera estável do wallpaper:

1. Substituir a neblina empírica por transporte de luz até a distância real da água e das nuvens.
2. Melhorar formação, espessura, auto-sombra e iluminação difusa das nuvens, mantendo inicialmente o cache angular.
3. Acrescentar transmissão espacial das nuvens para sombras sobre o mar.
4. Introduzir bancos baixos de neblina e integração heterogênea somente depois de validar a base. Comparar raymarch em resolução reduzida e froxels, sem assumir vencedor antecipadamente.

A superfície aprovada — espectro, deslocamentos, normais e movimento — permanece referência imutável. Seus reflexos e a visibilidade à distância mudarão porque o ambiente muda. O objetivo é uma imagem convincente e estável, com erros conhecidos e mensuráveis; não uma promessa de transporte radiativo exato em tempo real.

## 2. O que existe e onde estão os limites

As referências de linha abaixo correspondem ao commit base, não a um futuro código modificado.

| Área | Evidência no projeto | Consequência para o plano |
| --- | --- | --- |
| Nuvens 3D | [OceanClouds.hlsl](../Shaders/OceanClouds.hlsl), `Density`/`BuildClouds`, linhas 32–111: camada de 1,2–3,2 km, value noise, erosão, integração exponencial e sombra | Já são volumétricas; falta diversidade de formas e uma iluminação melhor fundamentada |
| Representação | [OceanClouds.cs](../Services/OceanClouds.cs), linhas 9–18: dois mapas angulares RGBA16F, radiância + transmissão | Econômica para câmera fixa; não descreve paralaxe ou densidade em qualquer posição |
| Fog sobre água | [Ocean.hlsl](../Shaders/Ocean.hlsl), linhas 330–332: `1-exp(-distance*.0007)` e mistura com `Environment` | Extinção homogênea isolada é válida, mas a radiância adicionada é uma aproximação, sem integral até a superfície |
| Haze das nuvens | `OceanClouds.hlsl`, linhas 103–111: `exp(-distance*.04)` e alteração de alpha | Outra atenuação empírica independente dos aerossóis; deve ser substituída, não empilhada com novo fog |
| Luz dentro das nuvens | `OceanClouds.hlsl`, linhas 62–101: função direcional com ganhos, duas exponenciais e preenchimento ambiente | Aparência atual não corresponde a uma solução calibrada de espalhamento múltiplo |
| Auto-sombra | Intervalos começam a 0,12 km; alcance de 0,48/1,92/7,68 km em Leve/Equilibrado/Alto | Omite os primeiros 120 m e trunca caminhos longos; especialmente relevante com luz rasante |
| Reflexão/ocultação | `Ocean.hlsl`, `CloudField`, `CelestialTransmission`, `EnvironmentFiltered`, `CycleDirect` | Céu/reflexos compartilham o cache, mas a transmissão direta não varia com a posição do ponto na água |
| Atmosfera | [OceanOptics.hlsl](../Shaders/OceanOptics.hlsl), [OceanCycleSky.hlsl](../Shaders/OceanCycleSky.hlsl), [OceanOpticalDepth.cs](../Services/OceanOpticalDepth.cs) | Há LUT óptica esférica e luz solar/lunar; reutilizar coeficientes e geometria, expandindo o contrato para trajetos finitos |
| Meteorologia | [OceanCelestialModel.cs](../Services/OceanCelestialModel.cs), perto da linha 120: o perfil de aerossóis também escolhe cobertura | Separar cobertura de nuvens, aerossóis e neblina; ar limpo pode ter nuvens e céu sem nuvens pode ter névoa |
| Tempo | `OceanClouds.Update` usa o relógio das ondas para vento; céu tem relógio próprio | Formalizar tempo meteorológico independente, com pausa comum e reconstrução determinística |
| Profundidade | [OceanGpuRenderer.cs](../Services/OceanGpuRenderer.cs), linhas 155–157: D32_FLOAT somente como depth-stencil | Um futuro pós-processamento volumétrico precisa de profundidade amostrável; a primeira etapa pode usar `input.world` no shader da água |

**Diagnóstico quantitativo próprio:** o coeficiente atual de fog é 0,0007 m⁻¹. Isolando apenas a extinção, transmite 93,24% a 100 m, 49,66% a 1 km e 6,08% a 4 km. Isso não demonstra que a imagem esteja errada, mas mostra que já existe um véu forte e fixo sobre a água, independente do seletor de ar. A equivalência homogênea MOR é aproximadamente 4,28 km, sob a convenção da seção 5.

## 3. O que as referências técnicas sustentam

| Fonte primária | Achado relevante | Aplicação proposta neste projeto |
| --- | --- | --- |
| [Guerrilla, Horizon Zero Dawn, 2015](https://www.guerrilla-games.com/read/the-real-time-volumetric-cloudscapes-of-horizon-zero-dawn) | Formação, animação e iluminação precisam ser controladas separadamente | Weather map, perfis verticais e detalhe subordinado à massa principal |
| [Nubis, 2017](https://www.advances.realtimerendering.com/s2017/Nubis%20-%20Authoring%20Realtime%20Volumetric%20Cloudscapes%20with%20the%20Decima%20Engine%20-%20Final%20.pdf) | Modelagem procedural dirigida de paisagens de nuvens | Referência para variação de tipos e cobertura, sem simular meteorologia completa |
| [Nubis³, SIGGRAPH 2023](https://advances.realtimerendering.com/s2023/index.html) | Voxels, aceleração por SDF e técnicas para exploração dentro das nuvens | Evolução futura se houver voo/câmera próxima; complexidade sem benefício demonstrado para a vista atual |
| [Hillaire, projeto de referência EGSR 2020](https://github.com/sebh/UnrealEngineSkyAtmosphere) | LUTs atmosféricas e comparação com path tracing; referência DX11 | Reaproveitar o princípio de radiância/transmissão e usar um integrador de referência para calibração |
| [Hillaire, Frostbite, SIGGRAPH 2015](https://www.advances.realtimerendering.com/s2015/index.html) | Representação consistente de meios participantes e iluminação volumétrica | Contrato comum de extinção/espalhamento, sombras e composição |
| [Wronski, Volumetric Fog, SIGGRAPH 2014](https://www.realtimerendering.com/advances/s2014/wronski/bwronski_volumetric_fog_siggraph2014.pdf) | Volumes alinhados ao frustum, iluminação e integração em etapas | Comparação arquitetural para bancos baixos; não importar timings históricos |
| [Patry/Sucker Punch, SIGGRAPH 2021](https://www.advances.realtimerendering.com/s2021/jpatry_advances2021/index.html) | Cache angular de nuvens com construção distribuída e integração atmosférica | Referência de produção particularmente próxima da câmera estável do oceano |
| [PBRT, transmissão](https://pbr-book.org/4ed/Volume_Scattering/Transmittance) e [equação de transporte](https://pbr-book.org/4ed/Light_Transport_II_Volume_Rendering/The_Equation_of_Transfer) | Atenuação e luz adicionada são parcelas diferentes; o trajeto termina na superfície visível | Fundamentação das equações da seção 4 |
| [NVIDIA Research, STBN, 2022](https://research.nvidia.com/publication/2022-07_spatiotemporal-blue-noise-masks) | Distribuição do erro no espaço/tempo ajuda convergência e filtragem temporal | Candidato posterior para amostragem; não elimina a necessidade de resolver histórico inválido |
| [WMO, classificação das nuvens](https://public.wmo.int/world-meteorological-day-2017/classifying-clouds) | Tipos têm morfologias diferentes | Separar stratus, stratocumulus e cumulus em vez de uma única textura genérica |
| [NOAA/NWS, fog sobre água](https://www.weather.gov/safety/fog-water) | Neblina marítima pode ser advectada quando ar úmido passa sobre água mais fria | Bancos baixos com movimento amplo; não emitir fumaça de cada onda |

As técnicas e custos publicados em outros jogos/hardware não são benchmarks do HYPNIX. As escolhas abaixo são **propostas de engenharia**, sustentadas por essas referências e pela auditoria local. Não foi incorporado código, pacote de ruído, nuvem voxelizada ou asset de terceiros nesta entrega.

## 4. Contrato físico e ordem de composição

### 4.1 Representar o meio, não apenas sua cor

Cada meio fornece extinção `sigma_t = sigma_a + sigma_s`, espalhamento `sigma_s` e função de fase. Densidade e coeficientes nunca negativos. Albedo de espalhamento `omega = sigma_s/sigma_t` entre zero e um. A unidade deve acompanhar cada interface: água em metros; atmosfera atual em quilômetros. Uma conversão explícita na fronteira evita erros de fator mil.

Ao longo de um segmento de comprimento `d`:

```text
T(d) = exp(-integral_0^d sigma_t(s) ds)
S(d) = integral_0^d T(s) * j(s) ds
L_observada = S(d) + T(d) * L_fundo
```

`j` é o termo de luz adicionada por unidade de comprimento, incluindo luz recebida, fase e espalhamento. `S` é radiância já acumulada; **não multiplicar por alpha novamente**. `T` pode ser RGB. A fundamentação é a [equação de transporte do PBRT](https://pbr-book.org/4ed/Light_Transport_II_Volume_Rendering/The_Equation_of_Transfer).

Para um passo com propriedades constantes, integrar a exponencial dentro do passo: `DeltaS = T_acumulada * j * (1-exp(-sigma_t*ds))/sigma_t`; quando `sigma_t` tende a zero, usar o limite `T_acumulada*j*ds`. A avaliação de `j` constante é uma aproximação numérica; a integração da extinção dentro desse passo é analítica. Há implementação pública desse princípio no [raymarch de Hillaire](https://raw.githubusercontent.com/sebh/UnrealEngineSkyAtmosphere/master/Resources/RenderSkyRayMarching.hlsl).

Para segmentos consecutivos, próximo e distante: `S_total = S_proximo + T_proximo*S_distante`, `T_total = T_proximo*T_distante`. Para **meios sobrepostos**, somar coeficientes e integrar a fonte sob a extinção conjunta. Multiplicar transmissões é válido; somar duas radiâncias calculadas independentemente não modela a atenuação recíproca.

### 4.2 Quatro trajetos, com responsabilidades explícitas

| Trajeto | Resultado necessário | Erro a impedir |
| --- | --- | --- |
| Sol/Lua → amostra do volume | Transmissão atmosférica, bloqueio pela Terra, sombra de nuvens/neblina | Fonte continuar iluminando através de uma coluna opaca |
| Volume → câmera | Radiância e transmissão até cada distância | Atenuar toda a atmosfera, inclusive ar à frente da nuvem, pelo alpha da nuvem |
| Ambiente → ponto da água | Radiância incidente e transmissão direta avaliadas na origem correta | Usar o mesmo cache da câmera para um banco de fog local que depende da posição |
| Água → câmera | `S(d)+T(d)*L_agua`, terminando no ponto visível | Aplicar fog atrás da água ou usar profundidade de um plano sem as ondas |

O céu já contém uma integral atmosférica completa. Não aplicar a mesma atmosfera novamente sobre ele. Ao introduzir nuvens, dividir o trajeto em ar à frente, segmentos contendo ar+nuvem, e ar/fundo atrás; usar a composição ordenada acima. O cache de vista pode continuar armazenando o resultado final para consulta barata, mas sua construção precisa respeitar essa ordem. O cache de radiância incidente na água é um consumidor distinto: não reutilizar o resultado final da câmera quando ele já incluir fog local.

O ambiente refletido exclui discos solares/lunares; as fontes extensas continuam no termo direto da BRDF. Bloom, exposição e tone mapping permanecem depois da composição HDR e nunca voltam para a iluminação. Para ar colorido, não reduzir transmissão RGB a um alpha sem medir o erro. A transmissão quase neutra das gotículas pode permanecer escalar quando justificada.

## 5. Fog, névoa marítima e perspectiva aérea

Perspectiva aérea é a mudança de contraste e cor com a distância no ar. Aerossóis e neblina participam desse transporte, mas são controles diferentes: aumentar aerossóis em toda a atmosfera não representa automaticamente uma camada baixa de gotículas.

### 5.1 Etapa inicial: meio estratificado e distância real

Implementar uma camada baixa com densidade dependente da altura e topo suave, somada ao perfil atmosférico existente. Começar com densidade horizontal uniforme. A transmissão de perfis simples pode ser integrada analiticamente; iluminação variável, sombras e curvatura podem exigir quadratura/LUT. Não chamar toda a solução de analítica se apenas a extinção for exata.

No início, consultar um campo de `S/T` por direção e distância no `WaterPS`, que já conhece o ponto mundial. Desligar o lerp antigo ao ativar esse caminho. Para o céu, avaliar até a saída do meio; para as nuvens, parar/particionar nos segmentos corretos. Transições entre o volume próximo e o ar distante devem ter fronteira compartilhada e continuidade de `S/T`, sem buraco nem sobreposição duplicada.

**Curvatura:** raios quase horizontais atravessam caminhos longos. Manter a geometria esférica do ar/camada de nuvens e o modelo local do mar claramente separados. Usar a superfície realmente desenhada como limite da integração sobre água. Não mudar a malha aprovada para uma Terra esférica nesta etapa; medir a compatibilidade na costura distante e registrar qualquer erro residual. Remover o véu forte atual pode expor limites da representação distante; validar a extensão da água e a continuidade do fundo em ar límpido, sem voltar a ocultar defeitos com neblina artificial.

### 5.2 Parâmetro de visibilidade compreensível

Usar MOR como controle técnico de referência, com convenção explícita de transmissão fotométrica de **5%**: em um meio homogêneo `sigma_t = -ln(0,05)/V ≈ 2,995732/V`. `V` e o coeficiente devem usar a mesma unidade. A relação histórica com limiar de contraste de 2% usa 3,912; não misturar as duas definições. Fonte: [WMO-No. 8, edição 2008, capítulo 9, equações 9.4–9.7](https://www.weather.gov/media/epz/mesonet/CWOP-WMO8.pdf#page=214). Aqui a definição histórica é referência matemática, não alegação sobre a edição normativa mais recente.

Tabela **calculada para extinção homogênea**, não fotografia, previsão ou brilho final:

| Distância | Fog atual, 0,0007 m⁻¹ | MOR 20 km | MOR 5 km | MOR 1 km |
| --- | ---: | ---: | ---: | ---: |
| 100 m | 93,24% | 98,51% | 94,18% | 74,11% |
| 1 km | 49,66% | 86,09% | 54,93% | 5,00% |
| 4 km | 6,08% | 54,93% | 9,10% | 0,000625% |

Essa MOR representa a **extinção total na faixa de referência**, não algo a acrescentar cegamente à extinção existente. Ao especificar visibilidade total, descontar a contribuição do ar de base para obter a parcela adicional, limitada a zero. Uma atmosfera com perfis espaciais não tem uma única visibilidade universal; a interface deve identificar altura/direção de calibração. MOR também não prediz diretamente a visibilidade da Lua à noite. A tabela é um modelo neutro de extinção; converter a definição fotométrica para coeficientes RGB exige declarar a aproximação. Dados e fórmulas estão em [visibility.csv](references/ocean/volumetric-study/visibility.csv) e no [registro dos cálculos](references/ocean/volumetric-study/README.md).

Presets candidatos para avaliar, sem tratá-los como medições meteorológicas: horizonte límpido, bruma marítima, névoa baixa, banco de neblina. Separar intensidade, altura/topo, extensão e variação horizontal. A cor deve surgir da iluminação e do transporte; evitar escolher uma cor laranja ou azul de fog independente da hora.

### 5.3 Etapa posterior: bancos heterogêneos

Usar ruído de baixa frequência advectado e máscara de cobertura para criar bancos, com detalhe pequeno moderado. Comparar:

- **Raymarch em baixa resolução:** integração por raio com passos distribuídos pela distância/variação da densidade; composição e reconstrução conscientes da profundidade. Bom candidato para poucos volumes baixos.
- **Froxels:** células 3D alinhadas ao campo de visão, com densidade, luz e integração ao longo dos raios. A distância entre fatias deve favorecer a região próxima. Bom candidato se muitos pixels reutilizarem as mesmas amostras e houver sombra volumétrica variável.

Nem todo efeito precisa dos dois. Fog distante suave pode continuar no caminho estratificado. Froxels da câmera não representam automaticamente o trajeto refletido fora do campo de visão; o campo de densidade no mundo continua sendo a fonte comum. Raios de luz visíveis só devem surgir quando variações reais de sombra e espalhamento os produzirem. A [documentação de volumetric fog da Epic](https://dev.epicgames.com/documentation/en-us/unreal-engine/volumetric-fog-in-unreal-engine) explicita riscos de poucos slices para alcances longos e rastros temporais sob mudanças rápidas de iluminação; usar esses casos como regressões.

## 6. Nuvens: forma antes de microdetalhe

### 6.1 Morfologia e movimento

Separar o mapa meteorológico 2D (cobertura, tipo e espessura) da densidade 3D. Prototipar massas de baixa frequência, combinação Perlin/Worley ou função equivalente própria, perfil vertical por tipo e erosão limitada às bordas. O detalhe deve desaparecer gradualmente com distância/pixel footprint; uma textura de ruído igual em toda a formação parece fumaça ou algodão.

Primeiros alvos visuais:

| Formação | Característica a reproduzir | Uso no oceano |
| --- | --- | --- |
| Stratocumulus | Massas agrupadas, cobertura extensa com aberturas e bases relacionadas | Preset marítimo principal |
| Cumulus de bom tempo | Bases relativamente planas, volumes superiores arredondados, separação entre massas | Inspeção clara de volume e auto-sombra |
| Stratus | Camada mais uniforme, sem o mesmo relevo de cumulus | Céu fechado e ligação com névoa baixa |
| Cirrus, posteriormente | Estruturas altas, finas e direcionais | Segunda camada; não duplicar o mesmo ruído de cumulus |

Tipos inspirados no [Atlas Internacional de Nuvens da WMO](https://cloudatlas.wmo.int/en/clouds-definitions.html); os parâmetros artísticos e a altura exata de cada preset serão ajustados com referências, não deduzidos só do nome.

Definir vento por camada e tempo meteorológico próprio. O multiplicador do dia não deve acelerar o vento 144 vezes. Pausa congela os relógios; alterar velocidade das ondas não deve mudar nuvens. Advecção move a massa; deformação evolui lentamente e sem fazer a formação desaparecer e reaparecer a cada frame. Preservar seed e origem temporal para reconstituir qualquer instante.

### 6.2 Iluminação

1. **Extinção/auto-sombra:** integrar desde a vizinhança da amostra até a saída do volume ou saturação óptica. Substituir o intervalo inicial de 120 m por um offset numérico pequeno e explícito. Para luz rasante, testar passos adaptativos, informação grosseira de densidade e cache de transmissão; não encurtar silenciosamente o caminho em qualidades menores.
2. **Fase normalizada:** iniciar com HG, depois avaliar uma soma ponderada de dois lobos. Fixar a convenção dos vetores e testar o pico voltado à fonte. Hoje os ganhos compensam uma fase sem normalização explícita; a migração exige recalibração, não só inserir `1/(4*pi)` e esperar a mesma imagem. A base teórica é [PBRT — funções de fase](https://pbr-book.org/4ed/Volume_Scattering/Phase_Functions).
3. **Luz difusa:** consultar irradiância aproximada do céu na altura da nuvem e retorno amplo do oceano. Usar uma representação de baixa frequência, sem substituir o ambiente por uma única cor de energia solar.
4. **Múltiplo espalhamento:** comparar uma aproximação limitada por energia com uma referência offline. Separar essa parcela da contribuição direta. Nuvem densa não deve ficar preta por falta de ordens superiores, nem autoiluminada por um preenchimento constante.
5. **Sol/Lua:** mesmas trajetórias, fase lunar e energia já aprovadas. Usar transmissão por amostra; nuvens altas ainda podem estar iluminadas quando o observador está em sombra. Neblina/nuvem opaca pode legitimamente esconder o astro e seu reflexo.

O brilho de borda depende da geometria e iluminação; não desenhar um contorno branco fixo. HG não reproduz arco-íris, glória, corona ou halos de cristais de gelo. Esses fenômenos não fazem parte desta etapa. Não há motivo técnico identificado para acrescentar DLSS ou ray tracing de hardware ao caminho escolhido.

## 7. Sombras no mar e coerência dos reflexos

O cache angular é adequado como aproximação do céu distante, mas não projeta manchas de sombra em posições diferentes da água. Introduzir um mapa de transmissão de nuvens no mundo, amostrado pelo ponto da água para cada iluminante relevante. O mapa representa atenuação da luz, não uma textura escura sobre a cor final: a contribuição ambiente deve permanecer. No termo direto da água, a transmissão espacial **substitui** a consulta angular atual `CelestialTransmission(l)`; multiplicar ambas contaria a mesma camada duas vezes. O disco observado pela câmera mantém a transmissão do seu próprio trajeto.

Começar com mapa 2D ancorado no plano do mar, faixa visível e margem para o movimento do vento. Estabilizar a origem em texels e filtrar de acordo com footprint/tamanho angular da fonte. Mapas separados ou avaliação equivalente para Sol e Lua; seleção por contribuição pode reduzir custo, com transição contínua e considerando a exposição noturna. Luz rasante requer extensão/cascata ou fallback explícito para raios que saiam da área coberta.

Uma sombra ao nível do mar não basta para iluminar fog em diferentes alturas. Para isso será necessário volume de transmissão, camadas em altura ou marcha reduzida até a luz. Não reutilizar uma sombra 2D como se fosse correta em todo o volume.

O reflexo do céu continuará inicialmente no cache angular distante, com limitação registrada. Bancos de fog próximos exigem avaliar o trajeto ambiente→água, por aproximação espacial validada, antes da BRDF e do trajeto água→câmera. Não alimentar o reflexo com a imagem final do céu já vista através da neblina da câmera. Evitar ciclos de dependência entre nuvens e reflexão: retorno difuso do mar pode usar irradiância aproximada derivada do estado ou um número fixo de iterações por snapshot, com energia limitada. Não depender do frame apresentado anteriormente, para preservar busca de instante e reconstrução determinísticas.

## 8. Cache e estabilidade temporal

Manter primeiro o método determinístico atual de dois instantes. Separar decisões de atualização de **densidade**, **iluminação**, **transmissão** e **ambiente refletido**. Uma mudança de hora não exige gerar novamente toda a forma; uma mudança de seed exige.

A cadência atual das nuvens está no tempo das ondas, não é um teto garantido de 1/2 Hz de relógio real. O novo orçamento deve usar tempo real e erro visual: deslocamento projetado do vento, mudança da direção da fonte e variação de transmissão. No ciclo rápido, a iluminação pode mudar muito entre snapshots; medir esse caso antes de fixar frequências.

Se dividir reconstrução em vários frames, publicar somente snapshots completos com identificador de estado. Não misturar céu novo, sombra antiga e nuvem parcialmente atualizada. Reservar limite de trabalho por frame e manter um fallback de menor resolução quando houver busca de horário.

Somente após resolver a composição, testar reprojeção por profundidade representativa/vento e amostragem distribuída no tempo. Há risco de rastros nas bordas, nuvens duplas, perda de detalhe lunar e vazamento de luz nas desoclusões. O histórico precisa ser rejeitado ou reduzido em mudanças de câmera, seed, densidade, qualidade, horário e luz; estimativa de profundidade volumétrica é aproximada, não uma superfície sólida.

Pausa deve manter a imagem estável. Para capturas determinísticas e busca A→B→A, estabelecer um modo de referência sem histórico ou aquecimento fixo reprodutível; não exigir igualdade byte a byte de um acumulador dependente do histórico sem definir esse contrato. Filtragem espacial deve operar nas grandezas adequadas: média de transmissão e radiância representa cobertura subpixel; `exp(-media(tau))` não é igual à média de `exp(-tau)`.

## 9. Níveis de qualidade e orçamento

### Base medida, não estimativa

O [benchmark existente](OCEAN_CELESTIAL_CYCLE_IMPLEMENTATION.md) registra RTX 5070 Ti, 1080p Equilibrado, 30 FPS: GPU p95 de **5,68–6,08 ms**, máximos de frames com cache de **12,21–15,80 ms**, aproximadamente **125,8 MiB** de texturas lógicas. Inclui readback/apresentação GDI em janela oculta; não mede potência atribuível ao efeito, compositor visível ou outra GPU.

Cache atual de nuvens, par com mipmaps: **5,33 / 21,33 / 85,33 MiB** em Leve/Equilibrado/Alto. Não aumentar automaticamente o cache Alto de 4096×1024: primeiro determinar se o erro está na forma, luz, amostragem ou resolução.

### Proposta de orçamento para protótipos

As metas abaixo são **limites de decisão propostos**, não desempenho obtido. Cada nível preserva o mesmo modelo de clima/luz, variando resolução, integração e atualização.

| Nível | Nuvens | Ar/fog | Sombras/reflexos |
| --- | --- | --- | --- |
| Leve | Cache distante atual como ponto de partida; detalhe limitado por distância | Meio estratificado e integração/LUT finita | Transmissão angular; mapa espacial pequeno apenas se couber |
| Equilibrado | Densidade melhorada, sombra adaptativa, cache coerente | Estratificado primeiro; bancos opcionais após medição | Mapa espacial sobre mar; cache distante de reflexão |
| Alto | Mesmo clima com maior precisão; raymarch reduzido/reprojeção somente se superar o cache | Bancos heterogêneos e sombra volumétrica | Maior extensão/precisão espacial; trajetória refletida aproximada e validada |

Meta inicial Equilibrado na máquina de referência: manter ≥28 FPS, p95 de intervalo ≤40 ms; buscar GPU p95 ≤8 ms e p99 ≤16 ms, com picos de reconstrução ≤20 ms. Se um efeito exceder a margem ou elevar consumo sem ganho perceptível, reduzir seu escopo/cadência antes de incorporá-lo ao padrão. Esses limites não permitem inferir consumo elétrico e precisarão de ajuste por GPU.

Exemplo de viabilidade de fog: `160×90×48` contém 691.200 células; um volume RGBA16F ocupa 5,27 MiB. Dois campos RGB (`S` e `T`) com buffers duplos ocupam cerca de 21,09 MiB **antes** de densidade, iluminação, profundidade e metadados. Dobrar cada dimensão multiplica o custo por oito. Comparar esse candidato a um raymarch de meia resolução em cada eixo, isto é, um quarto dos pixels. Não somar ambos por padrão.

Meta provisória de memória para a nova etapa Equilibrado: acréscimo lógico ≤48 MiB, discriminado por recurso. Dimensionar sombras e amostragem após profiling. 4K Alto exige benchmark próprio; aumentar saída não autoriza quadruplicar cada estrutura volumétrica. Qualidade de água não deve ser reduzida silenciosamente para pagar as nuvens.

## 10. Plano de execução com portas de qualidade

| Etapa | Trabalho futuro | Critério para avançar |
| --- | --- | --- |
| V0 — referência e contrato | Congelar baseline, seed, tempos, exposição, câmera e clima; registrar imagens HDR/SDR e termos separados; definir unidades e `S/T` | Mesma água reproduzível e composição auditável por trajeto |
| V1 — perspectiva aérea | Substituir fog fixo na água; integração finita do ar/camada baixa; vácuo e meio homogêneo de referência | Identidade com densidade zero, distâncias corretas e horizonte sem dupla atenuação |
| V2 — nuvens integradas | Separar ar diante/atrás, remover haze empírico, melhorar perfis/massas e auto-sombra; calibrar fase e luz difusa | Volume legível em luz frontal/rasante/contraluz, sem contorno artificial ou fonte vazando |
| V3 — sombras/reflexos | Transmissão espacial no mar, filtragem e sincronização; testar duas fontes | Nuvem, sombra, disco e trilho refletido respondem ao mesmo evento |
| V4 — neblina baixa | Prototipar bancos; comparar raymarch e froxels; sombra volumétrica e tratamento do trajeto refletido | Banco tem espessura/distância, não atravessa a água nem vira uma faixa colada na tela |
| V5 — estabilidade e custo | Atualizações por erro, eventual reprojeção, rejeição de histórico, níveis de qualidade | Sem rastros nas transições e dentro do orçamento; referência determinística preservada |
| V6 — revisão visual | Dia/noite, primeiras horas dos astros, névoa/limpo/nublado; sessões prolongadas | Aprovação visual do usuário e relatório de limitações restantes |

**Primeiro incremento recomendado: V0 + V1.** Ele isola a causa atual da aparência de véu e cria a base para iluminar nuvens e neblina sem contradições. V2 vem antes de novos efeitos de câmera. Cada etapa entrega comparação A/B; implementação só começa em solicitação posterior.

Pontos de trabalho prováveis: `OceanSettings`/estado meteorológico; `OceanOptics`/nova consulta de segmentos; `OceanCycleSky`; `OceanClouds`; composição de `Ocean.hlsl`; recursos e diagnóstico em `OceanGpuRenderer`; controles em `OceanProofWindow`. Um pós-processamento que leia depth exigirá recurso typeless com DSV/SRV compatíveis, ou distância linear separada, além de desvincular leituras/escritas em D3D11. Isso não é requisito para a primeira consulta feita diretamente no shader da água.

## 11. Como provar que ficou melhor

### Testes numéricos e de integração

- Vácuo: `T=1`, `S=0`; identidade em render sem novo meio, tolerância FP32 registrada.
- Meio homogêneo: comparar com solução analítica para profundidade óptica 0, 0,01, 0,1, 1, 5 e 20; meta inicial erro absoluto de `T` ≤0,001. Radiância com fonte constante: erro relativo ≤1% quando o sinal for mensurável, usando tolerância absoluta perto de zero.
- Composição: dois segmentos equivalentes a um; meios sobrepostos comparados à integração conjunta; caminho termina na superfície, transmissão em [0,1], sem NaN/Inf ou energia negativa.
- Fase: integral sobre esfera aproximadamente um, reciprocidade/convenção angular verificadas. Caso sem absorção não justifica energia infinita em aproximação de múltiplo espalhamento.
- Convergência: dobrar passos e resolução contra uma referência de maior qualidade. Separar erro de discretização do erro do modelo; mais amostras não validam a física aproximada.
- Preservação: hashes espectrais/normais iguais no mesmo instante; o novo ambiente pode alterar os pixels finais da água. Densidade zero do novo caminho comparada ao baseline atmosférico definido, não à antiga fog fixa por acidente.
- Recursos: resize, recriação de GPU, troca de qualidade, pausa/retoma, duas saídas, dispositivo perdido e ausência de leituras SRV durante escrita incompatível.

### Matriz visual mínima

| Caso | O que procurar |
| --- | --- |
| Sol a −6°, −1°, +1°, +5°, +15° e +45° | Crepúsculo progressivo, bases/topos com iluminação plausível, passagem no horizonte sem salto |
| Lua cheia, gibosa e crescente a +1°, +5° e +15° | Fase/energia coerentes; crateras quando a transmissão permite; neblina sem azul emissivo fixo |
| Sol e Lua simultaneamente relevantes | Sombras/transmissões não assumem uma única fonte em todos os horários |
| Stratocumulus, cumulus e stratus | Formas distintas, escala crível, erosão secundária, ausência de repetição evidente |
| Limpo, bruma e banco baixo | Água próxima legível no caso leve, perda gradual à distância; forte neblina pode esconder o horizonte |
| Nuvem atravessando o astro | Disco, espalhamento, sombra e trilho refletido sincronizados |
| Nascer/pôr em ciclo 1× e 144× | Sem pulsação, duplicação ou luz antiga no cache; fontes fora do quadro ainda iluminam |
| 1080p, retrato, ultrawide e 4K | Sem costura angular, anéis, degraus de fatias ou artefatos nas bordas da água |

Para cada alvo, guardar A/B com a mesma câmera/água/tempo, HDR linear antes de bloom, exposição usada, seed, parâmetros, versão e GPU. Comparar primeiro com exposição travada; depois com a adaptação fotográfica aprovada. Uma imagem bonita com exposição diferente não prova melhoria no transporte.

Vídeos de pelo menos 60 s nas cenas críticas, inspeção em velocidade normal e quadro a quadro. Medir erro temporal de bordas/luminância contra referência, distinguir movimento real do ruído. Nenhuma tolerância única de PSNR/SSIM substitui a revisão visual do horizonte e dos astros.

### Desempenho e consumo

Repetir o protocolo existente de 30 s de aquecimento + 3×120 s, incluindo todos os frames de cache, readback e apresentação. Reportar GPU por passe, p50/p95/p99/máximo, intervalo de frames, working set e bytes de cada textura. Medir separadamente inicialização, busca de horário e troca de qualidade.

Acrescentar sessão visível prolongada, com mesma resolução/refresh e outros aplicativos controlados; comparar placa inteira em repouso e com o efeito, temperatura e potência como observações do sistema, sem atribuir toda a diferença a um único passe. A captura de GPU 8%/CPU 1% do usuário continua referência de satisfação, não orçamento mensurado universal.

## 12. Fontes, proveniência e limites

Fontes web consultadas em 28/09/2026. Documentos antigos foram usados por descreverem métodos e implementações; não são alegações sobre a versão mais recente de um motor. O PDF completo de Hillaire 2020 excedeu o limite de leitura direta da ferramenta; foram consultados o projeto do autor e o shader público, sem afirmar leitura integral desse PDF.

- Física: [PBRT — transmissão](https://pbr-book.org/4ed/Volume_Scattering/Transmittance), [fase](https://pbr-book.org/4ed/Volume_Scattering/Phase_Functions), [transporte](https://pbr-book.org/4ed/Light_Transport_II_Volume_Rendering/The_Equation_of_Transfer).
- Atmosfera: [Hillaire, código de referência](https://github.com/sebh/UnrealEngineSkyAtmosphere), [shader de integração](https://raw.githubusercontent.com/sebh/UnrealEngineSkyAtmosphere/master/Resources/RenderSkyRayMarching.hlsl), [licença publicada](https://raw.githubusercontent.com/sebh/UnrealEngineSkyAtmosphere/master/LICENSE). A referência está sob MIT; incorporar código futuramente exige preservar avisos e verificar os arquivos utilizados.
- Nuvens: [Guerrilla 2015](https://www.guerrilla-games.com/read/the-real-time-volumetric-cloudscapes-of-horizon-zero-dawn), [Nubis 2017](https://www.advances.realtimerendering.com/s2017/Nubis%20-%20Authoring%20Realtime%20Volumetric%20Cloudscapes%20with%20the%20Decima%20Engine%20-%20Final%20.pdf), [Nubis Evolved](https://www.guerrilla-games.com/read/nubis-evolved), [Nubis³, material dos autores](https://advances.realtimerendering.com/s2023/index.html).
- Volumetria unificada: [Frostbite/SIGGRAPH 2015](https://www.advances.realtimerendering.com/s2015/index.html).
- Fog em produção: [Wronski/SIGGRAPH 2014](https://www.realtimerendering.com/advances/s2014/wronski/bwronski_volumetric_fog_siggraph2014.pdf), [Epic — limitações e qualidade](https://dev.epicgames.com/documentation/en-us/unreal-engine/volumetric-fog-in-unreal-engine).
- Cache e atmosfera: [Patry/Sucker Punch — Real-Time Samurai Cinema, 2021](https://www.advances.realtimerendering.com/s2021/jpatry_advances2021/index.html), seção Atmospheric Lighting/Clouds. A referência usa mapa paraboloide e trabalho distribuído; a projeção e a cadência não precisam ser copiadas para HYPNIX.
- Amostragem: [STBN, publicação dos autores](https://research.nvidia.com/publication/2022-07_spatiotemporal-blue-noise-masks). O método é referência; licença de pacote/código/assets precisa de verificação específica antes de redistribuição. Preferir geração própria ou dependência permissiva confirmada.
- Meteorologia visual: [WMO, definições de nuvens](https://cloudatlas.wmo.int/en/clouds-definitions.html), [classificação](https://public.wmo.int/world-meteorological-day-2017/classifying-clouds), [NOAA/NWS, fog marítima](https://www.weather.gov/safety/fog-water).
- Visibilidade: [WMO-No. 8, 2008, capítulo 9, cópia hospedada pelo NOAA/NWS](https://www.weather.gov/media/epz/mesonet/CWOP-WMO8.pdf#page=214), definição MOR e equações 9.4–9.7; condições de contraste e diferenças para observação noturna nas mesmas páginas.

Os números de transmissão e memória apresentados são cálculos próprios reproduzíveis pelas fórmulas do texto. Novas frequências, resoluções e limites de tempo são hipóteses para prototipagem. Referências meteorológicas ajudam a escolher aparência plausível, mas o wallpaper não prevê umidade, condensação ou clima real. Fotografias dos anexos e de atlas não se tornam assets distribuíveis automaticamente.

Fora desta etapa: voar através de nuvens voxelizadas, simulação de fluidos meteorológica, arco-íris/halos/coronas físicos, chuva, relâmpagos, simulação de spray, transporte espectral completo e path tracing no wallpaper. Uma referência offline mais completa pode auxiliar a validação sem se tornar o renderizador em tempo real.
