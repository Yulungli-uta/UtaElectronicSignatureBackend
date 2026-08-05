# SGN database naming

- Tables: `SGN.tbl_EntityNames`
- Views: `SGN.vw_ViewName`
- Primary keys: `PK_tbl_TableName`
- Foreign keys: `FK_tbl_Child_tbl_Parent_Column`
- Indexes: `IX_tbl_Table_Columns`; unique indexes: `UX_tbl_Table_Columns`
- Checks/defaults: `CK_tbl_Table_Rule`, `DF_tbl_Table_Column`
- Business timestamps are `datetimeoffset`; concurrency columns are SQL Server `rowversion`.
