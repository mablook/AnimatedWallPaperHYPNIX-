# Oceano P3 — cores, sol, lua e céu

Data: 2026-09-27. Estado: primeira iteração implementada, com feedback positivo sobre cores e consumo. Sol/lua parecem pontos e as nuvens ainda precisam de realismo; o [plano seguinte](OCEAN_SKY_REALISM_PLAN.md) detalha a correção. Ver [relatório P3](OCEAN_P3_IMPLEMENTATION.md). A água P2 permanece preservada. O plano abaixo conserva os objetivos da primeira iteração.

## Decisão do utilizador

A água da segunda prévia foi aprovada: “ja nao vejo as linhas” e “a agua esta perfeita”. O trabalho seguinte deve concentrar-se em **cores realistas, sol, lua e céu**. A [superfície P2](OCEAN_P2_IMPLEMENTATION.md) é a referência a preservar.

## O que preservar

Manter a geração das ondas: três bandas 256² com extensões 192 / 28 / 4,5 m, RMS base 0,24 / 0,12 / 0,012 m, seeds existentes, ganhos por estado de mar e deformação horizontal 0,45. Manter relógio, dispersão, fases, malha projetada, deslocamentos, derivadas e filtragem da superfície. Conservar os enquadramentos de referência para a comparação.

Os ficheiros fonte aprovados estão identificados no [manifesto SHA-256](references/ocean/p2-approved-source.sha256), com cópia local em `artifacts/ocean-p2/approved-baseline-20260927`. Uma mudança de iluminação deve alterar a aparência refletida mantendo o mesmo campo de ondas no mesmo instante lógico.

## Sequência de trabalho

1. **Cor e exposição.** Organizar os parâmetros de iluminação separadamente dos parâmetros de ondas. Manter composição em espaço linear e uma única conversão final. Comparar exposição e compressão de altas luzes com capturas de referência, conservando volume nas zonas escuras e variação de cor nos reflexos.
2. **Céu e horizonte.** Substituir o gradiente simplificado por um ambiente com distribuição de luminosidade e cor mais convincente. Definir uma única função/modelo de céu para a vista e para o ambiente refletido, com a mesma direção do astro. Avaliar transição do horizonte e atmosfera distante antes de acrescentar nuvens detalhadas.
3. **Sol.** Refinar o disco visível, a cor da iluminação e a exposição. Manter o disco coerente com a fonte usada pelo material e evitar contar a sua energia duas vezes. O trilho dourado ou branco deve resultar da luz e das normais existentes, sem pintar uma faixa sobre a água.
4. **Lua e noite.** Trabalhar disco lunar, céu noturno, exposição e contraste em conjunto. Documentar qualquer pré-exposição artística. Buscar uma noite legível, com reflexo prateado e relevo perceptível, sem simplesmente aplicar azul ao preset diurno.
5. **Comparação e custo.** Rever as cenas abaixo em pausa e movimento, começando pela configuração aprovada. Medir o custo adicional da iluminação separado da simulação, além do custo total da prévia. Só então consolidar presets e níveis de qualidade.

Modelo da primeira iteração: dispersão simples RGB Rayleigh/Mie com absorção aproximada por ozono, mapa HDR compartilhado e nuvens procedurais estáticas. Calibração fotométrica absoluta e múltipla dispersão completa permanecem fora desta prova; detalhes no [relatório P3](OCEAN_P3_IMPLEMENTATION.md).

## Direção visual por cena

| Cena | Resultado procurado | Problema a detectar |
| --- | --- | --- |
| Dia | Céu e água com cores naturais; reflexos solares claros e volume legível | Água excessivamente ciano, aspeto plástico ou brilho branco uniforme |
| Pôr do sol | Luz quente localizada, contraste entre reflexo e água, horizonte convincente | Toda a superfície alaranjada, canais de cor cortados ou trilho pintado |
| Lua | Noite com gradações escuras, reflexo prateado e astro coerente | Aspeto de dia tingido de azul, luz lunar incoerente ou perda total do relevo |
| Nublado | Ambiente difuso com variação suficiente para revelar a superfície | Água sem volume ou reflexo solar incompatível com o céu |

Usar as [seis referências fornecidas](references/ocean/README.md) para avaliar cor, contraste e distribuição do brilho. As variantes vermelhas e brilhos em estrela permanecem acabamentos artísticos posteriores. Espuma, alteração das ondas e DLSS não fazem parte desta iteração de cores e céu.

## Critérios de aceitação

- A forma e o movimento da água aprovada permanecem reconhecíveis, sem retorno das linhas observadas na P1.
- A posição, a cor e a luminosidade do astro, do céu e do reflexo são coerentes entre si.
- As altas luzes mantêm gradação e as sombras preservam o volume pretendido; não corrigir a exposição modificando amplitudes ou normais das ondas.
- Comparar cada alteração com seed, estado de mar, tempo, câmara, resolução e FPS fixos. Ao avaliar exposição, registar explicitamente a diferença de exposição entre as imagens.
- Rever as quatro iluminações × três estados de mar, nas vistas próxima e com horizonte; verificar reflexos em movimento, ultrawide, retrato e 4K.
- Reutilizar os testes matemáticos e de continuidade da P2. A avaliação visual deve complementar os testes; os testes não substituem a aprovação da aparência.
- Registar consumo e limitações medidos. O equilíbrio entre realismo e consumo continua a orientar as escolhas.

A aprovação da água já foi recebida. A aprovação seguinte será a do **conjunto de cores, céu, sol/lua e reflexos**, com a superfície preservada.
