# Referência pública NASA/JPL Horizons — nascer do Sol e da Lua

Estudo de 2026-09-27. Observador **exemplo**, não localização inferida do utilizador: longitude −9,14° (leste positivo), latitude geodética 38,72°, altura 0,0028 km. Os arquivos `*-query.json` preservam as consultas exatas. `*-original.json` preservam as respostas HTTP originais; `*-original.txt` contêm o campo textual `result`, sem alteração do conteúdo astronômico. Os CSV são extrações numéricas da tabela original.

Fontes: [API Horizons](https://ssd-api.jpl.nasa.gov/doc/horizons.html) e [manual NASA/JPL](https://ssd.jpl.nasa.gov/horizons/manual.html).

## Método

1. Dia completo de 00:00 a 00:00 do dia seguinte, passo 15 min.
2. Busca de nascer/trânsito/pôr com `STEP_SIZE='1m TVH'`.
3. Série de 120 minutos a partir do marcador retornado de nascer, passo 5 min: 25 amostras por astro.

O marcador de nascer solar retornado é aproximadamente **06:29 UT**; o lunar, **18:38 UT**. São amostras da busca TVH, não instantes observados exatos. A explicação da resposta informa precisão dos marcadores limitada a até duas vezes o passo de busca de 1 min. TVH considera horizonte visual do elipsoide, depressão do horizonte e refração de luz amarela. Isso não transforma a coluna de elevação em refratada: ela foi explicitamente solicitada como `APPARENT='AIRLESS'`.

## Resultados principais

| Astro | UT inicial → final | Elevação do centro, airless | Diâmetro angular completo | Distância aparente |
|---|---|---|---|---|
| Sol | 06:29 → 08:29 | −0,772618° → 22,058452° | 1913,922″ → 1913,999″ | 149 952 730,12 → 149 946 735,83 km |
| Lua | 18:38 → 20:38 | −0,738158° → 22,114843° | 1908,105″ → 1922,135″ | 375 624,7819 → 372 882,9963 km |

A fração lunar iluminada vai de 98,42453% a 98,18277%; `S-T-O` vai de 14,4267° a 15,5000°. Trata-se de Lua gibosa muito próxima da cheia. A fração iluminada não equivale à fração de brilho.

No intervalo, o diâmetro lunar aumenta **0,7352845%**, e o solar **0,00402315%**. Esse exemplo ilustra que o tamanho angular físico não segue a ampliação artística da Lua no horizonte: a distância topocêntrica lunar diminui enquanto ela sobe neste caso. Não generalizar estes números para outras datas/localizações.

## Definições e cuidados de uso

- Azimute: graus no sentido horário, norte 0°, leste 90°. Elevação: centro do astro relativamente ao plano perpendicular ao zênite local do elipsoide.
- `AIRLESS` significa **aparente sem refração atmosférica**, não coordenada geométrica instantânea: as correções de tempo de luz, deflexão gravitacional, aberração, precessão e nutação constam na definição original.
- Coordenadas já são **topocêntricas**. Não aplicar paralaxe lunar novamente.
- O primeiro bordo aparecer não equivale a elevação do centro igual a zero. A busca TVH emprega sua própria convenção de visibilidade; a série não deve ser realinhada silenciosamente ao cruzamento de 0°.
- `Ang-diam` é diâmetro completo, em segundos de arco; dividir por 7200 para obter o raio em graus.
- `delta` é distância aparente em km com correção de tempo de luz. `S-T-O` aproxima o ângulo de fase, com pequena diferença descrita no original.
- Os horários mantêm a escala UT e os rótulos retornados; nenhum fuso local foi somado.
- Estes dados **não medem** cor, aerossóis, blur, aura, exposição, luminância ou tamanho percebido. Esses fenômenos exigem os modelos atmosféricos/ópticos do estudo.

Validação: 25 linhas em cada série curta, campos numéricos válidos, elevação crescente, intervalo solicitado de 5 min e janela de 120 min; cabeçalhos originais confirmam o observador e `NO (AIRLESS)`. Metadados, unidades e hashes estão em `horizons-reference-metadata.json`.
