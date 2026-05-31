# DiagKit.Dbc.Workbook

DBC Excel format extension for `DiagKit.Dbc`.

Create a blank `.xlsx` DBC semantic table file or export one from a DBC document, edit DBC entities in Excel, then import that Excel file by itself and write normalized DBC output. Exported files contain only DBC entity sheets such as `Network`, `Nodes`, `Messages`, `Signals`, `ValueDescriptions`, `MultiplexRanges`, `EnvironmentVariables`, and attribute tables; they do not contain manifest/readme sheets, source paths, hashes, or internal object keys.
