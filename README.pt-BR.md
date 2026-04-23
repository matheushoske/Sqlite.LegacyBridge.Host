# Sqlite.LegacyBridge.Host

Runtime de compatibilidade para acesso a SQLite legado criptografado (pilhas historicas RC4/RSA) consumido por aplicacoes .NET modernas.

Este repositorio publica o executavel host usado por:

- [EntityFrameworkCore.Sqlite.Legacy](https://github.com/matheushoske/EntityFrameworkCore.Sqlite.Legacy)

## Por que este host existe

Muitos sistemas legados em producao dependem de comportamento especifico do `System.Data.SQLite` e de bins nativos antigos.  
Esse host foi criado para preservar compatibilidade sem impedir modernizacao da aplicacao.

Com ele, voce consegue:

- manter bancos criptografados antigos ativos;
- modernizar a camada de negocio gradualmente;
- reduzir risco de migracao total imediata.

## Papel na arquitetura

`App EF Core` -> `Bridge client` -> `Named Pipes` -> `Sqlite.LegacyBridge.Host (net462)` -> `System.Data.SQLite` -> `banco legado`

## Integracao com o pacote EF

O pacote EF consegue baixar automaticamente o host via:

```csharp
SqliteLegacyDbContextOptionsExtensions.SetupBridgeHost();
```

Fonte padrao:

- `https://github.com/matheushoske/Sqlite.LegacyBridge.Host/releases/latest/download/Sqlite.LegacyBridge.Host.zip`

## Contrato de release

Cada release publica um artefato:

- `Sqlite.LegacyBridge.Host.zip`

Conteudo esperado:

- `Sqlite.LegacyBridge.Host.exe`
- dependencias gerenciadas
- dependencias nativas (`SQLite.Interop.dll`, etc.)

## CI/CD

- Workflow: `.github/workflows/host-release-sqlite-legacy-bridge.yml`
- Build em `net462`
- Validacao do executavel antes de zipar
- Publicacao com tag `host-v*`

## Operacao e seguranca

- manter senhas fora do codigo;
- validar antivirus/EDR para execucao do host;
- manter host + bins nativos no mesmo diretorio;
- respeitar compatibilidade de arquitetura do provider legado.
