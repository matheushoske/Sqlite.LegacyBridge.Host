# Sqlite.LegacyBridge.Host

Processo host responsavel por executar operacoes SQLite em runtime legada (`net462` + `System.Data.SQLite`) e expor o acesso por named pipes.

## Objetivo tecnico

Este executavel permite abrir e operar bancos legados criptografados (incluindo cenarios RC4/RSA historicos) com compatibilidade maior do que stacks modernas puras.

## Contrato de inicializacao

```powershell
Sqlite.LegacyBridge.Host.exe --pipe <pipe_name> --database <caminho_do_banco>
```

## Variaveis de ambiente comuns

- `PLU_SQLITE_PASSWORD`: senha do banco criptografado
- `PLU_SQLITE_DB`: caminho fallback do banco

## Ciclo de conexao

1. Host inicializa servidor de pipe
2. Sinaliza estado de pronto
3. Cliente confirma handshake
4. Loop NDJSON processa comandos/respostas

## Caminho recomendado de consumo

1. Instalar `EntityFrameworkCore.Sqlite.Legacy` (ou `.Ef31`)
2. Chamar `SetupBridgeHost()`
3. Configurar `UseSqliteLegacy(...)` no DbContext

## Boas praticas

- manter host e bins nativos na pasta `legacy/`;
- alinhar arquitetura com provider nativo;
- aplicar exclusoes no antivirus quando houver bloqueio de startup.

## Troubleshooting

- **Travou na inicializacao**: verificar permissao/colisao de pipe e bloqueios do sistema.
- **`file is not a database`**: validar caminho, senha e modo de criptografia.
- **Sai imediatamente**: geralmente dependencia nativa ausente.
- **Erro de interop**: incompatibilidade de arquitetura (`x86`/`x64`).
