# Oceano 3D — pesquisa e decisões técnicas

Pesquisa: 2026-09-27, revista após as seis referências visuais. Documentos associados: [plano de implementação — revisão B](OCEAN_WALLPAPER_PLAN.md) e [revisão técnica](OCEAN_TECHNICAL_REVIEW.md).

Atualização posterior à pesquisa: [P1 implementada](OCEAN_P1_IMPLEMENTATION.md) e [água espectral P2 aprovada visualmente pelo utilizador, com testes matemáticos](OCEAN_P2_IMPLEMENTATION.md). A próxima etapa é [cores, sol, lua e céu, preservando a água](OCEAN_P3_LIGHTING_PLAN.md). Os resultados das provas são separados das hipóteses históricas abaixo; a aprovação do conjunto final e a validação ampliada continuam pendentes.

Atualização após P3: cores e consumo receberam feedback positivo; sol/lua e nuvens motivaram o [plano de realismo do céu](OCEAN_SKY_REALISM_PLAN.md), com nova pesquisa primária e diagnóstico da implementação. Essa revisão não altera o renderer.

Objetivo: escolher um caminho para água convincente no HYPNIX, equilibrando aparência, consumo e custo de integração. As recomendações são inferências de engenharia apoiadas nas fontes e no código local; não descrevem tecnologia confirmada da Optimum nem benchmarks já realizados.

## 1. Conclusões utilizáveis

1. O HYPNIX já tem D3D11, HLSL, sessões nativas, pausa e prévias. A integração mais direta é um renderer C# dedicado nesse sistema.
2. A aparência depende simultaneamente da distribuição de ondas, normais, iluminação refletida e estabilidade temporal. Aumentar a resolução de uma imagem não resolve esses componentes.
3. Comparar cedo Gerstner e espectro direcional multiescala sintetizado por IFFT. O segundo é candidato principal para Equilibrado/Alto; a técnica isolada não garante qualidade. A formulação anterior “FFT só depois” foi substituída.
4. Separar céu distribuído e contribuição do sol/lua de tamanho angular finito, sem dupla contagem. Filtragem especular e transferência de detalhe até ao material fazem parte da base, não de um polimento posterior.
5. Uma simulação deve parar quando o wallpaper está oculto. Orçamentos por viewport precisam de incluir as sessões/prévias simultâneas.
6. Água física completa, com rebentação e salpicos, constitui outro nível de complexidade. O alvo inicial será mar aberto sem interação com objetos.
7. DLSS 5 não é requisito. Uma eventual reconstrução de resolução deve ser avaliada separadamente e não substituir os contratos de luz, ondas e estabilidade.

## 2. Fontes primárias consultadas

As datas de consulta são iguais à data deste documento. Artigos antigos são usados para fundamentos matemáticos; não como evidência de compatibilidade ou desempenho de hardware atual.

### R1 — Referência comercial

[Optimum — Chaos Pack](https://optimum.store/products/chaos-pack).

A página afirma resolução de renderização 7680×4320, pretos para OLED, granulação e prévias de baixa resolução. Não revela software, pipeline ou animação. Serve para definir a direção artística e distinguir imagem pré-renderizada de uma cena calculada por frame. Não foram obtidos os assets pagos.

### R2 — Ondas Gerstner e detalhe

[NVIDIA GPU Gems, capítulo 1 — Effective Water Simulation from Physical Models](https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models).

Explica modelos de ondas, deslocamento de superfície e detalhe de normais. Apoia a separação de escalas e o uso de Gerstner como base. É referência conceitual para uma implementação própria; nenhum código do artigo será incorporado automaticamente. Os resultados históricos não estimam o consumo deste produto.

### R3 — Percurso de implementação de água

[Alex Tardif — Water Walkthrough](https://alextardif.com/Water.html).

Relato do próprio autor sobre grelha, tessellation, ondas, normais, reflexos e espuma. Ajuda a identificar dependências entre passes. O autor explicita aproximações artísticas; não é um modelo de água fisicamente completo. Para o HYPNIX, tessellation de hardware e reflexos de ecrã ficam como alternativas posteriores, não requisitos da primeira grelha.

### R4 — Fundamento espectral

[Jerry Tessendorf — Simulating Ocean Water, notas disponibilizadas pela Clemson](https://jtessen.people.clemson.edu/reports/papers_files/coursenotes2002.pdf).

Fonte do autor para síntese de superfície, dispersão e ótica. A síntese por FFT combina componentes com distribuição estatística em patches que podem ser repetidos. O material também delimita fenómenos que o método básico não cobre. Orienta a comparação precoce P2; não implica copiar shaders nem garantir uma tempestade fotorealista.

### R5 — Espectros e camadas

[SideFX — Ocean Spectrum](https://www.sidefx.com/docs/houdini/nodes/sop/oceanspectrum.html).

Documenta amplitude, frequência, fase, parâmetros de vento e composição de espectros. É evidência de um fluxo de produção para controlar ondas em várias escalas. No HYPNIX, a escolha de bandas e cascatas terá de ser implementada e medida; os parâmetros do Houdini não se traduzem diretamente em sliders equivalentes.

### R6 — Espuma ao longo do tempo

[SideFX — Ocean Evaluate](https://www.sidefx.com/docs/houdini/nodes/sop/oceanevaluate-.html).

Documenta atributos de crista e acumulação/decadência de espuma, além da exportação de mapas. Motiva a persistência temporal. O nosso domínio de textura e a aproximação de transporte precisam de validação própria, sobretudo em pausa, resize e alteração do vento.

### R7 — Material de superfície

[SideFX — Ocean Surface](https://www.sidefx.com/docs/houdini/gallery/shop/vopmaterial/oceansurface.html).

Descreve um material de oceano com contribuição difusa baixa, atenuação e controlo de espuma. Sustenta a prioridade de reflexos e absorção sobre uma cor difusa saturada. As funções disponíveis no Houdini não estão automaticamente disponíveis em D3D11.

### R8 — Fresnel e filtragem de materiais

[Google Filament — Physically Based Rendering](https://google.github.io/filament/main/filament.html).

Documentação do motor sobre BRDF, Fresnel, IOR, rugosidade, ambiente e filtragem. Inclui água com IOR de 1,33 e refletância frontal de aproximadamente 2%. Usar essas relações como referência de calibração. O HYPNIX continuará com renderer próprio e aproximações explicitamente documentadas.

### R9 — Compute em D3D11

[Microsoft — Compute Shader Overview](https://learn.microsoft.com/en-us/windows/win32/direct3d11/direct3d-11-advanced-stages-compute-shader) e [Direct3D feature levels](https://learn.microsoft.com/en-us/windows/win32/direct3d11/overviews-direct3d-11-devices-downlevel-intro).

Confirmam a programação de compute em HLSL, Dispatch e diferenças de capacidade. O projeto já cria devices 11_0/11_1, o que torna `cs_5_0` uma opção coerente para espuma/FFT. Verificar formatos e limites concretos, mantendo a distinção entre disponibilidade da funcionalidade e velocidade.

### R10 — Apresentação e medição

[Microsoft — DXGI flip model](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/for-best-performance--use-dxgi-flip-model) e [timestamp disjoint](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ns-d3d11-d3d11_query_data_timestamp_disjoint).

Apoiam a apresentação flip-model do desktop e a medição GPU com timestamps válidos. As prévias atuais têm saída offscreen/GDI, com readback próprio, e devem ser medidas separadamente. Comportamento num jogo fullscreen não prova comportamento equivalente no host do Explorer.

### R11 — Exemplo de oceano espectral comercial

[NVIDIA — WaveWorks](https://developer.nvidia.com/waveworks).

Descreve oceano no domínio da frequência, IFFT, JONSWAP, espuma e níveis de detalhe, com interfaces de gráficos nativas. Confirma a viabilidade geral desse tipo de arquitetura. O download está sujeito a EULA e a página não resolve por si só manutenção, redistribuição ou adequação atual ao produto. Nesta proposta é referência, não dependência escolhida.

### R12 — Proveniência de iluminação

[Poly Haven — Asset License](https://polyhaven.com/license).

A página identifica os assets como CC0 e permite uso comercial. Isso não se estende automaticamente aos exemplos renderizados, logótipos ou conteúdo editorial do site. Se for escolhida uma HDRI, registar página específica, autor, licença, ficheiro, hash, data e conversão usada. Nenhum asset foi descarregado nesta pesquisa.

## 3. Decisões registadas

As fontes R13–R19 abaixo sustentam as mudanças desta revisão e a resposta sobre DLSS. As decisões continuam propostas de engenharia; não houve ensaio gráfico.

| ID | Estado | Decisão | Reabrir quando |
| --- | --- | --- | --- |
| D01 | Recomendada | D3D11/HLSL nativo no HYPNIX | Surgir uma limitação concreta não resolvível no backend atual |
| D02 | Recomendada | `OceanGpuRenderer` dedicado | A prova demonstrar que um backend comum pequeno reduz duplicação sem regressões |
| D03 | Revista | Gerstner como referência de comparação e candidato Económico | Comparação P2 demonstrar adequação também a níveis superiores |
| D04 | Revista | Espectro/IFFT multiescala avaliado cedo, candidato Equilibrado/Alto | Ganho visual não justificar custo/complexidade na comparação controlada |
| D05 | Revista | Céu distribuído + sol/lua de tamanho finito, sem dupla contagem | Outra representação conservar a mesma resposta com custo melhor |
| D06 | Recomendada | Cena ambiente, áudio desligado por capacidade na v1 | Houver pedido explícito por reação áudio e proposta visual coerente |
| D07 | Confirmada pelo utilizador | Equilibrar realismo e consumo com níveis | O utilizador alterar a prioridade |
| D08 | Recomendada | 30 FPS como recomendação, respeitando a escolha global | Medições e preferência do utilizador justificarem outro default |
| D09 | Recomendada | Saída SDR, cor interna linear | Houver requisito separado de saída HDR e testes de monitor |
| D10 | Recomendada | Qualidade explícita antes de automática | Existirem medições estáveis e política de adaptação testada |
| D11 | Recomendada | Built-in na primeira entrega | For definido suporte a pacotes Ocean com contrato próprio |
| D12 | Recomendada | Filtragem especular e transferência de escalas desde P2 | Uma alternativa demonstrar a mesma estabilidade e conservação de resposta |
| D13 | Recomendada | Separar material de água, espuma e efeitos óticos | Um requisito de produto justificar outro acabamento |
| D14 | Recomendada | DLSS 5 fora das dependências; Super Resolution só como experiência futura | Existir ganho medido, integração compatível e custo aceitável |
| D15 | Atualizada pelo código | Desktop DXGI; prévia offscreen/staging/GDI | A arquitetura de apresentação do produto mudar de forma validada |

“Recomendada” significa decisão proposta neste plano, não funcionalidade já implementada nem escolha adicional confirmada pelo utilizador.

## 4. Comparações e experiências

### E1 — Quanto a iluminação melhora o mesmo mar?

Fixar ondas, câmara, exposição e resolução. Comparar representação do disco finito com uma referência amostrada e avaliar o céu distribuído sem contar o astro duas vezes. Gravar 30 s por configuração e medir custo. Rever sol alto, pôr do sol, lua e céu difuso; não confundir uma exposição mais clara com mais realismo.

### E2 — Gerstner é suficiente para a direção artística?

Comparar conjuntos de componentes Gerstner em escalas/direções documentadas e energia semelhante. 4/8/12 componentes servem como baselines de custo, não limites de qualidade. Procurar padrões repetitivos e cristas regulares. Executar em conjunto com E5 antes de consolidar a arquitetura; mais componentes também têm custo que precisa de medição.

### E3 — A espuma melhora ou distrai?

Comparar sem espuma, máscara instantânea e acumulação temporal. Observar persistência, escala e movimento. Medir o custo do feedback e a memória por viewport. Aprovar espuma somente quando acrescenta coerência; não usá-la para esconder uma superfície defeituosa.

### E4 — Onde gastar os píxeis?

Em saída 4K, comparar 720p/1080p/1440p internos, 4K nativo e capturas supersampled reduzidas à mesma saída como referência. A referência pode ser lenta/offline; isso não a torna uma opção de runtime. Manter material, luz e exposição. Rever energia, detalhe, cintilação e rastos. Repetir em ultrawide/retrato e não aprovar Alto a 1440p por antecipação.

### E5 — Gerstner contra FFT

Em P2, junto de E1/E2, trocar a geração das ondas mantendo material filtrado, energia, câmara, luz e apresentação. Uma cascata valida a matemática; a comparação visual inclui duas/três bandas candidatas com extensão e resolução explícitas. Medir simulação, material e composição. Verificar tiling, normalização e energia nas sobreposições antes de atribuir o resultado à técnica.

### E6 — GPU integrada e sistema híbrido

Identificar adaptadores e saídas via DXGI. Comparar a mesma configuração com a política atual e uma seleção compatível com o monitor quando possível. Medir desktop mais prévia e duas saídas. Um resultado mais rápido pode consumir mais energia; reportar as duas dimensões quando mensuráveis.

### E7 — Filtragem, reconstrução e brilhos

Com câmara parada e água animada, comparar filtro espacial/material e eventual reconstrução temporal contra referência supersampled espacial. Para análise temporal, definir também subamostras temporais, janela de exposição e FPS comuns, separando aliasing espacial, temporal e motion blur. Variar ângulo rasante, brilho, agitação e resolução. Manter movimentos/exposição corretos e examinar desoclusões. DLSS Super Resolution só entra numa etapa posterior se houver necessidade medida; não incluir DLSS 5 como condição para executar esta prova.

## 5. Estratégia de assets e autoria

Seguir [Shader wallpaper authoring](SHADER_WALLPAPER_AUTHORING.md): código original, créditos honestos e assets com licença compatível com o produto. Usar artigos para compreender conceitos; não copiar snippets de shaders para o código comercial.

Começar com céu e detalhe procedurais próprios elimina a necessidade imediata de escolher ficheiros externos. Uma eventual cubemap deve ser convertida fora do runtime para um formato local previamente definido, com mipmaps/prefiltragem e metadados reproduzíveis. Definir como o loader lê o formato antes de o colocar no manifesto.

O projeto copia atualmente padrões específicos de imagens e shaders. O empacotador também filtra assets conhecidos. Portanto, acrescentar `.dds`, `.hdr` ou outro recurso exige mudanças explícitas nesses dois pontos e teste do pacote offline. Os pacotes importados pelo utilizador mantêm as suas restrições atuais.

## 6. Lacunas e confiança

| Questão | Confiança atual | Próxima evidência |
| --- | --- | --- |
| D3D11 cabe na arquitetura | Alta: dependências e renderers locais inspecionados | Prova Ocean no host real |
| Gerstner pode dar água convincente | Alta para a técnica; por validar para estas referências | Comparação controlada P2/P3 |
| Parecer suficientemente próximo da direção pretendida | Em aberto | Matriz de mar/luz e revisão em movimento |
| Orçamento em iGPU | Em aberto | Modelo/driver identificados e medições |
| Custo adicional em dois monitores | Em aberto | Medição com ligações/GPUs reais |
| Vantagem efetiva de espectro/IFFT neste wallpaper | Candidato tecnicamente fundamentado, resultado ainda em aberto | Comparação E5 já incluída em P2 |
| Benefício de HDRI externa | Em aberto | Comparação E1 e pipeline local |
| Tecnologia exata do Chaos Pack | Desconhecida | Informação publicada pelo autor; aparência não prova ferramenta |
| Integração/custo de DLSS 5 neste renderer | Não verificados; sem necessidade demonstrada | Só investigar SDK/compatibilidade se houver experiência futura justificada |

Nesta tarefa não houve instalação de dependências, download de assets externos, benchmark GPU, criação de renderer ou alteração de wallpaper. As seis imagens já fornecidas pelo utilizador foram preservadas em `docs/references/ocean`, para revisão, sem edição e sem inclusão no produto. A evidência obtida é pesquisa documental e inspeção do código; as experiências convertem hipóteses em decisões medidas.

## 7. Fontes acrescentadas na revisão B

### R13 — Transferência entre geometria, normais e BRDF

[Bruneton, Neyret e Holzschuch — Real-time Realistic Ocean Lighting using Seamless Transitions from Geometry to BRDF](https://morpho.inrialpes.fr/Publications/2010/BNH10/article.pdf).

Fundamenta a representação do detalhe em escalas distintas e a continuidade da iluminação quando a geometria deixa de resolver ondas. Motiva D12; não torna uma BRDF específica nem uma quantidade histórica de ondas obrigatória para o HYPNIX.

### R14 — Oceano e whitecaps multiescala

[Dupuy e Bruneton — Real-time Animation and Rendering of Ocean Whitecaps](https://liris.cnrs.fr/Documents/Liris-5812.pdf).

Relaciona bandas de superfície, compressão/Jacobiano e representação de espuma. Apoia o estudo de cascatas e filtragem; não equivale a simular fisicamente uma onda volumétrica a rebentar.

### R15 — Aliasing especular

[NVIDIA Research — Filtering Distributions of Normals for Shading Antialiasing](https://research.nvidia.com/publication/2016-06_filtering-distributions-normals-shading-antialiasing).

Trata a filtragem de distribuições de normais para controlar aliasing de shading. Apoia a exigência de um filtro compatível com o material. Não justifica misturar fórmulas de distribuições diferentes sem verificação.

### R16 — Fonte solar/lunar

[NASA — Exploring Angular Diameter](https://eclipse2017.nasa.gov/exploring-angular-diameter) e [NASA — Moonlight](https://science.nasa.gov/moon/moonlight/).

Referências para o tamanho angular aparente e a origem refletida da luz lunar. A escolha de integrar discos finitos separadamente do céu é uma proposta de engenharia desta revisão.

### R17 — Câmara e profundidade de campo

[Physically Based Rendering — Realistic Cameras](https://pbr-book.org/3ed-2018/Camera_Models/Realistic_Cameras).

Fundamento de efeitos da ótica/apertura. Ajuda a separar aparência da água e resposta da câmara. Os brilhos em estrela da imagem 05 são interpretados visualmente como acabamento ótico/artístico; não foi identificada a técnica exata usada nessa imagem.

### R18 — DLSS 5 e renderização neural

[NVIDIA Technical Blog — DLSS 5 with 3D-Guided Neural Rendering](https://developer.nvidia.com/blog/whats-new-for-game-developers-dlss-5-with-3d-guided-neural-rendering-nvidia-ace-updates-and-new-rtx-kit-capabilities/), publicado em 22/09/2026.

A NVIDIA descreve a tecnologia como uma etapa neural sobre o frame do motor e vetores de movimento. A descrição não prova qualidade, custo ou integração compatível com o nosso oceano. Decisão: não é dependência.

### R19 — Super Resolution e integração

[NVIDIA — Streamline, requisitos e checklist DLSS Super Resolution](https://developer.nvidia.com/rtx/streamline/get-started).

Documenta requisitos de integração como movimento, jitter e exposição. Suporte de uma API no Streamline não prova que todas as suas funcionalidades a suportem. Avaliar cada funcionalidade/versão separadamente se a otimização futura for justificada.
