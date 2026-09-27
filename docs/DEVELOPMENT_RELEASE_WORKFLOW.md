# Builds de desenvolvimento e teste manual

A build instalada é a referência do teste. Um commit, um build local e um pacote instalado são estados diferentes.

1. Verificar `git status`, incluir todos os desenvolvimentos pretendidos e aumentar a versão ao substituir um pacote. Não reutilizar a mesma versão para payloads diferentes.
2. Compilar com o SDK indicado em `global.json` e executar a regressão local. Os testes E2E de desktop desta fase são feitos manualmente pelo proprietário.
3. Gerar o MSIX com `scripts/package-msix.ps1 -Version <versão> -SelfSign -OutputDir <pasta nova> -DevelopmentLogDirectory <pasta absoluta de logs>`.
4. Guardar `build-info.json`, que identifica versão, build, commit, alterações locais e SHA-256 dos ficheiros do working tree. Guardar também o pacote, hash e cópia das fontes utilizadas; alterações posteriores não pertencem à build já gerada.
5. Antes de desinstalar, preservar configurações, biblioteca e logs. Instalar o pacote completo e verificar a versão, o `build-info.json` e o hash de `HYPNIX.dll` efetivamente instalados.
6. Confirmar um log novo com `Application started`, versão, build e caminho do executável instalado. A janela About & updates mostra versão/build e a pasta de logs. Open diagnostics abre essa pasta.
7. Registar o resultado manual por build. Uma mitigação não encerra um defeito sem confirmação do cenário específico. Não apresentar a build como publicada na Store sem submissão/distribuição verificadas.

## Sessão 1.2.1 — 2026-09-27

Inclui todas as alterações locais de aplicação existentes: arranque com Windows e atualização manual, preparação assíncrona de wallpapers, recuperação de apresentação GDI, fontes de áudio e seleção de dispositivos. Acrescenta identificação inequívoca da build e logs por sessão.

A nova mitigação do sininho usa apresentação BitBlt nas prévias GPU embutidas; o desktop mantém FlipDiscard. O teste manual posterior reproduziu o defeito na 1.2.1, como registado abaixo. Fundamento técnico: [modelos de apresentação DXGI](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/for-best-performance--use-dxgi-flip-model). A promoção do flip model a DirectFlip/Independent Flip é uma hipótese para o sintoma; a sua causa ainda não foi demonstrada.

Logs locais de desenvolvimento: `D:\desktopapp\AnimatedWallPaper\artifacts\development-logs`. Cada arranque gera um ficheiro separado, sem limpeza automática durante esta fase. Não grava áudio nem chaves de licença. Estes logs devem ser revistos e arquivados após o teste.

A documentação do oceano é investigação/planeamento; não existe ainda um wallpaper de oceano implementado para incluir na build.

## Sessão 1.2.2 — 2026-09-27

Resultado manual da 1.2.1: o sininho continuou a alternar com a janela de configuração aberta. O log instalado confirmou `1.2.1-20260927-153229-9f59b9e5` e `Preview=True; SwapEffect=Discard`. O proprietário confirmou que **Hide preview estabiliza o sino**. A troca para BitBlt/DXGI não resolveu o defeito.

A candidata 1.2.2 remove a swap chain de janela das prévias GPU. Os shaders continuam a renderizar numa textura D3D11; uma textura staging reutilizável transfere o resultado para GDI na janela filha. O desktop continua a usar FlipDiscard. O log identifica `Preview=True; Presentation=OffscreenGdi; WindowSwapChain=False`. A relação exata com a heurística de Não incomodar do Windows permanece uma hipótese, não uma causa demonstrada.

Regressão: 380 testes passaram; build nativa sem avisos/erros; smoke gráfico local dos 13 wallpapers passou. A verificação nova compara todos os pixels RGB da GPU com a imagem GDI, em duas frames animadas a 213x121, nos renderizadores de shaders e Living Fire. Exercita padding de linhas, orientação vertical e cores, e confirma que as prévias podem ser criadas sem HWND/swap chain. Estes testes não validam o sino no desktop do utilizador.

Evidências: `artifacts/development-1.2.2` contém os logs de teste, capturas, cópia do log instalado 1.2.1 e pacote com identidade das fontes. Os logs da aplicação continuam em `artifacts/development-logs`.

Aceitação manual concluída em 2026-09-27: o proprietário confirmou “foi resolvida a do sino.” O defeito reportado está encerrado na 1.2.2. Para futuras regressões, manter a prévia visível na janela em primeiro plano, alternar wallpapers e redimensionar; comparar com Hide preview e com a aplicação oculta. A confirmação deste bug não representa uma matriz completa de hardware.

## Fecho e próxima entrega

O inventário consolidado e os identificadores verificáveis da 1.2.2 estão no [registo da versão](DEVELOPMENT_RELEASE_1.2.2.md). O commit de fecho inclui também os desenvolvimentos anteriores que estavam locais e a pesquisa/plano do oceano; estes documentos de planeamento não significam uma funcionalidade implementada.

Para a próxima entrega: rever `git status --short` (incluindo ficheiros novos), registar cada desenvolvimento e respetiva validação, fazer commit das fontes e gerar a build desse estado. Não assumir que a versão instalada inclui alterações locais. Se for preciso gerar um diagnóstico antes do commit, identificar explicitamente a árvore suja no manifesto e guardar o snapshot exato, como nesta sessão.

Antes do push, conferir todos os ficheiros staged e `git diff --cached --check`. Depois do push, verificar que o hash remoto coincide com HEAD e que `git status --porcelain` está vazio. Os artefactos ignorados são preservados: limpar o estado Git não significa apagar instaladores, snapshots, logs ou evidências. Nunca incluir a chave privada do certificado nem dados de licença nos commits.

A consolidação documental posterior não altera o MSIX já testado. Não reconstruir silenciosamente o mesmo número de versão para fazer o manifesto parecer limpo: uma nova build com payload diferente recebe nova versão e nova validação.