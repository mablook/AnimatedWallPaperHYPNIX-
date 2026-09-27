# HYPNIX 1.2.2 — desenvolvimento consolidado

Data: 27 de setembro de 2026. Entrega local para instalação e E2E manuais pelo proprietário. A consolidação no Git não publica esta versão na Microsoft Store.

## Inventário incluído

- Prévia GPU offscreen apresentada por GDI, sem swap chain DXGI associada à janela; aplica-se aos shaders e Living Fire. O wallpaper no desktop mantém o seu caminho de apresentação.
- Regressão nativa de transferência GPU→GDI: igualdade RGB de todos os pixels, orientação, padding e animação em 213×121; integra o smoke e pode correr com `--preview-presentation`.
- Preparação assíncrona da primeira frame para sessões nativas e vídeo, com timeout/cancelamento e preservação do wallpaper anterior quando a preparação falha.
- Recuperação quando a apresentação GDI por GPU falha no desktop elevado do Windows 11, em vez de aceitar uma sessão preta como saudável.
- Arranque com Windows na janela e tray, integração MSIX StartupTask/Run por utilizador, consulta do estado do Windows e arranque na área de notificação.
- About & updates, versão/build visíveis, consulta manual de atualização pelo canal apropriado e acesso à pasta de diagnóstico.
- Logs por sessão de desenvolvimento, adicionais aos logs normais rotativos; versão/build/processo/localização registados no arranque.
- Empacotamento com `build-info.json`, commit base, estado dirty, hashes das fontes, patch e configuração opcional dos logs. SDK 8.0.421 fixado em `global.json`; diretórios de artefactos/pesquisa excluídos da compilação implícita.
- Documentação anterior ainda local: preparação/copy da Store 1.2.0, privacidade e ciclo de vida do microfone, mais pesquisa e plano do oceano. **O oceano é planeamento; não está implementado nem incluído na galeria.**

Os commits locais `fc3726d` (fontes de áudio, feedback dos dispositivos e Wallpaper settings) e `38f904c` (build Store 1.2.0 e WACK) precedem esta consolidação e também precisam de estar no remoto. A revisão antes do push confirmou que origin/main estava em `b9c4e84`, sem commits remotos divergentes.

## Identidade da build entregue

| Campo | Valor |
| --- | --- |
| Versão app / MSIX | `1.2.2` / `1.2.2.0` |
| Build ID | `1.2.2-20260927-155439-fc207191` |
| Commit base na criação | `38f904c4aedd43952a23d565af2ecf82dda5839a`, com alterações locais incluídas |
| Pacote | `artifacts/development-1.2.2/package/HYPNIX-1.2.2-x64.msix` |
| SHA-256 MSIX assinado | `71392F42F4770C105061F50DA13983D9BD2FC74710F8C02139D11364AB473EAE` |
| SHA-256 HYPNIX.dll no pacote | `FCA9D767B852975F6632293079AEA7FC7BCF1B5934B669A486C5A04A0182C2AD` |
| SHA-256 source-snapshot.zip | `8787BDF56BE8193A1596204E450391EA97799449CDE6A8E9F63418D968A40C7A` |
| Certificado local, thumbprint público | `621B6D3DB21F685873F56BE275420D729583D25E` |
| Assinatura | `signtool verify /pa`: passou |
| Manifesto das fontes | `build-info.json`: 385 ficheiros, guardados e conferidos no snapshot |

O pacote usa o certificado de desenvolvimento anteriormente autorizado e confiado pelo proprietário. Não é um pacote de distribuição pública. Não foi instalado automaticamente nesta entrega.

O snapshot documenta as fontes usadas antes deste fecho documental. O `sourceCommit` não muda retroativamente depois de um commit: o manifesto, snapshot e hashes continuam a identificar o pacote efetivamente entregue. Só documentação foi ajustada na consolidação posterior; não foi reconstruída a 1.2.2.

## Diagnóstico do sino e validação

1. A 1.2.0 instalada ainda reproduzia o sino do Windows a aparecer/desaparecer com a janela de configuração aberta.
2. A 1.2.1 usava realmente `SwapEffect=Discard` na prévia, conforme o log instalado, mas o utilizador reproduziu o sintoma. **Hide preview estabilizava o sino.**
3. A 1.2.2 eliminou a swap chain HWND da prévia. O log da instalação manual confirma a build acima e `Preview=True; Presentation=OffscreenGdi; WindowSwapChain=False`.

Evidência instalada: sessão `20260927-145629-32672`, Windows build 26200, GPU NVIDIA GeForce RTX 5070 Ti. A cópia local de aceitação contém 11 inicializações da prévia com OffscreenGdi, incluindo Living Fire e diferentes shaders; não contém linhas com `Exception` ou `failed`. Isso verifica versão/caminho de execução, não o estado visual do sino.

Estado da aceitação do sino: **resolvido na 1.2.2, confirmado pelo proprietário em 27 de setembro de 2026**: “foi resolvida a do sino.” Esta confirmação encerra o defeito reportado na prévia. É aceitação do teste manual do proprietário; não implica validação de todos os cenários de hardware. A explicação exata da heurística do Windows permanece uma hipótese.

Validação local já concluída para estas fontes:

| Verificação | Resultado / evidência local |
| --- | --- |
| Regressão .NET | 380 passaram, 0 falharam; `results/regression.trx`, `unit-test.log` |
| Build do harness nativo | 0 avisos, 0 erros; `native-build.log` |
| Smoke GPU e UI isolada | Passou; 13 modos prepare/render/pause/resume/dispose e verificações de imagem; `native-smoke.log` |
| Transferência da prévia | Passou para shaders e Living Fire, frames animadas e comparação pixel a pixel; capturas em `captures/` |
| Pacote | Versão do manifesto e build ID embutido conferidos; hash do DLL obtido diretamente do MSIX; `package/handoff.json` |

Os caminhos relativos desta tabela são sob `artifacts/development-1.2.2/`. O primeiro ensaio isolado do teste de prévia usou um salto de um segundo, que o relógio do fogo trata intencionalmente como pausa; o teste foi corrigido para uma cadência de 30 FPS. O smoke final passou. O log desse ensaio inicial é histórico, não o resultado final.

Nenhum E2E de desktop foi executado pelo agente nesta correção. O WACK registado para 1.2.0 não é uma certificação da 1.2.2.

## Retenção e próximos passos

- Logs atuais: `artifacts/development-logs/`, um ficheiro por arranque. About & updates e Open diagnostics apontam para a mesma pasta na build entregue.
- Pacotes, fontes exatas, hashes, assinatura, testes, capturas e cópias dos logs anteriores estão preservados em `artifacts/development-1.2.1/` e `artifacts/development-1.2.2/`.
- `acceptance/` contém a cópia do log instalado 1.2.2 e `manual-result.json` com a confirmação do proprietário; `baseline/` preserva o log 1.2.1 e as imagens do sino fornecidas pelo utilizador.
- Estes artefactos permanecem ignorados pelo Git. O commit guarda o código, testes, documentação e este inventário verificável, sem publicar logs pessoais, pacotes grandes ou chaves privadas.
- O bug do sino está encerrado por confirmação manual do proprietário. Continuar a matriz manual de hardware quando aplicável. Publicação Store, configuração do feed Velopack e implementação do oceano continuam trabalhos distintos, registados em [RELEASE_PENDING.md](RELEASE_PENDING.md) e no [plano do oceano](OCEAN_WALLPAPER_PLAN.md).
- Para a próxima versão, seguir [DEVELOPMENT_RELEASE_WORKFLOW.md](DEVELOPMENT_RELEASE_WORKFLOW.md): inventário, fonte/commit, build identificada, teste da versão instalada, retenção de evidências e fecho do Git.
