# Dados de referência — ciclo do Sol e da Lua

Pesquisa de **27/09/2026**, associada ao [estudo técnico](../../../OCEAN_CELESTIAL_CYCLE_STUDY.md). Base do renderer examinada: `9d270a6`. Estes arquivos não alteram o aplicativo.

## Tipos de evidência

| Arquivo | Natureza | O que representa |
| --- | --- | --- |
| `horizons-*-rise-2h.csv` | Efemérides consultadas na NASA/JPL | Posição, diâmetro, distância e fase para um observador/data explícitos |
| `horizons-*-query.json` / `*-original.json` / `*-original.txt` | Proveniência | Consulta, URL, data de consulta, hash e resposta original |
| `airmass.csv` | Cálculo por fórmula publicada + exemplos escolhidos | Massa de ar e três exemplos escalares de transmissão |
| `refraction.csv` | Cálculo por aproximação publicada | Desvio angular e achatamento aproximado do disco solar |
| `first-hours.csv` | Geometria ilustrativa | Elevação solar em duas horas para três declinações constantes |
| `apparent-size.csv` | Reprodução da escolha artística existente | Ganho 3,2×→1× entre 0° e 25°, separado da escala física |
| `moon-phase.csv` | Curva empírica publicada, simplificada | Fração geométrica iluminada versus fluxo lunar relativo |
| `current-model-transmission.csv` | Diagnóstico do código existente | Transmissão RGB calculada com os coeficientes da base |
| `celestial-evolution.png` | Gráfico científico dos cálculos | Seis comparações; não é imagem gerada pelo renderer |

**Efemérides não são fotografias nem medições atmosféricas.** Nenhum desses dados determina sozinho RGB de monitor, cor exata do céu, aura, blur ou exposição. O observador a 38,72° N / 9,14° O é um exemplo; não foi inferido como localização do utilizador.

O guia específico das consultas está em [horizons-reference-readme.md](horizons-reference-readme.md). As amostras de nascer começam em marcadores de eventos aproximados, e não em contato exato certificado.

## Reprodução local

```powershell
# Na raiz do repositório; Python 3 + matplotlib disponível.
python docs/references/ocean/celestial-study/calculations.py
```

O script grava seis CSVs e a figura neste diretório. Usa `math` e `csv` da biblioteca padrão para os números, e Matplotlib somente para o gráfico. Não tem acesso à rede, não altera os dados Horizons nem executa o aplicativo. Validado neste ambiente com Python 3.14 e Matplotlib 3.10.9. Verificações aritméticas internas cobrem pontos de referência, domínios e invariantes; não certificam precisão meteorológica.

Os CSVs usam ponto decimal, vírgula como separador, UTF-8 e cabeçalhos com unidades. Casas adicionais servem à reprodução, não representam precisão física garantida. Reexecutar com outra versão de Matplotlib pode modificar bytes da figura sem alterar os dados.

## Definições e limites

### Massa de ar e transmissão ilustrativa

`X(h) = 1 / [sin(rad(h)) + 0.50572 × (h + 6.07995)^(-1.6364)]`, com `h` em graus **aparentes**, intervalo 0°–90°. A aproximação dá `X(90°) ≈ 0.999712`; normalizou-se cada transmissão por esse valor efetivo, sem assumir exatamente 1.

`T(h)/T(90°) = exp(−τ × [X(h)−X(90°)])`. Os valores **τ=0,1; 0,2; 0,4 foram escolhidos para ilustração escalar**, não ajustados a uma estação meteorológica ou a um comprimento de onda específico. Não usar essas colunas como RGB nem aplicar a mesma massa de ar indiscriminadamente a todos os constituintes atmosféricos. Fonte da massa de ar: [Kasten e Young, 1989](https://doi.org/10.1364/AO.28.004735); princípio de transmissão: [PBRT](https://pbr-book.org/4ed/Volume_Scattering/Transmittance).

### Refração

`R(h)` segue a aproximação por intervalos da [NOAA](https://gml.noaa.gov/grad/solcalc/calcdetails.html), com `h` em graus geométricos e retorno em graus:

```text
h > 85°: R = 0
h > 5°: R = (58.1/tan(h) − 0.07/tan(h)^3 + 0.000086/tan(h)^5) / 3600
h > −0.575°: R = (1735 − 518.2h + 103.4h² − 12.79h³ + 0.711h⁴) / 3600
demais: R = −20.774/tan(h) / 3600
```

No script, converter graus para radianos antes da tangente. `Dvertical = 2r + R(h+r) − R(h−r)`, com raio solar `r = 1919 / 3600 / 2` graus. O CSV amostra apenas centros a partir de 0°. A figura conserva as pequenas irregularidades da fórmula perto de 5° para não ocultar sua natureza aproximada. Não usar essa função por intervalos como solução pronta de primeiro contato ou como previsão sob inversão térmica. A largura horizontal foi tomada como referência sem deformação.

### Primeiras duas horas

Latitude de exemplo 38,72°, declinações constantes −23,44°/0°/+23,44°, sem avaliação de datas reais. Resolve-se o ângulo horário inicial para centro a −0,8333° e avança-se 0,25° por minuto. Isso ilustra sazonalidade; não substitui efeméride e não modela a Lua.

`sin(h) = sin(φ)sin(δ) + cos(φ)cos(δ)cos(H)`. Fonte geométrica: [USNO](https://aa.usno.navy.mil/faq/alt_az). As janelas JPL fornecem a referência astronômica consultada separadamente.

### Ampliação aparente

Reproduz `OceanLightingModel.ApparentRadius`: `t=clamp(h/25°,0,1)`; `gain=1+2.2×(1−t²(3−2t))`. O diâmetro solar ilustrativo multiplica o valor médio de 1.919″. O código atual usa raio `.00465` rad, numericamente muito próximo mas não idêntico. A coluna `physical_gain=1` isola a ampliação artística; não afirma distância orbital constante em todas as datas.

### Fase lunar

Fluxo relativo `10^(−0.4 × [0.026|α| + 4e−9 α⁴])`, α em graus, em comparação à extrapolação α=0 da equação de magnitudes. Fração iluminada geométrica `(1+cos(α))/2`. Fonte: [Krisciunas e Schaefer, eq. 9](https://articles.adsabs.harvard.edu/pdf/1991PASP..103.1033K).

Mantém distância fixa e exclui aumento de oposição, eclipses, libração e extinção atmosférica. A amostra 0° é referência matemática sem eclipse, não uma geometria observacional universal. A figura para em 150°; não pretende resolver crescentes extremos. Normalizar uma função local de material por essa energia exigirá evitar dupla contabilização da fase.

### Transmissão RGB da base

Reprodução em Python do integrador `OceanLightingModel.Transmittance`, incluindo 64 intervalos quadráticos, Terra de 6.360 km, topo a 6.460 km e origem a 2 m; coeficientes Rayleigh/Mie/ozônio da base. A câmera da cena e o observador JPL usam 2,8 m: são convenções diferentes explicitadas, não uma única simulação calibrada.

O caminho é retilíneo, sem refração. Os valores são RGB linear de transmissão, **não sRGB, hexadecimais ou cores espectrais medidas**. Propósito: demonstrar a variação que a compensação da iluminação lunar atual anula.

## Verificação realizada neste estudo

- Script executado; seis CSVs e figura gerados, verificações aritméticas aprovadas.
- Figura inspecionada visualmente: unidades, legendas, limites e distinção entre físico/artístico presentes.
- Séries JPL com 25 amostras cada, espaçamento de 300 s e duração de 7.200 s; consultas e respostas preservadas.
- Não foram executados testes do renderer, pois esta entrega modifica somente documentação e ferramentas de pesquisa.

Dados de referência e fórmulas têm limites diferentes. Em particular, não aplicar refração ou paralaxe duas vezes aos campos JPL; seguir as definições no guia de proveniência.
