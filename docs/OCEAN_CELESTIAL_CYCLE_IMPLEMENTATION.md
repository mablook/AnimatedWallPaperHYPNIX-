# Oceano — ciclo contínuo do Sol e da Lua

Implementação iniciada em 27/09/2026, validação concluída em 28/09/2026. Estudo preservado antes da implementação em **`8786419`**; renderer anteriormente aprovado em **`9d270a6`**. Referência: [estudo e dados astronômicos](OCEAN_CELESTIAL_CYCLE_STUDY.md).

## Resultado

A prévia nativa agora pode animar Sol e Lua simultaneamente, com trajetórias calculadas offline, tamanho por distância, refração, cor por transmissão atmosférica e transição contínua da iluminação. O campo de ondas, deslocamentos e normais aprovados continua igual para o mesmo instante da água.

O controle **Ciclo do céu** ativa a evolução; desativá-lo permite comparar os presets anteriores. **Velocidade** continua controlando as ondas. O tempo celeste tem controles próprios: tempo real, dia em duas horas, uma hora, vinte minutos ou dez minutos. Pausa congela os dois relógios; redimensionar ou recriar a GPU não reinicia nenhum deles.

Os controles de data/hora são explicitamente **UTC**; **Agora UTC** sincroniza a seleção com o relógio do computador. Latitude e longitude começam no observador de exemplo do estudo, 38,72° N / 9,14° O; não são geolocalização automática. **Ir ao horizonte** procura o próximo nascer/pôr do astro selecionado a partir da data escolhida. No nascer avança três minutos para facilitar a inspeção do disco; no pôr recua doze minutos para mostrar a aproximação. O motor trata a ausência de evento dentro da janela de busca.

## Trajetória, fase e escala

Dependência: **CosineKitty.AstronomyEngine 2.1.19**, licença MIT, incluída com versão fixa no projeto. O aplicativo não consulta serviços durante a animação. A licença acompanha os assets em `Assets/Effects/Ocean/ASTRONOMY-LICENSE.txt`. [Projeto original](https://github.com/cosinekitty/astronomy).

`OceanCelestialModel` transforma vetores equatoriais topocêntricos em coordenadas locais. A paralaxe já está incluída. O raio angular vem da distância; a Lua não copia a posição do Sol. A orientação do mapa usa polo e meridiano da Lua e a direção do observador; o terminador acompanha a direção solar vista da Lua. A iluminação local usa o material Lommel–Seeliger/Lambert existente, normalizado por sua integral analítica e pela curva de fase documentada no estudo.

Há duas composições:

- **Vista geográfica:** azimute real em relação à direção da câmera. Um enquadramento fixo não contém necessariamente nascente e poente.
- **Enquadramento cinema:** mantém as elevações astronômicas, mas comprime e reposiciona os azimutes no campo de visão. É uma escolha artística explícita para o wallpaper, não uma projeção geográfica. As posições relativas na imagem podem diferir do céu real.

O campo de visão vertical continua em 42°. Astros altos podem sair pelo topo do enquadramento enquanto continuam iluminando a água e as nuvens. O controle de ampliação no horizonte mantém a curva aprovada de 3,2×→1× até 25°. Essa ampliação não aumenta a potência da fonte nem seu raio físico usado na reflexão.

## Atmosfera, cor e horizonte

Uma LUT esférica de **256×64 RGBA32F**, construída uma vez na CPU e reutilizada, armazena profundidades ópticas de Rayleigh, aerossóis e ozônio. Os coeficientes RGB da base são mantidos. Perfis **Ar limpo**, **Ar marítimo** e **Névoa** variam a extinção dos aerossóis; são perfis de renderização, não meteorologia medida.

A LUT substitui a integração secundária aninhada durante as reconstruções do céu. Cada amostra consulta transmissão para sua própria posição e direção. Raios bloqueados pela Terra são opacos; vizinhos opacos da LUT não podem interpolar para uma fonte artificialmente transparente.

No ciclo, o estado das fontes contém energia anterior à atmosfera. A compensação inversa e o RGB lunar fixo dos presets anteriores não são usados. A transmissão aparece uma vez em cada trajeto correspondente. Céu, nuvens, discos e água usam os mesmos perfis, mas não recebem obrigatoriamente a mesma cor.

A refração usa o modelo contínuo da biblioteca astronômica. A diferença entre desvio da borda superior/inferior fornece a compressão vertical do disco e da textura. A deformação é uma aproximação elíptica local, não um traçado completo de raios refratados. O disco é recortado progressivamente no horizonte do renderer. O mar local usa horizonte de plano, enquanto a busca astronômica de eventos usa suas próprias convenções; não prometemos contacto observado ao segundo.

O céu exclui os discos. A água integra Sol e Lua como fontes extensas em um termo separado, evitando dupla contagem de energia. A fonte física usa oito amostras por disco e refração/ocultação/transmissão por direção. A auréola atmosférica é produzida pelo espalhamento frontal; bloom permanece óptico e é aplicado depois à imagem, sem voltar para o ambiente refletido.

## Exposição e resposta de exibição

A cena continua em RGB linear HDR antes de exposição e conversão sRGB. A relação aproximada entre fluxo solar e lunar foi incorporada, mas a escala absoluta ainda é a escala interna do renderer, não watts ou lux calibrados.

A exposição fotográfica varia suavemente com a depressão solar, limitada entre 1,1 e 16.000. A curva foi ajustada após capturas demonstrarem que uma adaptação rápida demais fazia o crepúsculo parecer dia. Não há adaptação dependente de histórico de frames: pausas, buscas e capturas do mesmo instante são determinísticas.

O disco solar usa escurecimento simples de bordo. Sol e Lua têm compressão fotográfica local de altas luzes para conservar cor e detalhe na saída SDR. Essa compressão afeta apenas a imagem do disco; a energia de iluminação da cena continua separada. É uma decisão de apresentação, não uma simulação radiométrica integral de uma câmera. O contraste lunar opcional aprovado permanece aplicado à textura visível.

## Atualização e recursos

`OceanCelestialClock` é propriedade da janela/saída, fora dos recursos GPU. O relógio da água permanece independente. Alterar velocidade integra o intervalo anterior antes de trocar a taxa; pausa não acumula tempo a recuperar.

`OceanCycleSky` conserva dois snapshots de radiância e interpola em RGB linear. Cada snapshot avalia as efemérides no seu próprio instante; um salto de horário gera no máximo os dois snapshots necessários. O horizonte e os discos atualizam por frame.

| Qualidade | Céu do ciclo | Integração de visão | Cadência de céu em reprodução contínua |
| --- | --- | --- | --- |
| Leve | 512×128 | 16 passos | Até 1 Hz |
| Equilibrado | 1024×256 | 32 passos | Até 2 Hz |
| Alto | 2048×512 | 32 passos | Até 2 Hz |

O intervalo astronômico mínimo é um segundo; em tempo real isso pode reduzir a cadência efetiva do céu a 1 Hz. Em reprodução acelerada, o intervalo cresce com a velocidade para limitar custo. O disco e a fonte direta permanecem contínuos. O limite é temporal, não um controle adaptativo completo de erro angular/radiométrico.

As nuvens conservam o volume, vento e níveis de qualidade anteriores. Sua iluminação avalia os dois astros no instante de cada snapshot de vento. Fontes abaixo do horizonte local podem continuar iluminando partes altas da atmosfera/nuvens quando a geometria permite. O preenchimento de espalhamento múltiplo permanece uma aproximação contínua e limitada.

As efemérides são calculadas na CPU; esta primeira implementação não adiciona uma tabela orbital interpolada. A LUT óptica é compartilhada por renderer no lado CPU e enviada uma vez a cada dispositivo. Nenhuma integração atmosférica volumétrica foi acrescentada por píxel da água a cada frame; a água consulta a LUT para suas amostras de luz direta.

## Validação

Resultados locais: `artifacts/ocean-cycle/`.

- **408 testes unitários aprovados**, incluindo dez casos novos: 50 posições/diâmetros/distâncias comparadas com os CSVs NASA/JPL, fase integrada independentemente, refração monotônica, profundidade óptica nas fronteiras, transmissão, relógios e estados em diferentes latitudes.
- Metas contra JPL atendidas nas séries do estudo: erro de azimute/elevação ≤0,005° no Sol e ≤0,01° na Lua; diâmetro dentro de 1,5″ e distância relativa dentro de 0,02%. Isso valida esses casos, não certifica todo o período selecionável ou meteorologia real.
- Matriz nativa com primeiro contacto, Sol/Lua a diferentes elevações, meio-dia, pôr e crescente; capturas em três qualidades e 4K.
- Campos espectrais comparados por hash em todas as cenas: preservados. Pausa, busca de instante e recriação de GPU comparadas por igualdade exata da imagem.
- Regressão nativa anterior: 24 cenas, IFFT comparada à DFT direta, HDR finito, caches, apresentações GDI/DXGI e 4K aprovados.
- Janela interativa: 132 frames apresentados; pausa, salto ao nascer lunar, resize, comparadores, taxas independentes do céu/ondas, composição geográfica/cinema, perfil de ar, captura e retorno ao ciclo aprovados; encerramento sem erro. O teste encontrou e corrigiu a ordem de inicialização do campo de longitude negativa antes da abertura final.

As medições do ciclo acelerado estão registradas em `benchmark/benchmark.json`, incluindo frames de reconstrução de céu e nuvens, apresentação GDI e consultas GPU. Protocolo: 30 segundos de aquecimento e três repetições de 120 segundos em **RTX 5070 Ti, 1920×1080 Equilibrado, alvo 30 FPS, ciclo 144×**.

| Repetição | FPS | GPU p95 | GPU máximo com atualização de cache | Frame com GDI p95 | Intervalo p95 | CPU / máquina |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 29,678 | 5,723 ms | 12,207 ms | 10,031 ms | 34,041 ms | 0,731% |
| 2 | 29,666 | 5,679 ms | 15,800 ms | 9,872 ms | 34,068 ms | 0,748% |
| 3 | 29,685 | 6,081 ms | 13,691 ms | 10,192 ms | 34,035 ms | 0,733% |

As três repetições passaram as metas de ≥28 FPS e intervalo p95 ≤40 ms. Foram medidos **10.686 frames**, sem perder consultas GPU, com **1.440 frames de atualização de cache**. O campo histórico `cloudUpdateSamples` do JSON conta nesta execução atualizações de céu **ou** nuvens. Primeira apresentação fria: 3.106 ms, incluindo compilação/preparação. Texturas lógicas estimadas: 125,8 MiB; não é medição de toda a VRAM do processo.

O ensaio atravessou aproximadamente 07:47–22:11 UTC da data de referência, incluindo o pôr solar e o nascer lunar. Mede renderização/cópia GPU→CPU/GDI para uma janela oculta; não mede custo do compositor visível, temperatura ou energia. A correção posterior da invalidação do cache ao mudar a velocidade das ondas não altera esse percurso de velocidade fixa; é coberta novamente pelos testes de janela e cenas.

## Reproduzir

### Ajuste de navegação da prévia — 28/09/2026

Os botões **Ver Sol** e **Ver Lua**, no início da barra, levam ao instante de 20 minutos após o próximo nascer do astro a partir da data selecionada. Ativam o ciclo, o céu refinado, a composição cinema e a vista com horizonte. A fase lunar permanece calculada para esse instante; o estado das ondas e o nível de qualidade são preservados. Em locais/datas sem o evento nos próximos dois dias, a prévia informa a ausência do nascer. Os presets fixos continuam disponíveis com o ciclo desligado.

A opção de lançamento `--show-ocean --ocean-moon` usa o mesmo atalho **Ver Lua**. Compilação Release aprovada sem avisos ou erros; `--ocean-window-check` aprovado com 122 frames, incluindo ambos os botões e a reativação do ciclo pelo atalho lunar. A janela visível foi inspecionada: Lua texturizada sobre o horizonte, oceano animado e indicador de 30 FPS. O usuário aprovou visualmente esse estado.

Artefatos locais desta verificação: `artifacts/ocean-preview/moon-controls/`. O executável deve ser iniciado na sessão interativa do desktop: um processo ativo no ambiente isolado de execução não comprova que a janela esteja visível ao usuário.

```powershell
dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release -p:OutDir=D:/desktopapp/AnimatedWallPaper/artifacts/ocean-cycle/bin/ -p:UseSharedCompilation=false
dotnet artifacts/ocean-cycle/bin/Hypnix.NativeSmoke.dll artifacts/ocean-cycle/captures --ocean-cycle
dotnet artifacts/ocean-cycle/bin/Hypnix.NativeSmoke.dll artifacts/ocean-cycle/legacy-regression --ocean-only
dotnet artifacts/ocean-cycle/bin/Hypnix.NativeSmoke.dll artifacts/ocean-cycle/benchmark --ocean-cycle-benchmark
./scripts/show-ocean-preview.ps1
```

`--ocean-window-check` executa a janela interativa com seu roteiro automático. A prévia continua isolada: não instala pacote, não substitui o wallpaper em uso nem altera preferências da aplicação instalada.

## Limites que permanecem explícitos

- Atmosfera RGB aproximada; não foi incorporada a referência espectral de 15–50 comprimentos de onda proposta no estudo. Cores ainda requerem calibração fotográfica/espectral para um perfil meteorológico específico.
- Espalhamento múltiplo por preenchimento aproximado; sem modelo completo de transporte radiativo, miragens, eclipse, corona por difração ou halo de gelo.
- Refração diferencial elíptica e caminho de extinção retilíneo. Não há solução acoplada completa de refração e transferência radiativa.
- Fontes/reflexos usam representação angular distante. Não foram acrescentadas sombras posicionais de nuvens sobre cada ponto do oceano ou paralaxe volumétrica de câmera em deslocamento.
- Ampliação de horizonte, composição cinema, exposição e compressão local dos discos são escolhas artísticas identificadas. Desativar a ampliação permite comparar a escala física.
- Desempenho medido nesta GPU/resolução não equivale a certificação energética, teste térmico prolongado ou validação em outros adaptadores.
