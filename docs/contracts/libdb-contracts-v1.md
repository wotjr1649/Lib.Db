# Lib.Db Contracts v1

`libdb.contracts.json` is a checked-in, no-secret contract file for `Lib.Db.Tools` no-DB validation and report generation.

`Lib.Db.Tools` is separate from the `Lib.Db` runtime package. The v2.5.0 MVP project is not packable and is used from source during repository verification.

## CLI Usage

```powershell
dotnet run --project Lib.Db.Tools -- contract validate --expected expected.libdb.contracts.json --actual actual.libdb.contracts.json --format json --out artifacts/libdb-contract-report.json
dotnet run --project Lib.Db.Tools -- contract report --contracts libdb.contracts.json --format markdown --out artifacts/libdb-contract-report.md
```

`contract validate` compares two checked-in contract files. `contract report` writes an inventory report for one contract file. Both commands are local-file only and print `No SQL executed` on successful execution.

Exit codes:

- `0`: command completed without contract differences.
- `1`: validation differences or input/report failure.
- `2`: unsupported command, unsupported option shape, or invalid command usage.

Unsupported commands such as `inspect`, `scaffold`, `apply`, `execute`, and `migrate` are fail-closed in the v2.5.0 MVP.

## Required Shape

```json
{
  "schemaVersion": "1",
  "procedures": [
    {
      "schema": "dbo",
      "name": "Customer_Get",
      "parameters": [
        {
          "name": "@CustomerId",
          "direction": "Input",
          "type": "int",
          "nullable": false
        }
      ],
      "resultShape": "Known"
    }
  ],
  "tableTypes": [
    {
      "schema": "dbo",
      "name": "CustomerTvp",
      "columns": [
        {
          "ordinal": 1,
          "name": "CustomerId",
          "type": "int",
          "nullable": false
        }
      ]
    }
  ],
  "bulkTargets": [
    {
      "schema": "dbo",
      "table": "Customer",
      "keyColumns": [ "CustomerId" ]
    }
  ]
}
```

## Rules

- `schemaVersion` must be `"1"`.
- v1 is a strict schema. Unknown fields are rejected instead of ignored.
- Connection strings, passwords, tokens, API keys, and secret-bearing fields are not part of the schema.
- String values that look like connection strings are treated as sensitive report data. They must not be used as schema/object/type names, and reports redact them if encountered.
- `resultShape` may be `Known` or `Unknown`; `Unknown` is reported for manual review.
- `procedures`, `tableTypes`, and `bulkTargets` are required arrays. Nested arrays such as `parameters`, `columns`, and `keyColumns` are also required.
- Validation is local-file only. It does not inspect, alter, or execute SQL against a database.
- Reports redact secret-like object names before writing Markdown or JSON output.
