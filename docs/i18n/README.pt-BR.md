# Panorama Screen Monitor

[Русский](../../README.md) · [English](README.en.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · **Português (Brasil)** · [简体中文](README.zh-CN.md) · [日本語](README.ja.md)

**Controle automático da iluminação da tela do water cooler TRYX Panorama no Windows.**

A tela do Panorama pode continuar acesa quando você se afasta do computador e o monitor principal já apagou. O Panorama Screen Monitor fica na bandeja do sistema, apaga a iluminação após um período de inatividade e a acende quando você volta.

**Testado com TRYX Panorama `391A:1011` e KANALI 2.7.0.**

Há cerca de seis meses, pedi aos desenvolvedores do KANALI que adicionassem o desligamento automático da iluminação quando o computador estivesse inativo. O recurso ainda não existia quando preparei esta versão, então escrevi este utilitário.

[Baixar para Windows x64](https://github.com/Viper029GH/PanoramaScreenMonitor/releases/latest) · [Changelog](../../CHANGELOG.md) · [CC0 1.0](../../LICENSE)

<p align="center">
  <img src="../images/panorama-demo.png" alt="Panorama Screen Monitor no modo de demonstração" width="420">
</p>

## Guia rápido

**Requisitos:** Windows x64, TRYX Panorama com o dispositivo USB RK PANO (`391A:1011`) e KANALI em execução normal.

1. Baixe o EXE ou ZIP para Windows x64 na [versão mais recente](https://github.com/Viper029GH/PanoramaScreenMonitor/releases/latest). Se baixar o ZIP, extraia o conteúdo.
2. Execute o EXE. Ele é portátil e inclui o .NET. Se o KANALI iniciar depois, o programa aguardará automaticamente.
3. O tempo padrão é de **10 minutos**. Para seguir o tempo do monitor principal, abra **Configurações**, marque **Usar o tempo limite de tela do Windows** e clique em **Salvar**.

Fechar a janela mantém o programa ativo na bandeja. Para encerrá-lo, escolha **Sair** no menu do ícone. Interrompa outros scripts de controle da iluminação do Panorama antes de iniciar o programa.

O EXE não tem assinatura digital. As somas de verificação estão em `SHA256SUMS.txt`.

## Recursos

| Recurso | Como funciona |
| --- | --- |
| Controle automático | Tempo próprio de 1 a 1440 minutos ou tempo limite de tela do Windows. A configuração de tomada ou bateria é respeitada. **Nunca** desativa o desligamento automático. |
| Controle manual e pausa | Ligue ou desligue a tela pela janela ou pela bandeja. A pausa mantém o estado atual. O modo automático pode ser retomado a qualquer momento. |
| Brilho | O controle de 0 a 100% fica disponível com a tela ligada. O programa lê o dispositivo para confirmar cada alteração. |
| Diagnóstico | Modelo, firmware, software e sistema da tela, estado de USB e UDB, brilho e última leitura bem-sucedida. |
| Integração com o Windows | Inicie com o Windows ou na bandeja. Escolha tema claro, escuro ou siga o sistema. |
| Idiomas da interface | Russo, inglês, alemão, francês, espanhol, português do Brasil, chinês simplificado e japonês. |

## Como funciona

O programa calcula a inatividade com base na última interação no Windows. Os comandos passam pela interface USB padrão com acesso compartilhado. O KANALI continua funcionando enquanto o Panorama Screen Monitor encontra a resposta correspondente no log do UDB e verifica o estado da tela. Cada ajuste começa com uma nova leitura da configuração e preserva os outros campos. Se a configuração completa não puder ser interpretada, a gravação é cancelada.

A sincronização usa o tempo limite de tela do plano de energia ativo. Reprodução de vídeo, desligamento manual e outros eventos podem afetar o monitor principal independentemente.

As configurações e um pequeno log ficam em `%LOCALAPPDATA%\PanoramaScreenMonitor`. Ao relatar um problema, inclua a versão do KANALI, a versão do firmware exibida no diagnóstico e as etapas para reproduzi-lo.

## Compilar a partir do código-fonte

É necessário o **SDK do .NET 10**. O projeto usa C# e WPF.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
.\artifacts\publish\win-x64\PanoramaScreenMonitor.exe --demo
```

O modo de demonstração simula inatividade, interação, erros e tempos limite sem o water cooler. Rode `PanoramaScreenMonitor.exe --self-test "<caminho do relatório>"` para testar protocolo, log do UDB e controlador com dados simulados.

Execute `./scripts/build-release.ps1` na raiz do repositório no PowerShell. O [script](../../scripts/build-release.ps1) verifica a compilação e cria EXE, ZIP, avisos do .NET e somas SHA-256 em `artifacts/releases/`. A versão está no [arquivo de projeto](../../PanoramaScreenMonitor.csproj).

A implementação segue a documentação publicada de comandos e campos do dispositivo. [Esquema de transporte TRYX](https://github.com/DXVSI/Tryx-Linux-GUI/blob/production/protocol/wire-v1/transport.proto) · [Esquema de configuração](https://github.com/DXVSI/Tryx-Linux-GUI/blob/production/protocol/wire-v1/configuration.proto)

## Licença

O código-fonte e a documentação estão disponíveis sob [CC0 1.0 Universal](../../LICENSE). Os termos separados do .NET incluído estão em `THIRD-PARTY-NOTICES.txt`.
