# Prévia de fogo 3D

Harness independente do estudo em `docs/FIRE_RENDERING_STUDY.md`.
Agora usa o mesmo `FireSimulation`/`FireGpuRenderer`/shader do **Living Fire** na
galeria do HYPNIX. Código original; sem código dos exemplos Shadertoy.

Estado atual: velocidade 4× a original (quatro subpassos físicos de 1/60 s por tick),
quantidade de fontes adaptada à proporção da janela e FFT separado por fonte, com
agudos à esquerda e graves à direita. As descrições da segunda/terceira versão abaixo
registram a evolução do estudo. Recursos de interface, presets e background ficam na
aplicação principal; consulte `docs/LIVING_FIRE.md` e `docs/VISUAL_SETTINGS_DESIGN_PLAN.md`.

```powershell
dotnet run --project Tests/Hypnix.FirePreview -c Release
```

Espaço pausa, R reinicia, F alterna a visibilidade das faíscas, Esc fecha.
A alterna áudio do sistema → demonstração → desligado. B alterna faixa/chama única.
A janela tem tamanho fixo de 1080 × 640.

Modelo reduzido, não uma simulação química validada: grade 288 × 128 × 40;
advecção semi-Lagrangiana com correção MacCormack limitada pelos oito vizinhos
para os campos materiais; combustível, calor, fuligem e reação separados;
empuxo, confinamento de vorticidade e 24 iterações de pressão por passo.
A disponibilidade de oxigênio é aproximada a partir do combustível local.
Subpasso físico fixo de 1/60 s, com agendamento por `FireFrameStepper`, sem acumular atrasos longos.
Renderização volumétrica com emissão e absorção, 72 amostras, sem bloom.
A cor e o detalhe subvoxel são aproximações artísticas.

Na segunda versão, a correção do transporte preserva bordas e curvas com menos
difusão. Até 64 brasas persistentes nascem em momentos diferentes, integram a
velocidade projetada do ar, sofrem arrasto e gravidade reduzida e esfriam até apagar.
O arrasto no ar limita o crescimento da velocidade durante execuções prolongadas.
Pequenos rastros se orientam pela velocidade. A visibilidade é atenuada pela fuligem
com uma aproximação de uma amostra; a composição aditiva ocorre após o tone mapping.
Não há bloom nem simulação completa de oxigênio.

Na terceira versão, nove focos com dimensões, profundidades e pulsos distintos
alimentam um único volume compartilhado. O fogo ocupa a base da janela e mantém
espaço escuro acima. B preserva a opção de avaliar uma chama isolada.

O áudio reutiliza o capturador WASAPI/FFT do HYPNIX, incluindo reconexão de saída.
Graves aumentam combustível e impulso na fonte; médios modulam o combustível;
agudos encurtam o intervalo entre brasas. Ataque de 12/s e liberação de 3/s são
integrados no passo fixo, com valores limitados a 0–1. A captura é do som reproduzido
pelo sistema e é analisada em memória. Não usa microfone nem cria gravações.
Após 500 ms sem dados, o alvo volta a zero; sem música permanece uma chama de repouso.
O modo demonstração usa pulsos sintéticos e é identificado no título da janela.

## Captura e verificações

```powershell
dotnet run --project Tests/Hypnix.FirePreview -c Release -- --capture artifacts/fire-preview
```

Produz uma imagem de repouso, 120 quadros a 30 fps com **sinal de demonstração**
e `checks.json`. Verifica valores finitos
e não negativos nos campos materiais, redução da divergência após projeção,
pausa sem mudança de pixels ou partículas, animação não estática, combustível ativo
e dissipação do calor após desligar a fonte. Verifica ainda nascimento, movimento,
visibilidade e extinção das faíscas; reprodução idêntica após reset; e estabilidade
dos campos após 30 segundos de reprodução (120 segundos simulados), com uma imagem adicional desse instante.
Compara simulações de mesma duração com áudio zero/forte: combustível e calor
precisam crescer, e depois recuperar no silêncio. Garante que áudio sem avanço
temporal não altera a imagem, rejeita entrada não finita, verifica ignição nas nove
regiões e a volta ao modo de chama única. Essas verificações usam sinais sintéticos;
a captura de uma música real requer som em reprodução no dispositivo padrão.
Divergência residual é medida, não presumida zero.
Tempo total da captura não é um benchmark isolado da GPU.

Esta prévia serve para avaliar movimento, cor e silhueta. Ainda há uma base
relativamente lisa e resolução limitada; não é aprovação de realismo fotográfico.
O produto está integrado na galeria. A renderização interna é limitada a
1080 × 720 por monitor, preservando a proporção, e usa estado separado por monitor.
Veja `docs/LIVING_FIRE.md` para controles, verificações de integração e limites das medições.
