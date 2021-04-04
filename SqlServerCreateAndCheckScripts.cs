using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace SqlServerDemo1
{
	class SqlServerCreateAndCheckScripts
	{
		#region Create DB

		/// <summary>
		/// Returns TSQL script for creating a database with a specified name.
		/// </summary>
		/// <param name="newDbName">The name of the to-be-created database.</param>
		public static string SqlCreateDatabase(string newDbName)
		{
			return $@"

					CREATE DATABASE {newDbName}
					GO

					USE {newDbName}
					GO

					CREATE TABLE [City] (
						[ID] INT IDENTITY (1, 1) NOT NULL,
						[Name] NVARCHAR (100) NOT NULL,
						PRIMARY KEY CLUSTERED ([ID] ASC)
					)

					CREATE TABLE [Country] (
						[ID] INT IDENTITY (1, 1) NOT NULL,
						[Name] NVARCHAR (100) NOT NULL,
						[Alpha3Code] CHAR (3) NOT NULL,
						[CapitalCityID] INT NOT NULL,
						[Population] INT NULL,
						[Area] DECIMAL (10, 2) NULL,
						[UpdatedOn] DATETIME2 (3) DEFAULT (getutcdate()) NOT NULL,
						PRIMARY KEY CLUSTERED ([ID] ASC),
						CONSTRAINT [FK_Country_CapitalCityID_City] FOREIGN KEY (CapitalCityID) REFERENCES [City] (ID)
					)

					DECLARE @dbFileName NVARCHAR(128);
					DECLARE @dbLogFileName NVARCHAR(128);

					SET @dbFileName = (
						SELECT name from sys.database_files
							where type_desc = 'ROWS'
						)

					SET @dbLogFileName = (
						SELECT name from sys.database_files
							where type_desc = 'LOG'
						)

					DECLARE @query NVARCHAR(255)

					SET @query = 'ALTER DATABASE {newDbName} MODIFY FILE ( NAME = ' + @dbFileName + ', FILEGROWTH = 1MB );' 
						+  'ALTER DATABASE {newDbName} MODIFY FILE ( NAME = ' + @dbLogFileName + ', FILEGROWTH = 1MB );'

					EXEC(@query)

					-- Database creation is successful if execution reached here.
					SELECT 'success'

					";
		}

		#endregion

		#region Check Create DB

		/// <summary>
		/// Returns TSQL script for checking if the user has permissions to create databases.
		/// </summary>
		public static string SqlCheckPermsCreateDatabase()
		{
			return $@"

					IF HAS_PERMS_BY_NAME('master', 'DATABASE', 'CREATE DATABASE') != 1
					BEGIN
						SELECT 'ERR_NO_PERMS_CREATE_DATABASE_IN_master'
						SET NOEXEC ON
					END

					IF HAS_PERMS_BY_NAME(null, null, 'CREATE ANY DATABASE') != 1
						BEGIN
							SELECT 'ERR_NO_PERMS_CREATE_ANY_DATABASE'
							SET NOEXEC ON
						END
	
					IF HAS_PERMS_BY_NAME('dbo', 'SCHEMA', 'ALTER') != 1
						BEGIN
							SELECT 'ERR_NO_PERMS_ALTER_SCHEMA_dbo'
							SET NOEXEC ON
						END

					-- Database creation is not denied if execution reached here.
					SELECT 'allow'

					";
		}

		#endregion

		#region Check interaction permissions

		/// <summary>
		/// Returns TSQL script for checking if the user has permissions to interact with the database 
		/// so that the app's functionality is fully available to the user.
		/// </summary>
		public static string SqlCheckInteractionPerms()
		{
			List<string> needSelectPermColumnNames_City = new()
			{
				"ID",
				"Name"
			};

			List<string> needSelectPermColumnNames_Country = new()
			{
				"Name",
				"Alpha3Code",
				"CapitalCityID",
				"Population",
				"Area",
				"UpdatedOn"
			};

			var city = "City";
			var country = "Country";

			// Unique variable names for composite script to avoid collision.
			var schemaNameForTable_City = $"@schemaNameForTable_{city}";
			var schemaNameForTable_Country = $"@schemaNameForTable_{country}";


			return $@"

					DECLARE {schemaNameForTable_City} NVARCHAR(128);
					DECLARE {schemaNameForTable_Country} NVARCHAR(128);

					SET {schemaNameForTable_City} = (
						SELECT TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES
							WHERE TABLE_NAME = '{city}'
						)

					SET {schemaNameForTable_Country} = (
						SELECT TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES
							WHERE TABLE_NAME = '{country}'
						)

					--SELECT on individual, non-extra columns of City & Country (excluding Country.ID)
					{SqlCheckPermsColumnsSelect(schemaNameForTable_City, city, needSelectPermColumnNames_City)}
					{SqlCheckPermsColumnsSelect(schemaNameForTable_Country, country, needSelectPermColumnNames_Country)}
					{SqlCheckPermsTable(schemaNameForTable_Country, city, "INSERT")}
					{SqlCheckPermsTable(schemaNameForTable_Country, city, "DELETE")}
					{SqlCheckPermsTable(schemaNameForTable_Country, country, "INSERT")}
					{SqlCheckPermsTable(schemaNameForTable_Country, country, "DELETE")}
					{SqlCheckPermsTable(schemaNameForTable_Country, country, "UPDATE")}
					{SqlCheckPermsTable(schemaNameForTable_Country, country, "ALTER")}
					
					-- Interaction permissions are not denied if execution reached here.
					SELECT 'allow'

					";
		}

		/// <summary>
		/// Returns TSQL script for checking if the user has SELECT permissions on specified columns.
		/// </summary>
		/// <param name="schemaNameForTable">The schema name for the table containing the columns that require SELECT.</param>
		/// <param name="tableName">The name of the table containing the columns that require SELECT.</param>
		/// <param name="columnNamesToCheckList">The list of column names that require SELECT.</param>
		public static string SqlCheckPermsColumnsSelect(string schemaNameForTable,
			string tableName, List<string> columnNamesToCheckList)
		{
			var columnNamesToCheck = string.Join(", ", columnNamesToCheckList.Select(name => $"'{name}'"));

			// Unique variable names for composite script to avoid collision.
			var permsColumnsSelect = $"@permsColumnsSelect_{tableName}_{nameof(SqlCheckPermsColumnsSelect)}";
			var noSelectPermsColumnName = $"@noSelectPermsColumnName_{tableName}_{nameof(SqlCheckPermsColumnsSelect)}";

			return $@"

					DECLARE {permsColumnsSelect} TABLE
					(
						ColumnName NVARCHAR (128) NOT NULL,
						HasSelectPerm BIT NOT NULL
					)

					INSERT INTO {permsColumnsSelect} ( ColumnName, HasSelectPerm )
						SELECT name AS ColumnName,
							HAS_PERMS_BY_NAME({schemaNameForTable} + '.' + '{tableName}', 'OBJECT', 'SELECT', name, 'COLUMN')
							AS HasSelectPerm
								FROM sys.columns
								WHERE OBJECT_NAME(object_id) = '{tableName}'
								AND name IN ( {columnNamesToCheck} )

					IF NOT EXISTS (
						SELECT * FROM {permsColumnsSelect}
						)
						BEGIN
							SELECT 'ERR_NO_PERMS_VIEW_TABLE_{tableName}'
							SET NOEXEC ON
						END

					DECLARE {noSelectPermsColumnName} NVARCHAR (128)

					SET {noSelectPermsColumnName} = (SELECT TOP (1) ColumnName FROM {permsColumnsSelect}
							WHERE HasSelectPerm = 0)

					IF {noSelectPermsColumnName} IS NOT NULL
						BEGIN
							SELECT 'ERR_NO_PERMS_SELECT_COLUMN_{tableName}' + '.' + {noSelectPermsColumnName}
							SET NOEXEC ON
						END

					";
		}

		/// <summary>
		/// Returns TSQL script for checking if the user has the specified permission on the specified table.
		/// </summary>
		/// <param name="schemaNameForTable">The schema name for the table.</param>
		/// <param name="tableName">The name of the table.</param>
		/// <param name="permission">The permission name.</param>
		/// <returns></returns>
		public static string SqlCheckPermsTable(string schemaNameForTable,
			string tableName, string permission)
		{
			// Unique variable name for composite script to avoid collision.
			var hasPermsOnTable = $"@hasPermsOnTable_{tableName}_{permission}_{nameof(SqlCheckPermsTable)}";

			return $@"

					DECLARE {hasPermsOnTable} BIT = 0;

					SET {hasPermsOnTable} = 
						HAS_PERMS_BY_NAME({schemaNameForTable} + '.' + '{tableName}', 'OBJECT', '{permission}')

					IF {hasPermsOnTable} != 1
						BEGIN
							SELECT CASE WHEN {hasPermsOnTable} = 0
									THEN 'ERR_NO_PERMS_{permission}_TABLE_{tableName}'
									ELSE 'ERR_CHECK_PERMS_BAD_PERMS_{permission}'
								END
							SET NOEXEC ON
						END

					";
		}

		#endregion

		#region Check valid DB

		/// <summary>
		/// Returns TSQL script for checking if the database is valid for use according to the app's functionality.
		/// </summary>
		public static string SqlCheckValidDatabase()
		{
			var city = "City";
			var country = "Country";

			return $@"

					{SqlCheckTableExistsAndSchemaUnique(city)}
					{SqlCheckTableExistsAndSchemaUnique(country)}

					{SqlCheckTablesInSameSchema(city, country)}

					-- Check columns for all tables.
					{SqlCheckColumns(city, country)}

					-- Check PKs.
					{SqlCheckPK(city, "ID")}
					{SqlCheckPK(country, "ID")}

					{SqlCheckFK(country, "CapitalCityID", city, "ID")}

					-- Structure is valid if execution reached here.
					SELECT 'valid'

					";
		}

		/// <summary>
		/// Returns TSQL script for checking if the specified table exists in the database 
		/// and that there are no other tables with the same name in other schemas.
		/// </summary>
		/// <param name="tableName">The name of the table.</param>
		public static string SqlCheckTableExistsAndSchemaUnique(string tableName)
		{
			// Unique variable name for composite script to avoid collision.
			var tableCountWithSameName = $"@tableCountWithName_{tableName}_{nameof(SqlCheckTableExistsAndSchemaUnique)}";

			return $@"

					-- Check if table {tableName} exists
					-- and there are no tables with the same name in other schemas.

					DECLARE {tableCountWithSameName} INT = 0;

					SET {tableCountWithSameName} = (
						SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
							WHERE TABLE_NAME = '{tableName}'
						)

					IF {tableCountWithSameName} != 1
						BEGIN
							SELECT CASE WHEN {tableCountWithSameName} = 0
									THEN 'ERR_NOT_EXISTS_TABLE_{tableName}'
									ELSE 'ERR_MULTIPLE_SCHEMAS_CONTAIN_TABLE_{tableName}'
								END
							SET NOEXEC ON
						END

					";
		}

		/// <summary>
		/// Returns TSQL script for checking if two specified tables belong to the same schema.
		/// </summary>
		/// <param name="table1Name">The name of the first table.</param>
		/// <param name="table2Name">The name of the second table.</param>
		public static string SqlCheckTablesInSameSchema(string table1Name, string table2Name)
		{
			// Unique variable names for composite script to avoid collision.
			var schemaNameForTable1 = $"@schemaNameForTable_{table1Name}_{nameof(SqlCheckTablesInSameSchema)}";
			var schemaNameForTable2 = $"@schemaNameForTable_{table2Name}_{nameof(SqlCheckTablesInSameSchema)}";

			return $@"

					-- Check if tables {table1Name} and {table2Name} are in the same schema.

					DECLARE {schemaNameForTable1} NVARCHAR(128);
					DECLARE {schemaNameForTable2} NVARCHAR(128);

					SET {schemaNameForTable1} = (
						SELECT TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES
							WHERE TABLE_NAME = '{table1Name}'
						)

					SET {schemaNameForTable2} = (
						SELECT TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES
							WHERE TABLE_NAME = '{table2Name}'
						)

					IF {schemaNameForTable1} != {schemaNameForTable2}
						BEGIN
							SELECT 'ERR_DIFFERENT_SCHEMAS_TABLES_{table1Name}_{table2Name}'
							SET NOEXEC ON
						END

					";
		}

		/// <summary>
		/// Returns TSQL script for checking the structure of columns in the City and Country tables.
		/// </summary>
		/// <param name="city">The name of the City table.</param>
		/// <param name="country">The name of the Country table.</param>
		public static string SqlCheckColumns(string city, string country)
		{
			return $@"

					-- Check City table columns.

					{SqlCheckColumn(city, "ID", "INT", "NO")}
					{SqlCheckColumn(city, "Name", "NVARCHAR", "NO", SqlCheckColumn_TextAddition(100))}

					{SqlCheckExtraColumns(city,
					new()
					{
						"ID",
						"Name"
					})}
					
					-- Check Country table columns.

					{SqlCheckColumn(country, "ID", "INT", "NO")}
					{SqlCheckColumn(country, "Name", "NVARCHAR", "NO", SqlCheckColumn_TextAddition(100))}
					{SqlCheckColumn(country, "Alpha3Code", "CHAR", "NO", SqlCheckColumn_TextAddition(3))}
					{SqlCheckColumn(country, "CapitalCityID", "INT", "NO")}
					{SqlCheckColumn(country, "Population", "INT", "YES")}
					{SqlCheckColumn(country, "Area", "DECIMAL", "YES", SqlCheckColumn_DecimalAddition(10, 2))}
					{SqlCheckColumn(country, "UpdatedOn", "DATETIME2", "NO", SqlCheckColumn_Datetime2Addition(3, "(getutcdate())"))}
					
					{SqlCheckExtraColumns(country,
					new()
					{
						"ID",
						"Name",
						"Alpha3Code",
						"CapitalCityID",
						"Population",
						"Area",
						"UpdatedOn"
					})}

					";
		}

		/// <summary>
		/// Returns TSQL script for checking the column structure.
		/// </summary>
		/// <param name="tableName">The name of the table containing the required column.</param>
		/// <param name="columnName">The name of the column.</param>
		/// <param name="dataType">The data type that the column must have.</param>
		/// <param name="isNullable"><see langword="true"/> if the column can have NULL values; otherwise, <see langword="false"/>.</param>
		/// <param name="addition">The additional TSQL script for extended structure check.</param>
		public static string SqlCheckColumn(string tableName, string columnName, string dataType, string isNullable, string addition = "")
		{
			return $@"

					-- Check column {tableName}.{columnName}.

					IF NOT EXISTS (
						SELECT * FROM INFORMATION_SCHEMA.COLUMNS  
							WHERE TABLE_NAME = '{tableName}'
							AND COLUMN_NAME = '{columnName}'
							AND DATA_TYPE = '{dataType}'
							AND IS_NULLABLE = '{isNullable}'
							{addition}
						)
						BEGIN
							SELECT 'ERR_INVALID_COLUMN_{tableName}.{columnName}'
							SET NOEXEC ON
						END

					";
		}

		/// <summary>
		/// Returns TSQL script for additional check of the DECIMAL column structure.
		/// </summary>
		/// <param name="precision">The precision.</param>
		/// <param name="scale">The scale.</param>
		public static string SqlCheckColumn_DecimalAddition(int precision, int scale)
		{
			return $@"AND NUMERIC_PRECISION = '{precision}'
						  AND NUMERIC_SCALE = '{scale}'";
		}

		/// <summary>
		/// Returns TSQL script for additional check of the DATETIME2 column structure.
		/// </summary>
		/// <param name="datetimePrecision"></param>
		/// <param name="columnDefault"></param>
		/// <returns></returns>
		public static string SqlCheckColumn_Datetime2Addition(int datetimePrecision, string columnDefault)
		{
			return $@"AND DATETIME_PRECISION = '{datetimePrecision}'
						  AND COLUMN_DEFAULT = '{columnDefault}'";
		}

		/// <summary>
		/// Returns TSQL script for additional check of the text-containing column structure.
		/// </summary>
		/// <param name="characterMaximumLength"></param>
		/// <returns></returns>
		public static string SqlCheckColumn_TextAddition(int characterMaximumLength)
		{
			return $@"AND CHARACTER_MAXIMUM_LENGTH = '{characterMaximumLength}'";
		}

		/// <summary>
		/// Returns TSQL script for checking the extra (not required by the app's functionality) columns in the specified table.<br/>
		/// These columns must be NULLABLE or have a DEFAULT value to be ignored during the app's interaction with the database.<br/>
		/// <br/>
		/// Assumes that <see cref="SqlCheckTableExistsAndSchemaUnique(string)"/> is called beforehand,<br/>
		/// which eliminates possible existence of another table with the name <paramref name="tableName"/> in other schemas.
		/// </summary>
		/// <param name="tableName">The name of the table.</param>
		/// <param name="excludeColumnNamesList">The list of column names to exclude from this check. These columns are the part of the app's functionality.</param>
		public static string SqlCheckExtraColumns(string tableName, List<string> excludeColumnNamesList)
		{
			var excludeColumnNames = string.Join(", ", excludeColumnNamesList.Select(name => $"'{name}'"));

			return $@"

					-- Check extra columns in {tableName}. These columns must be NULLABLE or have a DEFAULT value.

					IF EXISTS (
						SELECT * FROM INFORMATION_SCHEMA.COLUMNS  
							WHERE TABLE_NAME = '{tableName}'
							AND COLUMN_NAME NOT IN ({excludeColumnNames})
							AND IS_NULLABLE = 'NO'
							AND COLUMN_DEFAULT IS NULL
						)
						BEGIN
							SELECT 'ERR_INVALID_EXTRA_COLUMNS'
							SET NOEXEC ON
						END

					";
		}

		/// <summary>
		/// Returns TSQL script for checking the primary key of the specified table.<br/>
		/// <br/>
		/// Assumes that <see cref="SqlCheckTableExistsAndSchemaUnique(string)"/> is called beforehand,<br/>
		/// which eliminates possible existence of another table with the name <paramref name="tableName"/> in other schemas.
		/// </summary>
		/// <param name="tableName">The name of the table.</param>
		/// <param name="pkColumnName">The name of the column that must be the PK.</param>
		public static string SqlCheckPK(string tableName, string pkColumnName)
		{
			// Unique variable name for composite script to avoid collision.
			var pkIdForTable = $"@pkIdForTable_{tableName}_{nameof(SqlCheckPK)}";

			return $@"

					-- Check PK for table {tableName}.
					
					DECLARE {pkIdForTable} INT;

					SET {pkIdForTable} = (
						SELECT object_id FROM sys.key_constraints kc 
							WHERE type = 'PK'
							AND OBJECT_NAME(kc.parent_object_id) = '{tableName}'
						)

					IF {pkIdForTable} IS NULL
						BEGIN
							SELECT 'ERR_NOT_EXISTS_PK_TABLE_{tableName}'
							SET NOEXEC ON
						END

					IF NOT EXISTS (
						SELECT * FROM INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE
							WHERE CONSTRAINT_NAME = OBJECT_NAME({pkIdForTable})
							AND COLUMN_NAME = '{pkColumnName}'
							AND TABLE_CATALOG = CONSTRAINT_CATALOG
							AND TABLE_SCHEMA = CONSTRAINT_SCHEMA
						)
						BEGIN
							SELECT 'ERR_INVALID_PK_TABLE_{tableName}'
							SET NOEXEC ON
						END

					";
		}

		/// <summary>
		/// Returns TSQL script for checking the foreign key connecting the specified columns.<br/>
		/// <br/>
		/// Assumes that <see cref="SqlCheckTablesInSameSchema(string, string)"/> is called beforehand,<br/>
		/// which makes sure that the <paramref name="parentTableName"/> and <paramref name="referencedTableName"/> tables belong to the same schema.
		/// </summary>
		/// <param name="parentTableName">The name of the parent table.</param>
		/// <param name="parentColumnName">The name of the parent column.</param>
		/// <param name="referencedTableName">The name of the referenced table.</param>
		/// <param name="referencedColumnName">The name of the referenced column.</param>
		public static string SqlCheckFK(string parentTableName, string parentColumnName,
			string referencedTableName, string referencedColumnName)
		{
			return $@"

					-- Check FK {parentTableName}.{parentColumnName} references {referencedTableName}.{referencedColumnName}.

					IF NOT EXISTS (
						SELECT * FROM SYS.FOREIGN_KEY_COLUMNS
							WHERE OBJECT_NAME(parent_object_id) = '{parentTableName}'
							AND COL_NAME(parent_object_id, parent_column_id) = '{parentColumnName}'
							AND OBJECT_NAME(referenced_object_id) = '{referencedTableName}'
							AND COL_NAME(referenced_object_id, referenced_column_id) = '{referencedColumnName}'
						)
						BEGIN
							SELECT 'ERR_NOT_EXISTS_OR_INVALID_FK_{parentTableName}.{parentColumnName}_{referencedTableName}.{referencedColumnName}'
							SET NOEXEC ON
						END

					";
		}

		#endregion
	}
}
