# Oceano 3D — pesquisa e decisões técnicas

Pesquisa: 2026-09-27. Documento associado: [plano de implementação](OCEAN_WALLPAPER_PLAN.md).

Objetivo: escolher um caminho para água convincente no HYPNIX, equilibrando aparência, consumo e custo de integração. As recomendações são inferências de engenharia apoiadas nas fontes e no código local; não descrevem tecnologia confirmada da Optimum nem benchmarks já realizados.

## 1. Conclusões utilizáveis

1. O HYPNIX já tem D3D11, HLSL, sessões nativas, pausa e prévias. A integração mais direta é um renderer C# dedicado nesse sistema.
2. A aparência depende simultaneamente da distribuição de ondas, normais, iluminação refletida e estabilidade temporal. Aumentar a resolução de uma imagem não resolve esses componentes.
3. Gerstner permite uma primeira superfície controlável. FFT merece um experimento quando existir uma referência que permita avaliar se a variedade adicional compensa.
4. Um céu procedural serve de base inicial. Uma cubemap pré-filtrada pode melhorar a luz; importar uma HDRI exige também conversão, carregamento e empacotamento.
5. Uma simulação deve parar quando o wallpaper está oculto. Orçamentos por viewport precisam de incluir as sessões/prévias simultâneas.
6. Água física completa, com rebentação e salpicos, constitui outro nível de complexidade. O alvo inicial será mar aberto sem interação com objetos.

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

Fonte do autor para síntese de superfície, dispersão e ótica. A síntese por FFT combina componentes com distribuição estatística em patches que podem ser repetidos. O material também delimita fenómenos que o método básico não cobre. Orienta F1; não implica copiar os shaders de exemplo nem garantir uma tempestade fotorealista.

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

Apoiam a reutilização da apresentação flip-model e a medição GPU com timestamps válidos. Medir o efeito no HWND de wallpaper e nas prévias; comportamento num jogo fullscreen não prova comportamento equivalente no host do Explorer.

### R11 — Exemplo de oceano espectral comercial

[NVIDIA — WaveWorks](https://developer.nvidia.com/waveworks).

Descreve oceano no domínio da frequência, IFFT, JONSWAP, espuma e níveis de detalhe, com interfaces de gráficos nativas. Confirma a viabilidade geral desse tipo de arquitetura. O download está sujeito a EULA e a página não resolve por si só manutenção, redistribuição ou adequação atual ao produto. Nesta proposta é referência, não dependência escolhida.

### R12 — Proveniência de iluminação

[Poly Haven — Asset License](https://polyhaven.com/license).

A página identifica os assets como CC0 e permite uso comercial. Isso não se estende automaticamente aos exemplos renderizados, logótipos ou conteúdo editorial do site. Se for escolhida uma HDRI, registar página específica, autor, licença, ficheiro, hash, data e conversão usada. Nenhum asset foi descarregado nesta pesquisa.

## 3. Decisões registadas

| ID | Estado | Decisão | Reabrir quando |
| --- | --- | --- | --- |
| D01 | Recomendada | D3D11/HLSL nativo no HYPNIX | Surgir uma limitação concreta não resolvível no backend atual |
| D02 | Recomendada | `OceanGpuRenderer` dedicado | A prova demonstrar que um backend comum pequeno reduz duplicação sem regressões |
| D03 | Recomendada | Gerstner primeiro | Padrões continuarem artificiais em comparação controlada |
| D04 | Condicional | FFT em compute como F1 | Existir ganho visual justificável dentro do orçamento |
| D05 | Recomendada | Iluminação procedural inicial; cubemap opcional | Uma HDRI específica melhorar claramente o preset e tiver pipeline local validado |
| D06 | Recomendada | Cena ambiente, áudio desligado por capacidade na v1 | Houver pedido explícito por reação áudio e proposta visual coerente |
| D07 | Confirmada pelo utilizador | Equilibrar realismo e consumo com níveis | O utilizador alterar a prioridade |
| D08 | Recomendada | 30 FPS como recomendação, respeitando a escolha global | Medições e preferência do utilizador justificarem outro default |
| D09 | Recomendada | Saída SDR, cor interna linear | Houver requisito separado de saída HDR e testes de monitor |
| D10 | Recomendada | Qualidade explícita antes de automática | Existirem medições estáveis e política de adaptação testada |
| D11 | Recomendada | Built-in na primeira entrega | For definido suporte a pacotes Ocean com contrato próprio |

“Recomendada” significa decisão proposta neste plano, não funcionalidade já implementada nem escolha adicional confirmada pelo utilizador.

## 4. Comparações e experiências

### E1 — Quanto a iluminação melhora o mesmo mar?

Fixar ondas, câmara e resolução. Comparar luz direcional simples, céu procedural refletido e, se escolhido, ambiente local pré-filtrado. Gravar 30 s por configuração e medir custo do passe. Escolher pela legibilidade das ondas, estabilidade e profundidade aparente, sem confundir uma exposição mais clara com mais realismo.

### E2 — Gerstner é suficiente para a direção artística?

Comparar conjuntos de 4, 8 e 12 ondas, mantendo energia/amplitude geral semelhante. Alterar separadamente o detalhe fino. Procurar padrões repetitivos, cristas demasiado regulares e aspeto de borracha. Se houver progresso suficiente com custo baixo, consolidar esse caminho antes de FFT.

### E3 — A espuma melhora ou distrai?

Comparar sem espuma, máscara instantânea e acumulação temporal. Observar persistência, escala e movimento. Medir o custo do feedback e a memória por viewport. Aprovar espuma somente quando acrescenta coerência; não usá-la para esconder uma superfície defeituosa.

### E4 — Onde gastar os píxeis?

Em saída 4K, comparar 720p/1080p/1440p internos e, como referência, 4K interno se couber na GPU de teste. Manter material idêntico. Rever detalhe do primeiro plano, reflexos distantes e cintilação. Aplicar o mesmo teste a ultrawide; documentar área efetiva e não apenas “resolução equivalente”.

### E5 — Gerstner contra FFT

Só depois de E1/E2. Trocar a geração das ondas e manter a apresentação igual. Avaliar uma cascata antes de várias. Medir simulação, material e composição separadamente. Verificar se tiling, normalização ou bandas sobrepostas estão a criar o problema visual antes de atribuir o resultado à técnica.

### E6 — GPU integrada e sistema híbrido

Identificar adaptadores e saídas via DXGI. Comparar a mesma configuração com a política atual e uma seleção compatível com o monitor quando possível. Medir desktop mais prévia e duas saídas. Um resultado mais rápido pode consumir mais energia; reportar as duas dimensões quando mensuráveis.

## 5. Estratégia de assets e autoria

Seguir [Shader wallpaper authoring](SHADER_WALLPAPER_AUTHORING.md): código original, créditos honestos e assets com licença compatível com o produto. Usar artigos para compreender conceitos; não copiar snippets de shaders para o código comercial.

Começar com céu e detalhe procedurais próprios elimina a necessidade imediata de escolher ficheiros externos. Uma eventual cubemap deve ser convertida fora do runtime para um formato local previamente definido, com mipmaps/prefiltragem e metadados reproduzíveis. Definir como o loader lê o formato antes de o colocar no manifesto.

O projeto copia atualmente padrões específicos de imagens e shaders. O empacotador também filtra assets conhecidos. Portanto, acrescentar `.dds`, `.hdr` ou outro recurso exige mudanças explícitas nesses dois pontos e teste do pacote offline. Os pacotes importados pelo utilizador mantêm as suas restrições atuais.

## 6. Lacunas e confiança

| Questão | Confiança atual | Próxima evidência |
| --- | --- | --- |
| D3D11 cabe na arquitetura | Alta: dependências e renderers locais inspecionados | Prova Ocean no host real |
| Gerstner pode dar água convincente | Alta para a técnica; por validar para este preset | Capturas e vídeo de P2/P3 |
| Parecer suficientemente próximo da direção pretendida | Em aberto | Revisão visual do primeiro preset |
| Orçamento em iGPU | Em aberto | Modelo/driver identificados e medições |
| Custo adicional em dois monitores | Em aberto | Medição com ligações/GPUs reais |
| Utilidade da FFT neste wallpaper | Em aberto | Comparação E5, se necessária |
| Benefício de HDRI externa | Em aberto | Comparação E1 e pipeline local |
| Tecnologia exata do Chaos Pack | Desconhecida | Informação publicada pelo autor; aparência não prova ferramenta |

Nesta tarefa não houve instalação de dependências, download de assets, benchmark GPU, criação de renderer ou alteração de wallpaper. A evidência obtida é pesquisa documental e inspeção do código. As experiências acima são o caminho para converter hipóteses em decisões medidas.
