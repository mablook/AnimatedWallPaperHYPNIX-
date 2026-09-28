# Oceano vivo — experiência e controles no HYPNIX

28/09/2026 · Proposta de produto, ainda não implementada. Base técnica inspecionada: `56db826`, branch `codex/ocean-and-day-cycle`.

## 1. Decisão recomendada

Apresentar **Oceano vivo** como um ambiente personalizável, com página própria dentro do HYPNIX: paisagem grande, escolhas visuais e poucos controles de cada vez. O usuário escolhe o que deseja ver, observa a prévia e aplica ao monitor indicado.

A página mantém tipografia, cores, botões e navegação do HYPNIX. A diferença está na organização: momentos do céu e ambiente ocupam o lugar dos parâmetros de áudio e da barra técnica atual. Este trabalho especifica a interface e sua integração; não altera ondas, iluminação, shaders, movimento das nuvens ou o aplicativo em execução.

**Primeira utilização sugerida:** amanhecer, passagem do dia em 1 hora, mar moderado, nuvens em grupos a 25%, vento moderado, névoa marítima e qualidade Equilibrada. É uma configuração inicial de produto, não uma leitura do clima local. Reabrir sempre restaura a escolha daquele monitor, sem voltar aos padrões.

## 2. Entrada pela biblioteca

- Um único item **Oceano vivo**, com miniatura capturada do renderer. Descrição: **“Mar, céu e passagem do dia em tempo real”**. Identificação discreta: **“Ambiente 3D”**.
- Selecionar continua sem alterar o desktop. A ação contextual **“Personalizar oceano”** abre sua página no espaço principal da janela. “Prévia” leva à mesma experiência, com a paisagem em destaque; não abre outra ferramenta técnica.
- Manter **“Aplicar wallpaper”** na biblioteca para quem quiser usar imediatamente os padrões ou a última configuração. A personalização não é uma etapa obrigatória.
- Na página, **“Voltar à biblioteca”** fica no topo, junto do nome. O destino permanece explícito: **“Monitor 1 · principal”**, usando os monitores reais do HYPNIX.
- Sol, Lua, pôr do sol e nuvens são possibilidades do mesmo wallpaper; não criar entradas duplicadas na galeria para cada combinação.

## 3. Organização da página

Layout amplo: paisagem à esquerda, controles à direita; faixa de momentos imediatamente abaixo da paisagem. Barra inferior separada com destino, estado da prévia e ação Aplicar. Não desenhar botões por cima do HWND da prévia.

```text
HYPNIX       Voltar à biblioteca                  Monitor 1 · principal

Oceano vivo                                      Seu oceano
Mar, céu e passagem do dia                       Mar
                                                Calmo | Moderado | Agitado
┌───────────────────────────────────────┐
│                                       │        Nuvens                25%
│             PRÉVIA DO MAR             │        Nenhuma ──●──── Muitas
│                                       │        Formato: Em grupos
└───────────────────────────────────────┘
Pausar prévia                                    Mais ajustes ▸

Momento / Começar em
Amanhecer | Dia | Pôr do sol | Luar
Tempo: Manter este momento | Passagem do dia
Um dia passa em: 1 hora

Alterações só na prévia       Descartar alterações    Aplicar no monitor 1
```

A paisagem ocupa aproximadamente 60–65% da largura útil em janelas amplas. Painel de controles com 300–360 DIPs, conforme o espaço. Abaixo de aproximadamente 960 DIPs úteis, organizar uma coluna: prévia, momento/tempo, ambiente e aplicação. Os limites são decisões iniciais de layout, a validar com texto ampliado; não tamanhos fixos de tela física.

Na janela mínima de 640 × 520 DIPs, reduzir a área da prévia e permitir rolagem do conteúdo, mantendo o destino e a aplicação acessíveis. Não reduzir fontes para caber. Na largura padrão de 1280 × 820, as escolhas principais devem caber sem abrir detalhes. Em retrato/ultrawide, a prévia preserva a proporção do monitor selecionado; não esticar nem recortar a composição silenciosamente.

## 4. Controles principais e nomes

| Controle | Apresentação | Significado e contrato |
| --- | --- | --- |
| Momento / Começar em | Quatro opções com miniaturas: **Amanhecer, Dia, Pôr do sol, Luar** | Altera apenas o instante/composição do céu. Mantém mar, nuvens, vento, qualidade e monitor. Em passagem do dia, o rótulo é “Começar em” e a animação celestial continua a partir desse ponto. |
| Tempo | Duas opções visíveis: **Manter este momento** / **Passagem do dia** | O primeiro mantém a iluminação naquele instante, com água e nuvens animadas. O segundo permite ao Sol e à Lua seguir sua trajetória. Texto auxiliar muda conforme a escolha. |
| Um dia passa em | Lista: **10 minutos, 20 minutos, 1 hora, 2 horas** | Só aparece em Passagem do dia. Padrão: 1 hora. São 24 horas simuladas nesse tempo; não modifica a velocidade das ondas/nuvens. |
| Mar | **Calmo / Moderado / Agitado** | Mapeia para Agitation 0 / 0,65 / 1. Não implica simulação de tempestade, rebentação ou mudança do vento. |
| Nuvens | Slider **0–100%**, passos de 1, extremos **Nenhuma / Muitas** | É a quantidade configurada no céu procedural; não porcentagem medida da imagem. Ajuda: “A quantidade visível também depende do formato e do enquadramento.” Sem associar automaticamente à chuva. |
| Formato das nuvens | **Em grupos / Volumosas / Em camada** | Estratocúmulos / cúmulos / estratos. Nomes científicos apenas na ajuda. Com quantidade zero: manter a última escolha e desabilitar com “Aumente a quantidade para ver as nuvens”. |

As quatro miniaturas representam **momentos do céu**, não presets de todos os ajustes. Não usar “Cena personalizada” ao mexer apenas no mar. Se posteriormente existirem presets completos, chamá-los **“Combinações prontas”** e avisar quais ajustes substituem.

Em Passagem do dia, o cartão selecionado indica o ponto de partida. Quando o tempo avança, não identificá-lo como o estado atual da paisagem: manter “Começar em” e, quando necessário, uma descrição atual separada. Ao ativar Manter depois que o céu avançou, congelar exatamente esse instante e mostrar **“Momento atual”**, sem cartão ativo. Selecionar explicitamente um cartão volta a definir um dos quatro momentos curados. Não retornar ao amanhecer só para fazer o rótulo coincidir.

**Luar deve ser selecionável em um clique.** O preset usa data, local e instante curados com céu noturno e Lua suficientemente iluminada, em elevação adequada. A fase, posição e textura continuam coerentes com essa configuração. Não depender da Lua estar visível hoje, nem desenhar uma Lua extra. No modo Manter, preserva esse instante; no modo Passagem, continua o calendário. A visibilidade é validada com horizonte e céu aberto: nuvens densas e a vista Só o mar podem ocultar o astro. Preservar esses ajustes e explicar a condição, sem desenhar a Lua sobre as nuvens ou mudar a câmera automaticamente. Não garantir que o astro continue visível durante todo o ciclo.

Ao trocar um momento, preservar a fase espacial das ondas e o estado do vento/nuvens. A iluminação pode mudar para o novo instante; apresentar a nova prévia completa, evitando imagem parcial com reflexo antigo. Interpolação cinematográfica entre instantes é acabamento posterior, não requisito para liberar os controles.

## 5. Mais ajustes: abrir só quando necessário

Um expansor **“Mais ajustes”**, com grupos simples e sem expansores aninhados. Cada mudança mostra efeito na prévia. Não reunir tudo sob o nome “Avançado”, que não ajuda a encontrar uma opção.

| Grupo | Controle e escolhas | Mapeamento / limite |
| --- | --- | --- |
| Ambiente | **Vento: Sem vento / Moderado / Forte** | 0 / 9 / 18 m/s. Afeta deslocamento das nuvens e comportamento existente da neblina. Não altera o mar automaticamente. “As nuvens mudam de forma lentamente, mesmo sem vento.” |
| Ambiente | **Neblina: Sem neblina / Névoa marítima / Névoa baixa / Bancos de neblina** | OceanFog.Clear / Maritime / LowMist / Banks. “Sem neblina” retira as gotículas adicionais; a atmosfera continua existindo. |
| Enquadramento | **Vista: Com horizonte / Só o mar** | OceanSettings.Horizon. Céu e astros continuam iluminando a água quando estão fora do enquadramento. |
| Enquadramento | **Movimento das ondas: Lento / Natural / Rápido** | Speed 0,5 / 1 / 1,5. Ajuda: “Muda apenas o ritmo da água.” |
| Acabamento | **Brilho suave das luzes**, ligado/desligado | Bloom. Não altera potência física da Lua/Sol nem a posição dos reflexos. |
| Acabamento | **Destacar astros no horizonte**, ligado/desligado | HorizonMagnification. Descrever como efeito visual, não aumento físico dos astros. |
| Desempenho | **Qualidade: Econômica / Equilibrada — recomendada / Alta** | OceanQuality. Mostrar “Menor consumo” / “Equilíbrio entre detalhe e consumo” / “Mais detalhe; exige mais da GPU”. Não prometer FPS nem resolução de saída. |

Usar “Económica” se o locale for pt-PT e “Econômica” em pt-BR. Todo texto pertence a recursos de localização. Hoje o shell tem rótulos em inglês; implementar a experiência no idioma ativo, sem deixar metade da página em português e metade em inglês.

Na seção Desempenho, exibir o **limite global do HYPNIX** como informação, por exemplo “Limite do HYPNIX: 30 FPS”, e um link para as preferências gerais. Não criar outro limite conflitante. Pausa na bateria, apps em tela cheia e início com Windows continuam nas preferências do aplicativo.

O slider contínuo de quantidade amplia os pontos disponíveis na janela técnica atual (0/25/48/75/95); o renderer já aceita 0–1. Essa interface só será entregue após validar valores intermediários e 100%, incluindo o custo em céu fechado. Os 25/48/75/95 atuais continuam representáveis.

## 6. O que não entra na tela comum

| Parâmetro atual | Decisão de produto |
| --- | --- |
| Spectral/Analytic, céu antigo/refinado, atmosfera/volume on/off | Configuração interna de renderer. Manter o caminho aprovado; comparações ficam na ferramenta técnica. |
| UTC, latitude, longitude, azimute, salto de 1 h, seek de eventos | Fora da primeira versão de controles. Usar dados curados nos momentos; reservar essas ferramentas para estudo astronômico e futuros modos. |
| Cinematic/Geographic | Usar composição Cinematic na experiência inicial. A correspondência ao céu real exige um modo explicitamente diferente. Não chamar o padrão de “céu local”. |
| OceanAir | Padrão marítimo validado. Não duplicar com Neblina na interface principal. Uma futura opção “Visibilidade do ar” exige comparação que torne clara a diferença. |
| Lua cheia/gibosa legado | Não expor como override durante o ciclo astronômico. O material e a fase vêm do instante selecionado. |
| Seed, tamanho de volumes, amostras, escalas de nuvem, coeficientes de deformação | Internos; o usuário escolhe aparência e movimento. |
| FPS medido, CPU, GPU e tempos de passes | Diagnóstico opcional, fora da personalização comum. Não confundir limite de FPS com resultado medido. |

Não oferecer controles de chuva, áudio ambiente, tempestades, direção/altitude das nuvens ou exposição independente: são capacidades novas, não simples nomes para os parâmetros existentes.

## 7. Contrato dos relógios

**Manter este momento é um novo comportamento de integração**, e não equivale a desligar o ciclo antigo. O renderer deve continuar recebendo Celestial e Weather válidos; o controlador congela somente OceanCelestialClock. TimeScale normaliza no mínimo 1: não usar taxa zero como atalho. O switch atual de ciclo desligado retira Weather na janela técnica e perderia as nuvens volumétricas.

| Ação | Céu | Água e nuvens |
| --- | --- | --- |
| Manter este momento | Congela o instante atual | Continuam no ritmo próprio |
| Ativar Passagem do dia | Retoma desse instante, na duração escolhida | Mantêm continuidade |
| Mudar duração | Integra o tempo anterior antes de aplicar a nova taxa | Não acelera nem reinicia |
| Escolher Amanhecer/Dia/Pôr do sol/Luar | Seleciona instante curado; mantém o modo Tempo | Mantêm continuidade |
| Pausar prévia | Congela a prévia inteira | Congela a prévia inteira; desktop não é pausado por esse botão |
| Aplicar | Publica configuração e um snapshot temporal coerente | A sessão de destino parte desse snapshot, sem reset gratuito |
| Pausa global / bateria / monitor coberto | Respeita política do HYPNIX | Respeita a mesma política |

**Passagem do dia não é um vídeo em loop.** O calendário continua avançando, inclusive fase lunar e variações entre dias. “1 hora” significa taxa 24×; 2 horas = 12×; 20 minutos = 72×; 10 minutos = 144×. Não rebobinar à meia-noite: isso faria a Lua e as nuvens saltarem. Hora e data simuladas podem aparecer em um detalhe informativo, sem um editor de UTC na tela principal.

Nas pausas por política/suspensão, a primeira versão conserva o tempo simulado, sem recuperar horas perdidas ao voltar. A restauração do aplicativo guarda um checkpoint limitado de instante celestial e tempo ativo; não salva a cada frame. A trajetória meteorológica completa não precisa ser serializada indefinidamente: a especificação de sessão deve definir checkpoint compacto de deslocamento/velocidade e limitar histórico. Continuidade entre frames e recriação de GPU é obrigatória; continuidade bit a bit após reinício do processo não é prometida nesta primeira entrega.

### Evolução posterior: acompanhar horário real

Proposta separada, não opção desabilitada ocupando espaço na primeira versão. **“Acompanhar horário local”** exige cidade escolhida pelo usuário, coordenadas e fuso horário correto (incluindo horário de verão). O relógio do sistema sozinho não fornece posição geográfica. Não inferir que a pessoa está em Lisboa pelos padrões atuais.

Este modo lê a hora real, recupera-a após suspensão e usa composição geográfica; Sol/Lua podem estar fora de quadro ou abaixo do horizonte. Mostrar a situação e oferecer mudar de modo, sem adulterar a astronomia. Escolher “Luar” neste modo precisa de uma transição explícita para um momento escolhido. Nenhuma permissão de localização ou serviço de previsão do tempo é necessária para a versão inicial.

## 8. Prévia, rascunho, aplicação e monitores

O oceano terá **edição por rascunho**. Toda alteração atualiza sua prévia; somente **“Aplicar no monitor N”** altera o desktop. Texto permanente: **“Alterações só na prévia até aplicar.”** Esta é uma mudança deliberada em relação aos visualizadores atuais, cujas preferências podem atualizar imediatamente o wallpaper ativo.

Estados separados: configuração aplicada, rascunho do editor e snapshot de execução. O rascunho é identificado por wallpaper + DeviceId, nunca pelo índice visual “monitor 1”. Alterar a prévia não chama o caminho de salvamento/aplicação imediata dos visualizadores.

- **Descartar alterações:** restaura o rascunho à configuração aplicada daquele monitor; se ainda não há oceano aplicado, restaura o ponto de partida ao abrir. Desabilitar quando não há alterações.
- **Voltar ou trocar de monitor:** manter rascunho em memória, isolado por destino, sem diálogo repetitivo. Ao reabrir, indicar “Alterações não aplicadas”. Fechar o aplicativo descarta esses rascunhos; o texto de ajuda informa isso. Não persistir rascunho como se fosse preferência aplicada.
- **Aplicar:** preparar recursos e primeiro frame; só depois substituir a sessão e persistir. Botão mostra “Aplicando…”. Se o usuário continuar editando, a aplicação usa uma revisão imutável; o rascunho mais recente continua marcado como não aplicado.
- **Falha:** manter wallpaper anterior e rascunho. Mensagem: “Não foi possível aplicar o oceano. Seu wallpaper anterior foi mantido.” Ação “Tentar novamente”; detalhes técnicos recolhidos.
- **Aplicar em todos os monitores:** ação secundária explícita, preservando o padrão do HYPNIX. Copia o rascunho atual e a mesma âncora temporal para cada destino. Não significa panorama contínuo nem sincronização de frames. Resultados e falhas são reportados por monitor.
- **Monitor desconectado:** conservar configuração aplicada por DeviceId; impedir aplicação à saída ausente. Rascunho não pode migrar sozinho para outro monitor. Após pausas independentes, monitores podem divergir no tempo simulado; não rotular como “sincronizados”.
- **Pausar prévia:** estado local não vira preferência do desktop ao aplicar. Pausas globais continuam com seus motivos reais. Não usar apenas “Pausado” sem explicar quem causou a pausa.

## 9. Carregamento e sensação de resposta

A última medição de inicialização a frio registrou cerca de 19 s, enquanto o host genérico expira em 15 s. Antes da integração, alinhar cancelamento e preparação ao orçamento do oceano (a prova atual permite 45 s). Não assumir abertura instantânea ou progresso percentual conhecido. Ver [evidências da implementação das nuvens](OCEAN_CLOUD_MOTION_IMPLEMENTATION.md).

Mostrar miniatura/última imagem com **“Preparando o oceano…”**, indicador indeterminado e possibilidade de voltar. A interface permanece responsiva. Não substituir o desktop por preto durante preparação; não afirmar “Ao vivo” sobre imagem estática de fallback. Se a preparação falhar: estado visível, última imagem identificada como tal e “Tentar novamente”.

Movimentos de sliders devem atualizar rótulos imediatamente e agrupar trabalho caro, publicando apenas a revisão mais recente. Troca de qualidade ou dimensão não reinicia os relógios. Parâmetros compatíveis atualizam sem recriar o renderer. A troca de formato/quantidade precisa de nova prévia coerente, sem caches de sombras e reflexos de revisões diferentes.

O vento atual responde em até 1 s e transiciona por 3 s: não mascarar isso com um controle que pareça quebrado, nem reiniciar posição para responder instantaneamente. Explicação curta disponível na ajuda: **“O vento muda gradualmente.”**

Não abrir renderizadores animados em cada miniatura. Uma única prévia ativa por editor; suspender a prévia duplicada da biblioteca. Orçamento agregado deve considerar desktop + editor + monitores, especialmente ao selecionar Alta. Não reduzir qualidade silenciosamente: se houver adaptação automática no futuro, ela terá estado e contrato próprios.

## 10. Acessibilidade e linguagem

Usar controles WPF com nomes e valores expostos a UI Automation. As opções de mar e tempo devem funcionar como grupos de seleção exclusiva, com teclado e estado escolhido. Sliders têm rótulo, valor e extremos; setas alteram, Home/End chegam aos limites. Toda ação essencial é visível, sem depender de hover.

Contraste mínimo projetado de 4,5:1 para texto normal e 3:1 para texto grande; validar os controles também em alto contraste. Texto ampliado, escala Windows 100/150/200%, foco visível e ordem de tabulação devem ser testados. Anunciar aplicação concluída e erros; não narrar cada frame nem cada tick do céu. Diferenciar opção selecionada por texto/marca e estado acessível, além de cor.

Não usar termos como advecção, densidade volumétrica, bloom, azimute e TimeScale em rótulos públicos. A ajuda explica o resultado visual: “Muda apenas o ritmo da água”, “O céu fica neste momento; água e nuvens continuam em movimento”. Evitar “Automático” quando o comportamento não está definido.

Fundamentação: as [diretrizes de configurações do Windows](https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings) orientam agrupar escolhas, usar padrões úteis e revelar subopções quando necessárias. Aqui a edição responde imediatamente **na prévia**, com Aplicar destinado à publicação no desktop, preservando a separação já comunicada pela biblioteca. As recomendações de [sliders](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/slider) sustentam seu uso para quantidades relativas e feedback visível. Os [requisitos de texto acessível](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements) orientam contraste, nomes e escala. São referências de interação; não exigem migrar WPF para WinUI.

## 11. Plano técnico de integração futura

| Etapa | Entrega | Arquivos existentes a considerar |
| --- | --- | --- |
| 1. Capacidades e dados | Ocean como ambiente, preferências próprias versionadas/normalizadas, manifesto e miniatura real | `Services/WallpaperKind.cs`, `WallpaperCatalog.cs`, `AppSettingsStore.cs`, `WallpaperRequest.cs` |
| 2. Sessão do oceano | Relógios fora da GPU, snapshot tipado, pausa, cancelamento, resize, preparação do primeiro frame, sem captura de áudio | `WallpaperSession.cs`, `NativeWallpaperHost.cs`, `DisplayWallpaperController.cs`, `OceanGpuRenderer.cs`, `OceanCloudMotion.cs` |
| 3. Prévia e editor | Página WPF própria, rascunho por destino, única prévia offscreen, navegação e atualização agrupada | `MainWindow.xaml`, `MainWindow.xaml.cs`, `MainWindow.Preview.cs`, `MainWindow.Monitors.cs`, `WallpaperPreviewControl.cs`, `GpuPreviewSurface.cs` |
| 4. Momentos e tempo | Quatro instantes curados, congelamento celestial independente, passagem contínua de datas | `OceanCelestialModel.cs`, `OceanSettings.cs`, nova camada de preferências/editor |
| 5. Publicação e recuperação | Aplicar por monitor/em todos, manter sessão anterior em falha, persistência e restauração | `DisplayWallpaperController.cs`, `AppSettingsStore.cs`, testes de sessão/monitores |
| 6. Polimento e validação | Localização, teclado/leitor de tela, formatos/DPI, loading e consumo com editor aberto | Temas WPF, testes de layout/preview e smoke nativo |

Não basta adicionar Ocean ao enum: `IsVisualizer` hoje inclui quase todos os tipos e ativaria descrição de áudio, paletas e controles indevidos. Especificar capacidades explícitas como suporte a áudio e tipo de editor, sem renomear o oceano como VisualizerPreferences. A assinatura real de interfaces deve ser definida ao implementar, não adicionada parcialmente neste estudo.

`WallpaperPreviewControl` usa HwndHost; respeitar sua composição e o caminho offscreen existente. Manter controles fora da superfície nativa e medir o custo da apresentação GPU→CPU/GDI. A janela de testes permanece ferramenta técnica, sem virar a página pública por simples cópia da toolbar.

Revisar migração de configurações antigas: preferências de oceano ausentes usam padrões, enum/valores inválidos normalizam, atribuições de outros wallpapers e monitores desconectados permanecem intactas. Publicar a configuração aplicada somente depois do sucesso da sessão.

## 12. Critérios de aceitação

Estes são objetivos de validação, não resultados já medidos para a interface proposta.

| Cenário | Resultado necessário |
| --- | --- |
| Primeira utilização, sem explicar termos técnicos | Pessoa encontra Luar, deixa mar calmo e aplica ao monitor correto em até 1 minuto. Testar com 5 pessoas não envolvidas no desenvolvimento; buscar pelo menos 4 sucessos sem orientação. |
| Entendimento de Tempo | Pessoa explica que Manter este momento congela o céu, não o mar; diferencia passagem do dia da velocidade das ondas. |
| Ajustar só nuvens | Tipo e quantidade mudam sem alterar o mar, momento escolhido, qualidade ou destino. Valores 0/intermediários/100 funcionam. |
| Selecionar Luar | Noite e Lua iluminada em posição adequada na configuração curada; Lua visível com horizonte e céu aberto. Nuvens/câmera podem ocultá-la sem perder o preset. Textura e reflexo seguem o caminho aprovado; independe da data atual do computador. |
| Preview versus desktop | Mexer nos controles nunca altera a sessão aplicada até Aplicar, inclusive quando o oceano já está ativo. Descartar e trocar de monitor não vazam ajustes. |
| Falha/cancelamento durante preparação | UI responsiva, último wallpaper preservado, sem tela preta nem persistência de aplicação inexistente. |
| Editar durante aplicação | Resultado identifica a revisão aplicada; mudanças posteriores continuam no rascunho. |
| Relógios | Manter/Passagem/pause/resize/troca de qualidade não reiniciam ondas/nuvens; meia-noite não rebobina. |
| Múltiplos monitores | DeviceId correto, cópia explícita em todos, falhas parciais claras e desconexão tratada. |
| Acessibilidade | Completar o fluxo usando teclado; leitor de tela identifica nomes/valores/seleção; sem cortes a 200% e na janela mínima. |
| Desempenho | Medir abertura a frio/quente, frame times e consumo com desktop+editor; manter Equilibrada como padrão e nenhum renderer escondido ativo sem necessidade. |
| Regressão | Água, crateras lunares, trajetória dos reflexos, nuvens e neblina mantêm qualidade do renderer aprovado; visualizadores/vídeos continuam com sua semântica anterior. |

## 13. Ordem recomendada para seguir

Primeiro validar a organização e os nomes com o estudo de interface. Depois implementar **capacidades + sessão + editor com momentos, tempo, mar e nuvens**, incluindo os estados de aplicação desde o início. Completar Mais ajustes e acessibilidade na mesma entrega pública; somente então considerar horário local ou combinações salvas.

O estudo interativo que acompanha esta proposta usa capturas existentes do oceano para comparar organização e rótulos. Ele demonstra seleção de momentos, tempo, ajustes e rascunho; não está conectado ao renderer nem aplica wallpapers. Imagens são ilustrativas: alterar nuvens/mar ali atualiza o estado demonstrado, não recalcula a água.

Revisão desta entrega: duas auditorias independentes de código/contratos, referências oficiais e verificação dos links locais. O estudo passou por checagem estrutural de IDs, rótulos, imagens incorporadas e sintaxe JavaScript. A abertura automatizada do arquivo local no navegador foi bloqueada pela política de protocolos; portanto, layout e interações ainda exigem validação visual. Nenhum teste do renderer foi repetido porque esta entrega modifica somente documentação.
