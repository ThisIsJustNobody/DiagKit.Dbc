# DiagKit.Dbc.Tool

Command-line tools for `DiagKit.Dbc`.

The first commands focus on the DBC Excel format:

```bash
diagkit-dbc workbook template -o edit.xlsx
diagkit-dbc workbook export vehicle.dbc -o edit.xlsx
diagkit-dbc workbook validate edit.xlsx
diagkit-dbc workbook import edit.xlsx -o normalized.dbc
```

Import reads the Excel file itself and writes normalized DBC output through the core `DbcWriter` validation path. The Excel file contains DBC semantic entity sheets only, with no manifest/readme sheet or source-file binding.
