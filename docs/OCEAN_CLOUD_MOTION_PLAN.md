# Plano — movimento e evolução apenas das nuvens

28/09/2026 · Base técnica: `762a45a` · Branch: `codex/ocean-and-day-cycle`.

**Status: plano original, seguido pela [implementação documentada](OCEAN_CLOUD_MOTION_IMPLEMENTATION.md).** A implementação registra as escolhas finais, diferenças em relação à candidata e limites da validação; os números exploratórios abaixo continuam sendo metas do plano.

## 1. Contrato de escopo para a implementação

> Implementar somente o movimento e a evolução da densidade das nuvens volumétricas. Preservar os controles existentes de tipo, porcentagem e vento. Não alterar ondas, malha, normais, material/reflexão da água, astronomia, Sol, Lua, mapa lunar, atmosfera base, neblina, exposição, bloom ou câmera. As sombras, oclusões e reflexos das nuvens devem acompanhar seu movimento através dos caminhos ópticos já existentes, sem modificar esses modelos.

O escopo abrange o campo `VCloud`, suas coordenadas e seu estado de animação. São permitidos ajustes mínimos na passagem desse estado e na chave dos caches, para que todos os consumidores vejam a mesma nuvem no mesmo instante. **Não aplicar a nova deformação a `VFogDensity` nem à função compartilhada `VCoordinates` sem antes separar o caminho das nuvens.**

“Apenas as nuvens” preserva os modelos dos outros componentes, mas não congela seus pixels: uma nuvem atravessando o Sol pode reduzir a luz na água e seu reflexo. Isso é consequência esperada da nuvem; mudar a BRDF do mar ou a trajetória solar está fora do escopo.

Preservar também o caminho anterior `Weather=null`. Nenhum novo botão, slider, tipo de nuvem, camada meteorológica ou dependência externa é necessário. Não implementar física de vapor/temperatura, chuva, tempestades ou gelo nesta etapa.

## 2. O que existe hoje

Auditoria do código na base indicada:

| Componente | Situação atual | Consequência para o plano |
| --- | --- | --- |
| `OceanWeatherSettings` | Estratocúmulos, cúmulos e estratos; cobertura; vento; seed | Manter contrato e valores públicos |
| `VCloud` | Ruído 3D com massa, perfil vertical e erosão espacial | Reutilizar as formas e acrescentar evolução temporal |
| `VCoordinates` | Deslocamento uniforme compartilhado por nuvens e bancos de neblina | Criar coordenadas exclusivas para nuvens |
| `BuildPass` | Offset calculado como tempo × velocidade atual | Trocar vento pode mudar a posição imediatamente; corrigir somente para nuvens |
| Vento | Mesmo vetor em todas as alturas | Acrescentar perfil vertical suave |
| `weatherClock` | Tempo próprio a 1×, separado de ondas e astronomia | Reutilizar; não ligar movimento ao ciclo do dia |
| Cache | Dois estados apresentados e um terceiro preparado em quatro passes | Manter arquitetura e sincronizar o estado de movimento |

Hoje a distorção espacial se move junto com o volume: a nuvem é transportada, mas não possui crescimento, dissipação ou erosão que evoluam independentemente. A aparência de movimento pode ser discreta pela escala e distância, sem que o relógio esteja parado.

O vetor de offset atual é proporcional a `(1, 1/3)` e é somado à posição de consulta. Por isso, o movimento visual ocorre no sentido oposto, e seu módulo é aproximadamente 1,054 vezes o rótulo em m/s. No novo caminho de nuvens, explicitar a convenção `posição de consulta = posição no mundo − deslocamento`, normalizar a direção e usar o valor selecionado como velocidade de referência. Preservar a direção visual predominante atual: `(-3,-1)/sqrt(10)` no plano XZ. Não corrigir incidentalmente o vento da neblina.

## 3. Base física e objetivo visual

Nuvens são regiões de condensação, mistura e evaporação, não objetos sólidos simplesmente carregados pelo vento. A entrada de ar mais seco pode erodir suas bordas; correntes ascendentes favorecem crescimento dos cúmulos. Os estratos têm evolução vertical mais discreta. Isso orienta a animação, sem transformar o wallpaper em um modelo meteorológico. [NOAA/NWS — Clouds and Contrails](https://www.weather.gov/fgz/cloudscontrails).

Sublimação é a passagem direta de sólido para vapor. Para o comportamento visual dos três tipos atuais, a referência principal será condensação/evaporação e mistura turbulenta; não assumir gelo apenas por altitude nem chamar toda dissipação de sublimação. [WMO — Glossary](https://cloudatlas.wmo.int/en/glossary.html).

O vento pode mudar de velocidade **e direção** com a altura. “Mais alto sempre mais rápido” não é uma lei universal: o perfil depende da situação atmosférica. Usaremos perfis artísticos moderados, com topo inicialmente mais rápido, explicitamente separados de dados meteorológicos medidos. Não extrapolar uma lei de atrito superficial até 3 km. [NOAA/NWS — Wind Shear](https://www.weather.gov/ggw/GlossaryW).

O resultado desejado tem quatro componentes simultâneos:

1. Deslocamento coerente das massas pelo céu.
2. Topos e bases com pequena diferença de movimento, produzindo inclinação e alongamento.
3. Bordas que se formam, se desfazem e se enrolam gradualmente, mantendo uma identidade reconhecível por algum tempo.
4. Regiões que ganham ou perdem densidade em ritmos locais, sem o céu inteiro “respirar” junto.

A arquitetura será procedural e determinística, aproveitando o renderer atual. A separação entre modelagem, animação e iluminação é compatível com o trabalho apresentado pelos autores do Nubis; os parâmetros e a solução abaixo são propostas próprias, não uma reprodução do algoritmo ou dos tempos de GPU deles. [Guerrilla — Real-Time Volumetric Cloudscapes](https://www.guerrilla-games.com/read/the-real-time-volumetric-cloudscapes-of-horizon-zero-dawn).

## 4. Controles preservados

| Controle existente | Comportamento planejado |
| --- | --- |
| Estratocúmulos / Cúmulos / Estratos | Seleciona perfil de forma, cisalhamento e ritmo de evolução; não muda sozinho de tipo |
| Sem nuvens / 25% / 48% / 75% / 95% | Continua controlando o campo de cobertura; 0 exige densidade de nuvem exatamente zero |
| Vento parado / 9 m/s / 18 m/s | Define transporte horizontal de referência e parte da deformação induzida pelo vento |
| Pausar | Congela transporte e evolução; retomar não deve provocar salto |
| Qualidade | Muda custo e filtragem, sem mudar o tempo, a seed ou a trajetória das massas |
| Ciclo do céu / Avançar 1 h / Ver Sol / Ver Lua | Altera iluminação e astronomia; não adianta a animação das nuvens |

A porcentagem já é um parâmetro do gerador, não a porcentagem exata de pixels cobertos na câmera. Manter essa semântica e o tooltip existente. A nuvem pode se desfazer localmente enquanto outras partes ganham densidade, mas a evolução não pode transformar sistematicamente 25% em um céu fechado. Não usar normalização por quadro para obrigar uma contagem exata de pixels: isso faria nuvens não relacionadas pulsarem juntas.

Com **vento parado**, a translação e o cisalhamento induzido pelo vento cessam. Uma evolução intrínseca lenta continua, pois ar sem transporte médio não implica ausência de condensação/evaporação. O botão **Pausar** continua sendo o congelamento completo. Essa distinção deve constar na documentação/tooltip, sem acrescentar um controle.

## 5. Modelo de animação recomendado

### 5.1 Transporte e estado persistente

Criar um estado de movimento exclusivo das nuvens, pertencente à prévia/saída lógica e sobrevivendo à recriação do renderer. O relógio meteorológico continua sendo a fonte do tempo.

O deslocamento deve ser a integral da velocidade, e não `tempo total × velocidade escolhida agora`:

```text
A(t) = A(t0) + integral(v(s), s=t0..t)
q = posição_mundo - A(t)
```

Ao trocar 9 → 18 m/s, conservar a posição no instante da alteração e mudar apenas a velocidade futura. Usar uma rampa curta de 2–4 s como ponto de partida. Integrar essa rampa analiticamente em double na CPU, sem depender do número de frames. A seed não muda quando se altera vento, porcentagem, qualidade ou horário.

O estado precisa de uma trajetória avaliável em qualquer instante de snapshot: histórico de segmentos de velocidade, ou checkpoints com os segmentos necessários. Uma única posição acumulada mutável não basta, pois os caches consultam passado e futuro. Para reproduzir uma sessão com alterações de vento, guardar também a sequência de eventos; apenas `(tempo, vento final)` não identifica a mesma trajetória. Uma sessão nova com os mesmos parâmetros fixos deve ser determinística.

Mudanças de tipo/cobertura são eventos explícitos do usuário: preservar o offset e a fase temporal; reconstruir estados completos. Não prometer continuidade da silhueta ao trocar de espécie nem acrescentar uma transição global de dois céus nesta etapa. Mudança de seed, quando feita por testes, é uma nova formação intencional.

### 5.2 Vento por altitude

Usar a altura normalizada `h=(altura−base)/(topo−base)`, com interpolação suave do vetor de vento. O controle existente representa a velocidade na altura de referência central. Os valores abaixo são **hipóteses iniciais de calibração visual**, não velocidades típicas verificadas de cada gênero:

| Tipo | Base/topo existentes | Velocidade base → referência → topo | Diferença inicial de direção base/topo |
| --- | --- | --- | --- |
| Estratos | 0,65–1,25 km | 0,9× → 1× → 1,1× | 0–4° |
| Estratocúmulos | 1,0–2,1 km | 0,85× → 1× → 1,15× | 3–8° |
| Cúmulos | 1,1–3,1 km | 0,8× → 1× → 1,2× | 5–12° |

Exemplo de projeto: com 9 m/s, o perfil de cúmulos parte de 7,2 m/s na base e 10,8 m/s no topo. Isso representa um campo local simplificado, não uma medição meteorológica. A direção gira suavemente, nunca em degraus por fatia. Não criar várias camadas independentes: deformar a camada atualmente selecionada.

Essas velocidades são referências de transporte antes da renovação local. A combinação procedural de idades não resolve exatamente a equação de advecção: durante a renovação, exigir continuidade, direção predominante e diferença de movimento entre alturas, sem prometer que cada detalhe conserva instantaneamente o módulo exato da tabela. Velocidade aparente também depende da distância; não aplicar compensação em coordenadas de tela para fazer nuvens altas parecerem mais rápidas.

**Evitar cisalhamento ilimitado.** Transportar para sempre cada altitude com sua própria velocidade acabaria quebrando a continuidade vertical da formação. Usar transporte médio permanente e uma idade local limitada para a deformação diferencial. A renovação deve ser assíncrona por região material, com janelas suaves sobrepostas e fases estáveis por seed, preservando a envoltória de grandes massas.

A candidata para o primeiro protótipo é uma avaliação de duas idades locais sobrepostas somente na estrutura/erosão, com pesos cuja soma é um e transições com derivada contínua. Não fazer crossfade global de duas imagens nem trocar toda a seed no reinício da janela. Misturar campos pode alterar sua distribuição: a calibração estatística de cobertura e contraste é um requisito antes de aceitar essa candidata. Se produzir dissolução dupla, pulsações ou perda de massa, revisar a renovação local antes de avançar; não mascarar isso com bloom ou neblina.

Integrar também o histórico do vento diferencial dentro de cada idade local; `idade × diferença de vento atual` reintroduziria um salto ao trocar a velocidade. As janelas/fases devem variar suavemente tanto no tempo quanto no espaço. Um hash discreto de região pode selecionar parâmetros, mas não pode causar uma descontinuidade de idade na fronteira entre regiões.

### 5.3 Evolução das formas em três escalas

- **Envoltória:** mapa meteorológico de baixa frequência, com deslocamento médio coerente. Conserva o agrupamento e a distribuição geral de cobertura; muda mais lentamente que as bordas.
- **Estrutura:** deformação espacial limitada e correlacionada dentro da massa. Cúmulos privilegiam desenvolvimento aparente para cima; estratos permanecem achatados; estratocúmulos preservam massas baixas agrupadas. Manter as alturas e os perfis existentes.
- **Bordas:** campo de erosão com fase temporal própria e velocidade relativa pequena. Variar erosão e limiar de densidade localmente, sobretudo na periferia, sem esvaziar o núcleo a cada ciclo.

Os campos serão funções coerentes de coordenadas materiais, tempo e seed, nunca ruído aleatório novo por frame. Acrescentar detalhe temporal não deve exigir textura 3D reconstruída a cada quadro. Primeiro reutilizar o volume de ruído existente com consultas e fases próprias das nuvens; qualquer recurso adicional depende de medição.

O campo final deve manter densidade não negativa, teto de extinção e continuidade nas bases/topos. Crescimento e dissipação são aproximações visuais limitadas; não afirmar conservação de água, simulação termodinâmica ou cálculo real de sublimação.

Pontos de partida para a direção artística, a validar em movimento:

| Tipo | Mudança perceptível nas bordas | Renovação mais ampla da estrutura | Intensidade relativa |
| --- | --- | --- | --- |
| Estratos | 2–5 min | 20–40 min | Discreta, horizontal |
| Estratocúmulos | 1–3 min | 10–25 min | Moderada, bordas e agrupamentos |
| Cúmulos | 30–90 s | 5–15 min | Mais ativa nos lobos superiores |

Esses intervalos são tempos de percepção pretendidos no wallpaper, não tempos meteorológicos universais ou loops obrigatórios. O primeiro resultado deve mostrar deslocamento ao comparar 5–15 s e mudança de forma ao comparar 30–120 s, quando distância, vento e enquadramento permitirem. Não acelerar automaticamente com “Dia em 1 hora”. A proximidade angular ao horizonte pode tornar o movimento menos aparente.

### 5.4 Precisão e periodicidade

Manter integração temporal e offsets em double na CPU. Converter para float apenas offsets/fases locais pequenos. O módulo de 1.600 km usado hoje foi escolhido para as frequências existentes; novos fatores, cisalhamento e warps precisam de uma nova prova de continuidade.

Reduzir coordenadas pelo período de cada campo em seu próprio espaço, conservando identificadores inteiros estáveis para regiões materiais. Não aplicar `%1600` indiscriminadamente a um campo novo. Testar antes/depois das emendas espaciais, das trocas de idade e em tempos longos. A câmera parada não pode observar repetição curta, teleportes ou textura deslizando para trás ao reiniciar uma fase.

## 6. Integração sem ampliar o escopo

| Arquivo | Mudança futura permitida |
| --- | --- |
| `Services/OceanWeatherSettings.cs` | Perfis internos de movimento por tipo, preservando controles, valores e `FogProfile` |
| Novo `Services/OceanCloudMotion.cs` | Estado, trajetórias, eventos e snapshots determinísticos exclusivos das nuvens |
| `Tests/Hypnix.NativeSmoke/OceanProofWindow.cs` | Manter o estado fora do renderer e encaminhar eventos existentes de vento/pausa; sem novos controles |
| `Services/OceanGpuRenderer.cs` | Encaminhar snapshot de movimento; não tocar na água ou no modelo dos astros |
| `Services/OceanVolumetrics.cs` | Constantes próprias de nuvens e chave de revisão da trajetória; conservar preparação distribuída |
| `Shaders/OceanVolumeCommon.hlsl` | Separar coordenadas de nuvens e alterar somente `VCloud`/helpers exclusivos |
| `Shaders/OceanVolume.hlsl` | Somente diagnóstico de densidade, se necessário; preservar os integradores de transporte |
| Testes unitários e nativos | Adicionar verificações temporais e de isolamento |

Os quatro passes — transmissão de luz, céu, perspectiva aérea e probes — devem receber o mesmo estado imutável de movimento para o mesmo tick. Se houver alteração de vento, invalidar todos os snapshots futuros afetados pela revisão da trajetória, inclusive os já completos e o endpoint futuro usado na interpolação. Nenhuma combinação de sombra antiga com nuvem nova.

A substituição desse endpoint pode causar salto na imagem mesmo com deslocamento analítico contínuo. N1 deve validar a continuidade do resultado interpolado e definir uma ancoragem/transição consistente dos estados no instante do evento; simplesmente limpar o terceiro cache não resolve o caso. Não apresentar um estado parcialmente reconstruído nem reiniciar o tempo para esconder a transição.

Não mudar inicialmente as resoluções, passos de ray marching, frequência do cache ou fases de espalhamento. Preservar os intervalos atuais: 1 s no Leve, 0,5 s no Equilibrado/Alto. A interpolação atual pode mostrar imagem dupla se detalhes se moverem demais entre estados; medir esse erro. Primeiro limitar frequências/amplitudes de detalhes não resolvidos. Só considerar maior frequência de atualização se o orçamento permitir, mantendo sincronizados os quatro passes; não acrescentar reprojeção temporal global nesta etapa.

Nuvens zeradas devem produzir o mesmo ambiente sem nuvens da base, nos mesmos tempos e parâmetros, dentro da tolerância numérica definida. Campos de neblina devem permanecer iguais inclusive com nuvens presentes; sua iluminação pode variar pela sombra das nuvens. Não usar igualdade da imagem final como prova de neblina inalterada quando há oclusões diferentes.

## 7. Etapas e critérios de saída

| Etapa | Entrega | Critério para seguir |
| --- | --- | --- |
| N0 — Baseline | Densidade isolada, capturas e filme do renderer atual | Hashes da água e amostras da neblina registrados; comparação reproduzível |
| N1 — Separação e transporte | Coordenadas exclusivas e deslocamento integrado | Trocar vento não teleporta; pausa/recriação preservam posição; direção/módulo conferidos |
| N2 — Perfil vertical | Velocidade por altura e renovação local limitada | Topo/base diferem sem costuras, estiramento crescente ou reinício visível |
| N3 — Forma viva | Crescimento, dissipação e erosão coerentes por tipo | Massa reconhecível, bordas evoluem; sem ruído cintilante, gelatina ou pulsação coletiva |
| N4 — Coerência óptica | Um snapshot para todos os consumidores | Nuvem cruza Sol/Lua com oclusão, transmissão e reflexo coerentes |
| N5 — Ajuste e validação | Filmes, medições e documentação final | Critérios abaixo atendidos; revisão visual antes de ampliar a entrega |

Não implementar todas as camadas de movimento de uma vez: comparar cada etapa com a anterior com iluminação, câmera, água e seed congeladas. Após aprovar a densidade isolada, conferir o resultado iluminado.

## 8. Testes e orçamento

### Testes de comportamento e isolamento

1. **Transporte conhecido:** com deformação e shear desligados em diagnóstico, acompanhar um marcador do campo sob velocidade constante. Conferir direção e deslocamento `v×tempo` por comparação numérica, não apenas por diferença entre duas imagens.
2. **Troca de vento:** sequências 0→9→18→0 m/s, incluindo mudança durante preparação de cache. Posição contínua no evento; rampa com integral conhecida; nenhum reset de fase.
3. **Altura:** amostrar base/meio/topo em diagnóstico antes da renovação local; comparar com perfil esperado. Verificar continuidade de densidade e velocidade nas transições de altura.
4. **Evolução independente:** após compensar o deslocamento médio, a estrutura deve mudar gradualmente. Capturar nuvens sem Sol/Lua em movimento para não confundir sombra com deformação.
5. **Controles:** três tipos × cinco coberturas × três ventos. Cobertura zero sempre exata; tipo e porcentagem nunca mudam sozinhos. Com vento zero, deriva média zero e evolução interna lenta; com pausa, ambas zeradas.
6. **Cobertura estatística:** medir ocupação em área de mundo ampla e conjunto fixo de seeds, compensando a advecção. Comparar médias em janelas de 10 min. Meta inicial: desvio ≤3 pontos percentuais contra a envoltória calibrada, sem exigir a porcentagem nominal de pixels na tela. Reportar dispersão e revisar a meta com N0; não corrigir por ganho global por frame.
7. **Tempo:** pausa/retoma, resize, qualidade, recriação e busca meteorológica A→B→A devem reproduzir o estado para a mesma trajetória. Saltar a hora celeste não muda densidade das nuvens nem relógio do vento.
8. **Emendas:** instantes antes/depois do wrap espacial e da renovação local; sessões de 1 h e tempos sintéticos de 24 h/7 dias. Sem salto maior que o avanço normal entre instantes vizinhos.
9. **Preservação:** hashes dos espectros/deslocamentos/derivadas da água iguais; neblina amostrada igual; resultados astronômicos e LUT da reflexão iguais. Reexecutar regressão de transporte e matriz solar/lunar da correção anterior.
10. **Cache:** imagens de preparação incremental e reconstrução direta coincidem no mesmo instante/revisão; transmittância em [0,1], radiância finita e não negativa.

### Revisão visual

Filmes de pelo menos 120 s para cada tipo em 25% e 75%, com 9/18 m/s; amostras de vento parado, 48%, 95% e zero. Sol e Lua parcialmente ocultados; horizonte, zênite visível, céu claro e bruma atual. Rever em velocidade normal e quadro a quadro, com HDR/exposição registrados. Sequências longas aceleradas servem apenas para inspecionar repetição, não para julgar a velocidade normal.

Rejeitar: contornos duplicados, camadas deslizando como lâminas, ruído fervendo, troca de formação completa em bloco, nuvens inchando todas juntas, sombras atrasadas, erosão que altera drasticamente a cobertura ou deslocamento que parece textura colada ao céu.

### Custo

A correção anterior mediu 29,7 FPS e GPU p95 de 7,25 ms em uma sondagem curta na RTX 5070 Ti; isso não substitui um baseline longo lado a lado para esta mudança. A implementação volumétrica anterior teve p95 de 8,23–8,69 ms no ensaio longo. Não prometer custo nulo.

Reusar texturas e passes existentes. Orçamento inicial proposto no Equilibrado: acréscimo de GPU p95 ≤15% **e** ≤1,5 ms contra baseline medido nas mesmas condições; manter ≥28 FPS e intervalo p95 ≤40 ms no alvo de 30 FPS. Reportar p99 e picos separadamente, incluindo atualizações completas. Metas são critérios de engenharia a validar, não desempenho já alcançado.

Protocolo: 30 s de aquecimento + 3×120 s, mesmas cenas/seeds, todos os passes incluídos. Verificar Leve/Alto, ultrawide, retrato e saída 4K. Se o custo exceder o orçamento, reduzir primeiro consultas extras de detalhe e complexidade da renovação local; preservar a água, o transporte principal e a coerência temporal. Documentar custo de primeira abertura, troca de parâmetros e memória adicional. CPU deve atualizar poucos parâmetros/eventos, sem sintetizar voxels por frame.

## 9. Entrega pretendida

O próximo desenvolvimento deve produzir uma animação na qual a nuvem avança, mantém sua identidade por algum tempo e muda de forma aos poucos, com diferentes movimentos ao longo da altura. Os mesmos seletores continuam sendo a interface. A entrega inclui teste automatizado, comparação em vídeo e registro dos parâmetros aprovados.

**Instrução final de aplicação: alterar apenas o sistema de nuvens descrito neste documento. Não reabrir o refinamento da água, dos reflexos, do Sol, da Lua ou da neblina.**

Referências locais: [ambiente volumétrico](OCEAN_VOLUMETRIC_ENVIRONMENT_IMPLEMENTATION.md), [estudo de transporte](OCEAN_VOLUMETRIC_ENVIRONMENT_STUDY.md), [reflexos preservados](OCEAN_REFLECTION_TRACKING_FIX.md). Fontes web consultadas em 28/09/2026; usadas como fundamentos e referências de arquitetura, sem copiar assets ou prometer equivalência com motores de terceiros.
