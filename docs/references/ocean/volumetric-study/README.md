# Dados do estudo volumétrico

Calculados em 28/09/2026 para o [estudo de nuvens e neblina](../../../OCEAN_VOLUMETRIC_ENVIRONMENT_STUDY.md). São exemplos matemáticos próprios, não observações meteorológicas nem resultados de uma nova implementação.

## Visibilidade

`visibility.csv` usa ponto decimal e contém os quatro cenários em três distâncias. O coeficiente `current_fixed` vem de `Shaders/Ocean.hlsl` no commit `ed376a5`; representa somente a extinção do fog legado, sem sua parcela de luz adicionada.

Fórmulas para reproduzir cada linha, com distância em metros:

```text
sigma = 0.0007                         # cenário atual
sigma = -ln(0.05) / visibility_m      # demais cenários
transmittance = exp(-sigma * distance_m)
transmittance_percent = 100 * transmittance
equivalent_mor_m = -ln(0.05) / sigma
```

A definição de MOR com transmissão fotométrica de 5% está na [WMO-No. 8, 2008, cap. 9, equações 9.4–9.7](https://www.weather.gov/media/epz/mesonet/CWOP-WMO8.pdf#page=214). A tabela assume extinção homogênea neutra e não determina a cor ou luminância final. Transferir o controle para um meio RGB estratificado exige calibração da faixa, altura e direção de referência.

## Memória

Os exemplos do estudo usam bytes lógicos de textura, não VRAM residente medida: `160 * 90 * 48 * 8 / 1048576 = 5.2734375 MiB` para um volume RGBA16F; quatro volumes equivalem a `21.09375 MiB`, sem outros recursos.

Nenhum asset de nuvens, ruído ou fotografia de terceiros foi incorporado neste estudo.
