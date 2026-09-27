# Oceano P2 — superfície espectral e comparação

Data: 2026-09-27. Estado: **superfície da água aprovada visualmente pelo utilizador na prévia apresentada**. A próxima etapa é cores, sol, lua e céu; a validação técnica ampliada e a aprovação do conjunto final permanecem pendentes. Complementa o [plano](OCEAN_WALLPAPER_PLAN.md) e preserva o [relatório histórico P1](OCEAN_P1_IMPLEMENTATION.md).

## Aprovação da água e referência a preservar

Após experimentar a segunda versão, o utilizador confirmou: **“ja nao vejo as linhas”** e **“a agua esta perfeita”**. Também descreveu o resultado como “quase perfeito” e definiu o próximo objetivo: **cores realistas, sol, lua e céu**. Esta aprovação refere-se à aparência da água observada na prévia, sem substituir a matriz de testes em todos os formatos, estados e durações.

A superfície espectral P2 passa a ser a referência visual aprovada. Preservar espectro, seeds, bandas, amplitudes, fases, velocidade, deformação horizontal, geometria projetada, derivadas e filtragem das normais durante a etapa de iluminação. Não recalibrar as ondas para compensar um problema de cor ou de luz. Alterações necessárias por um defeito demonstrável devem ser isoladas e comparadas com esta referência.

Foi guardada uma cópia local de nove ficheiros fonte em `artifacts/ocean-p2/approved-baseline-20260927`, com correspondência SHA-256 verificada. O [manifesto da referência](references/ocean/p2-approved-source.sha256) identifica os conteúdos exatos. A cópia em `artifacts` é local e não substitui controlo de versão. Alguns ficheiros contêm tanto superfície como iluminação: o manifesto identifica a versão aprovada, não impede a edição dos trechos de luz na fase seguinte.

O [plano P3 de cores e iluminação](OCEAN_P3_LIGHTING_PLAN.md) define o trabalho seguinte sem reabrir a escolha da superfície.

Atualização: a [primeira revisão P3](OCEAN_P3_IMPLEMENTATION.md) está implementada e mantém a água desta referência. A prévia atual abre o estudo 03 com comparação de iluminação; os resultados de desempenho e testes abaixo pertencem à P2.

## Experimentar

Executar `./scripts/show-ocean-preview.ps1` na raiz do projeto. A janela **HYPNIX · Oceano — estudo 02** abre com **Ondas novas**. O seletor **Ondas anteriores** permite comparar a geração da superfície mantendo câmara, luz, material e malha projetada iguais. Não é uma reprodução pixel a pixel da P1, que utilizava outra distribuição de vértices. Pausar permite comparar os dois modelos no mesmo tempo lógico.

Continuam disponíveis quatro iluminações, três estados de mar, horizonte/enquadramento próximo, velocidade, resolução, pausa e captura. A prévia é independente da aplicação instalada. O catálogo e a integração completa no desktop permanecem para outra etapa.

## Diagnóstico e alteração

A P1 concentrava a geometria em 16 ondas analíticas longas, com apenas 32 componentes no material. As direções e comprimentos restritos produziam faixas reconhecíveis. A distribuição logarítmica da malha também gastava vértices fora da parte útil da imagem. Ambos contribuíam para a aparência artificial; aumentar apenas a resolução não corrigiria a distribuição das ondas.

A nova implementação substitui a geração padrão por três campos espectrais independentes, sintetizados por IFFT 2D em compute D3D11. Usa coeficientes gaussianos determinísticos, dispersão de águas profundas e deslocamento horizontal. O espectro é uma distribuição direcional inspirada em Phillips com calibração artística de RMS por banda; **não é um modelo JONSWAP calibrado para um vento ou altura significativa medidos**.

| Banda | Extensão periódica | Resolução | RMS de altura esperado antes do ganho do estado de mar |
| --- | --- | --- | --- |
| Ondulação longa | 192 m | 256 × 256 | 0,24 m |
| Ondas intermédias | 28 m | 256 × 256 | 0,12 m |
| Ondulações finas | 4,5 m | 256 × 256 | 0,012 m |

As janelas suaves particionam a potência por comprimento de onda. A normalização independente de RMS define uma distribuição artística de energia; ainda é necessário calibrar as sobreposições e o estado de mar contra referências. Os patches continuam periódicos, e a ausência de repetição perceptível em sessões longas não está certificada.

A malha passa a ser projetada pela câmara sobre o plano médio da água, com margem fora do enquadramento. São 384 × 256 células; o deslocamento é filtrado conforme o espaçamento dos vértices. O enquadramento próximo foi ajustado para mostrar a superfície e o reflexo com uma incidência mais rasante.

Altura, deslocamentos horizontais e as cinco derivadas espaciais vêm do mesmo campo. Mipmaps guardam médias de inclinação e segundos momentos, para transferir parte do detalhe não resolvido ao lóbulo especular. A amostragem considera o tamanho projetado do píxel e usa filtragem anisotrópica. Isso ainda é uma aproximação isotrópica da variância residual, sem a covariância completa nem validação temporal por supersampling.

A deformação horizontal espectral usa 0,45. O valor inicial 0,65 deixou um limite conservador de compressão negativo no diagnóstico; foi reduzido. O limite agora é positivo nos quatro estados temporais amostrados, sem afirmar uma prova para todo tempo possível. O modelo analítico anterior mantém o seu próprio parâmetro.

As fases são recalculadas em double por épocas de 64 segundos e evoluídas em float apenas dentro da época. O relógio continua fora dos recursos GPU, preservando o tempo ao pausar, mudar resolução ou recriar o renderer.

## Código

- [OceanSpectrumSeed](../Services/OceanSpectrumSeed.cs): espectro, normalização, conjugação e fases.
- [OceanSpectrum](../Services/OceanSpectrum.cs) e [OceanSpectrum.hlsl](../Shaders/OceanSpectrum.hlsl): evolução, IFFT radix-2, montagem dos mapas e mipmaps. Implementação própria.
- [OceanGpuRenderer](../Services/OceanGpuRenderer.cs) e [Ocean.hlsl](../Shaders/Ocean.hlsl): ligação dos recursos, malha projetada, geometria e material.
- [OceanSpectrumChecks](../Tests/Hypnix.NativeSmoke/OceanSpectrumChecks.cs): referência DFT direta independente em double, readback e métricas GPU.
- [OceanProofWindow](../Tests/Hypnix.NativeSmoke/OceanProofWindow.cs): comparação interativa.

Fundamentação: [notas de Tessendorf](https://jtessen.people.clemson.edu/reports/papers_files/coursenotes2002.pdf), [sincronização de memória de grupo em HLSL](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/groupmemorybarrierwithgroupsync) e fontes de filtragem reunidas na [pesquisa](OCEAN_RENDERING_RESEARCH.md). O uso dessas técnicas não estabelece, por si só, aprovação visual.

## Validação

Build Release sem erros ou avisos. **394 testes unitários passaram**, incluindo os novos casos de partição de potência, normalização de variância, simetria hermitiana e fases após 30 dias.

Verificações nativas na **NVIDIA GeForce RTX 5070 Ti**:

| Verificação | Resultado |
| --- | --- |
| IFFT GPU contra DFT direta double | 320 comparações: modo único e espectro aleatório, oito campos, quatro pontos e cinco tempos; erro absoluto máximo 1,35 × 10⁻⁵ |
| Componente imaginária residual | Máximo inferior a 6,65 × 10⁻⁷ |
| Energia espacial contra energia espectral | Erro absoluto inferior a 4,08 × 10⁻⁷ |
| Segundo momento no último mip | Erro absoluto inferior a 8,46 × 10⁻⁵, incluindo armazenamento half |
| Compressão horizontal no mar agitado | Limite inferior conservador de autovalor 0,227 nos tempos 0, 10, 64 e 36000,125 s |
| Continuidade da época | Campos comparados à DFT em 63,999 / 64 / 64,001 s |
| 12 combinações de mar/luz | Capturas geradas e inspecionadas em casos representativos; validação automática de conteúdo e contraste |
| Pausa e retorno da comparação analítica | Mesmos píxeis ao voltar aos mesmos parâmetros e tempo |
| GDI, DXGI, resize e recriação | Correspondência RGB, apresentação e preservação de fase verificadas |
| Retrato, ultrawide, 4K | Capturas geradas nos três formatos |
| Janela | Pausa, resize, troca de luz/modelo, retoma, captura e encerramento exercitados |

Evidência: `artifacts/ocean-p2/captures/ocean-checks.json`, PNGs e sequência de 180 frames a 30 FPS nessa pasta; TRX em `artifacts/ocean-p2/test-results`. A leitura visual de imagens estáticas não comprova ausência de cintilação em movimento.

A sondagem curta de GPU a 960 × 540 mediu mediana de aproximadamente **0,22 ms** e p95 de **0,24 ms** em 48 amostras. Exclui readback/apresentação, não mede watts, não substitui o benchmark de 120 segundos e não estima o custo em iGPU. As texturas lógicas nessa configuração somam cerca de **45,9 MiB**, sem incluir toda a residência, buffers e despesas do driver. Todos os níveis ainda usam três bandas de 256²; por enquanto, qualidade altera a resolução de desenho, não o custo da simulação.

## O que ainda impede o resultado final

O céu é um gradiente simplificado, a exposição e a luz lunar são artísticas, e faltam estrutura atmosférica/reflexos mais ricos, oclusão entre ondas e espuma validada para mar forte. O detalhe especular ainda precisa da comparação em movimento contra uma referência espacial/temporal de maior amostragem. Não há TAA, bloom nem DLSS.

O próximo refinamento é melhorar céu, cores e iluminação solar/lunar, preservando a água aprovada. A comparação analítica continua disponível como diagnóstico; não é necessário repetir a escolha visual da superfície. As verificações ampliadas de estabilidade e desempenho continuam no plano, e a aprovação do conjunto P3 ainda está pendente.

## Reprodução

```powershell
dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release --no-restore -p:OutDir=D:/desktopapp/AnimatedWallPaper/artifacts/ocean-p2/bin/ -p:UseSharedCompilation=false
dotnet artifacts/ocean-p2/bin/Hypnix.NativeSmoke.dll artifacts/ocean-p2/captures --ocean-only --ocean-frames
dotnet artifacts/ocean-p2/bin/Hypnix.NativeSmoke.dll artifacts/ocean-p2/window-check --ocean-window-check
dotnet test Tests/Hypnix.Tests/Hypnix.Tests.csproj -c Release --no-restore -p:OutDir=D:/desktopapp/AnimatedWallPaper/artifacts/ocean-p2/unit/ -p:UseSharedCompilation=false
```
