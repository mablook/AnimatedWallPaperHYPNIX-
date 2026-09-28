# Oceano — reflexo solar e lunar durante o ciclo

28/09/2026. Correção sobre `9942d38`, na branch `codex/ocean-and-day-cycle`.

## Problema observado e diagnóstico

O brilho permanecia muito forte junto ao horizonte mesmo com o astro alto, dando a impressão de que o reflexo estava preso ao nascer do Sol ou da Lua.

A direção astronômica já era atualizada. O disco visível e a iluminação direta da água usam a mesma direção aparente, incluindo refração. A auditoria também verificou constantes CPU/GPU, unidades angulares, avanço do relógio e invalidação dos caches. Não foi encontrado um vetor de luz congelado.

Capturas com ondas e vento congelados separaram quatro contribuições: Sol direto, Lua direta, ambiente refletido e dispersão do ar. Uma superfície plana de diagnóstico mostrou o reflexo se deslocando conforme a lei de reflexão já antes da correção. O problema identificado foi a resposta da iluminação rasante e do ambiente sobre água rugosa, não a órbita dos astros.

## Mudanças

1. **Limite rasante contínuo.** O piso artificial `NoV = max(dot(n,v), .015)` foi removido. `G1(NoV)/NoV` agora é avaliado por sua expressão estável, cujo limite em zero é `3.535/sqrt(alpha²)`. O piso anterior correspondia a aproximadamente 0,86°.
2. **Energia refletida compatível com a rugosidade.** O ambiente usava Fresnel de uma superfície lisa, embora as ondas distantes já estivessem integradas como variância de inclinações. Agora uma tabela de albedo direcional integra a mesma BRDF Beckmann/Smith usada pelos astros. Não se emprega uma aproximação GGX para uma distribuição Beckmann.
3. **Consulta na direção refletida.** Foi retirada a mistura do ambiente com a direção fixa `(0,.35,1)`. O ambiente é consultado a partir de `reflect(-v,n)`, conservando o filtro por mipmap e os probes espaciais.
4. **Comparação na prévia.** O botão **Avançar 1 h**, disponível com o ciclo ligado, move o relógio celeste uma hora sem adiantar o relógio das ondas ou do vento. **Ver Sol** e **Ver Lua** continuam levando ao nascimento de cada astro.

Exemplo numérico reproduzível na tabela: para `NoV=(16/127)²` e `alpha²=(33/127)²`, o Fresnel liso era aproximadamente **0,925**. A refletância integrada é aproximadamente **0,319**. Essa diferença explica uma contribuição ambiental excessiva no horizonte. Em ângulos menos rasantes a diferença é bem menor.

A tabela RG16F tem 128×128 texels, **64 KiB**, é construída uma vez por renderer/dispositivo com 1.024 amostras por texel e custa uma consulta por fragmento da água. Armazena `A/B`, com `E=F0*A+(1-F0)*B` e `F0=.02037`. O domínio é `NoV, alpha² ∈ [0,1]`; a linha de rugosidade zero usa o limite analítico. Não depende de clima, hora, imagem anterior ou DLSS.

Os campos espectrais, deslocamentos, derivadas, geometria e normais da água de produção foram preservados. A superfície plana e a separação das contribuições existem apenas nos testes nativos.

## Continuidade e limites

A geometria rasterizada pode estar visível enquanto a normal de shading, filtrada separadamente, cruza a tangente da câmera. Uma tentativa de rejeitar toda iluminação direta quando `dot(n,v)<=0` produziu faixas pretas horizontais na água distante. Essa tentativa foi descartada. A implementação usa `saturate(dot(n,v))` e o limite rasante contínuo para essas normais, tanto no ambiente quanto na luz direta. Trata-se de uma extensão de shading contínua; não é uma solução física completa de visibilidade ou auto-oclusão das ondas. Luz atrás da normal continua limitada por `G1(NoL)`; astros totalmente abaixo do horizonte continuam rejeitados.

Água com ondas não é um espelho plano: várias inclinações refletem o astro em pontos diferentes. Alguns brilhos distantes são legítimos mesmo quando o reflexo de uma superfície plana já saiu da imagem. A correção não pinta uma faixa que segue coordenadas de tela e não apaga artificialmente o horizonte conforme a elevação do astro.

O modo **Cinemático** conserva sua compressão de azimute para manter a composição; **Geográfico** conserva a direção geográfica em relação à câmera. Isso afeta quanto o reflexo se desloca lateralmente.

O albedo direcional corrige a energia integrada sob iluminação uniforme. Os mipmaps de ambiente continuam sendo uma aproximação angular: não equivalem à convolução completa da BRDF com um céu variável, nem ao traçado de caminhos entre ondas e nuvens. Essa aproximação pode deixar diferenças de largura/forma no brilho atmosférico refletido e permanece como refinamento futuro.

## Validação

- Build Release e 420 testes unitários passaram.
- Matriz nativa com Sol/Lua, modos Geográfico/Cinemático e cinco horários por astro: **20 poses**, cada uma com imagem completa, contribuição direta sobre ondas e contribuição direta sobre plano.
- Oráculo independente projeta o raio refletido `(Lx,-Ly,Lz)` na câmera. Quando dentro do enquadramento, o máximo no plano deve ficar a até 16 pixels por eixo da posição prevista em 1280×720. A tolerância inclui disco finito, fase/textura lunar e largura residual de inclinação de 0,005 no plano de diagnóstico. Essa tolerância não é uma certificação fotométrica da cena com ondas.
- Quando o reflexo previsto fica mais de 100 pixels abaixo da imagem, a contribuição direta máxima no plano deve ser ≤1e-5 em HDR linear.
- Leitura HDR verifica valores finitos e não negativos. A tabela inteira deve respeitar `0≤B≤A≤1`; dez pontos são comparados com integral independente em double de 262.144 amostras, com tolerância absoluta de 0,0015 por coeficiente.
- Regressão volumétrica verifica transporte analítico, preservação dos campos da água, pausa, busca temporal, recriação, cache distribuído, vento independente, limites de transmissão e formatos de saída.
- Janela exercitada com resize, pausa, Sol/Lua, avanço de uma hora, comparação de ambientes e troca de nuvens/neblina.

Os resultados da matriz acompanham este documento em [reflection.json](validation/ocean-reflection-checks-2026-09-28.json). Capturas e diagnósticos ficam em `artifacts/ocean-reflection/`, ignorado pelo Git. A inspeção visual inclui amanhecer e Lua mais alta para conferir que o tratamento rasante não reintroduz faixas pretas.

Uma sondagem curta de desempenho, com 2 s de aquecimento e 8 s de medição, acompanha a correção em [benchmark.json](validation/ocean-reflection-benchmark-2026-09-28.json). Ela verifica o custo imediato, mas não substitui o ensaio longo documentado na [implementação volumétrica](OCEAN_VOLUMETRIC_ENVIRONMENT_IMPLEMENTATION.md). O protocolo usa HWND oculto e não mede potência, temperatura ou custo do compositor de uma janela visível.

## Reproduzir

Na raiz do repositório, PowerShell:

```powershell
dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release -p:OutDir=D:/desktopapp/AnimatedWallPaper/artifacts/ocean-reflection/bin/
dotnet artifacts/ocean-reflection/bin/Hypnix.NativeSmoke.dll artifacts/ocean-reflection/final --ocean-reflection
dotnet artifacts/ocean-reflection/bin/Hypnix.NativeSmoke.dll artifacts/ocean-reflection/volumes --ocean-volumes
dotnet artifacts/ocean-reflection/bin/Hypnix.NativeSmoke.dll artifacts/ocean-reflection/benchmark --ocean-volume-benchmark --quick
dotnet artifacts/ocean-reflection/bin/Hypnix.NativeSmoke.dll artifacts/ocean-reflection/window --ocean-window-check
./scripts/show-ocean-preview.ps1
```

`show-ocean-preview.ps1 -Validate` inclui a matriz de reflexos antes de abrir a prévia. Para comparação visual: **Ver Sol** ou **Ver Lua**, depois **Avançar 1 h**. O astro pode sair do campo de visão da câmera quando sobe; isso não interrompe seu movimento ou sua iluminação.

Referência primária para amostragem de microfacetas e transformação do PDF pelo meio-vetor: [PBRT — Sampling Reflection Functions](https://pbr-book.org/3ed-2018/Light_Transport_I_Surface_Reflection/Sampling_Reflection_Functions). A implementação numérica foi feita para o modelo e os recursos deste renderer.
