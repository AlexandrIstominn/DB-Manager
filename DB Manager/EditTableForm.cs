using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Text;
using System.Windows.Forms;

namespace DB_Manager
{
    public partial class EditTableForm : Form
    {
        ListBox listBoxTables;
        private SqlConnection connection;
        private string originalTableName;
        public EditTableForm(SqlConnection connection, ListBox listBoxTables, string originalTableName)
        {
            InitializeComponent();
            this.connection = connection;
            this.listBoxTables = listBoxTables;
            this.originalTableName = originalTableName;
        }

        private void EditTableForm_Load(object sender, EventArgs e)
        {
            LoadData();
        }

        private void dataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)  //обрабатываем событие клика на datagridview
        {
            DataGridView senderGrid = (DataGridView)sender;
            if (senderGrid.Columns[e.ColumnIndex] is DataGridViewImageColumn &&  //удаляем выделенную строку
                e.RowIndex >= 0 && !dataGridView.Rows[e.RowIndex].IsNewRow)
            {
                dataGridView.Rows.RemoveAt(e.RowIndex);
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {

            errorProvider.Clear();
            bool isValid = ValidateTableName() && ValidateDataGridView();

            if (isValid)
            {
                EditTable();
                Close();
            }
            else
            {
                MessageBox.Show("Исправьте ошибки в указанных полях перед продолжением!", "Ошибка ввода", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void EditTable()
        {
            string tableName = originalTableName;
            string newTableName = txtBoxTableName.Text.Trim();

            try
            {
                // Начало транзакции
                using (SqlTransaction transaction = connection.BeginTransaction())
                {
                    RenameTable(tableName, newTableName, transaction);
                    tableName = newTableName;
                    originalTableName = tableName;

                    var existingColumns = ExtractExistingColumns(tableName, transaction);
                    var (columnsToAdd, columnsToUpdate, columnsToRemove, columnsToSetPK, columnsToRemovePK) = AnalyzeColumns(existingColumns, transaction);

                    // Применяем изменения в таблице
                    ApplyTableModifications(tableName, columnsToAdd, columnsToUpdate, columnsToRemove, columnsToSetPK, columnsToRemovePK, transaction);

                    transaction.Commit();
                    MessageBox.Show("Таблица успешно обновлена.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при обновлении таблицы '{tableName}': {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadData()
        {
            txtBoxTableName.Text = originalTableName;
            try
            {
                //запрос для получения информации о столбцах таблицы
                string query = $@"
                    SELECT C.COLUMN_NAME, 
                            C.DATA_TYPE,
                            CASE WHEN KCU.COLUMN_NAME IS NOT NULL THEN 'PK' ELSE '' END AS COLUMN_KEY
                    FROM [DBManaged].INFORMATION_SCHEMA.COLUMNS AS C
                    LEFT JOIN [DBManaged].INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS TC 
                        ON C.TABLE_NAME = TC.TABLE_NAME 
                        AND C.TABLE_SCHEMA = TC.TABLE_SCHEMA
                        AND TC.CONSTRAINT_TYPE = 'PRIMARY KEY'
                    LEFT JOIN [DBManaged].INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS KCU 
                        ON KCU.TABLE_NAME = C.TABLE_NAME 
                        AND KCU.TABLE_SCHEMA = C.TABLE_SCHEMA 
                        AND KCU.COLUMN_NAME = C.COLUMN_NAME 
                        AND KCU.CONSTRAINT_NAME = TC.CONSTRAINT_NAME
                    WHERE C.TABLE_NAME = '{originalTableName}'";

                SqlCommand command = new SqlCommand(query, connection);
                SqlDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    //получаем свойства столбца
                    string columnName = reader.GetString(0);
                    string dataType = reader.GetString(1);
                    bool isPrimaryKey = reader.GetString(2) == "PK";

                    //преобразуем SQL типы в пользовательский вид
                    switch (dataType.ToLower())
                    {
                        case "int":
                            dataType = "Целочисленный";
                            break;
                        case "varchar":
                            dataType = "Текстовый";
                            break;
                        case "float":
                            dataType = "Вещественный";
                            break;
                        default:
                            dataType = "Даты/времени";
                            break;
                    }
                    //добавляем строку в DataGridView с данными столбца
                    dataGridView.Rows.Add(columnName, dataType, isPrimaryKey);
                }
                reader.Close();
            }
            catch (SqlException ex)
            {
                MessageBox.Show($"Ошибка при загрузке данных таблицы '{originalTableName}': {ex.Message}", "Ошибка", MessageBoxButtons.OK);
            }
        }

        //получаем текущие столбцы таблицы из базы данных
        private HashSet<string> ExtractExistingColumns(string tableName, SqlTransaction transaction)
        {
            var columnNames = new HashSet<string>();

            string query = "SELECT COLUMN_NAME FROM [DBManaged].INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tableName";
            using (SqlCommand command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@tableName", tableName);
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columnNames.Add(reader.GetString(0));
                    }
                }
            }

            return columnNames;
        }

        //смотрим какие столбцы следует добавить, обновить или удалить
        private (List<string> columnsToAdd, List<string> columnsToUpdate, List<string> columnsToRemove, List<string> columnsToSetPK, List<string> columnsToRemovePK) 
            AnalyzeColumns(HashSet<string> existingColumns, SqlTransaction transaction)
        {
            var columnsToAdd = new List<string>();
            var columnsToUpdate = new List<string>();
            var columnsToRemove = new List<string>(existingColumns);
            var columnsToSetPK = new List<string>();
            var columnsToRemovePK = new List<string>();

            foreach (DataGridViewRow row in dataGridView.Rows)
            {
                if (row.IsNewRow) continue;

                string columnName = row.Cells[0].Value?.ToString();
                string dataType = row.Cells[1].Value?.ToString();
                bool isPrimaryKey = Convert.ToBoolean(row.Cells[2].Value);

                string sqlDataType = ConvertToSQLType(dataType);

                if (existingColumns.Contains(columnName))
                {
                    columnsToUpdate.Add($"ALTER COLUMN [{columnName}] {sqlDataType}");
                    columnsToRemove.Remove(columnName);

                    // Определяем, нужно ли добавить или удалить первичный ключ
                    if (isPrimaryKey && !IsPrimaryKey(columnName, originalTableName, transaction))
                    {
                        columnsToSetPK.Add(columnName);
                    }
                    else if (!isPrimaryKey && IsPrimaryKey(columnName, originalTableName, transaction))
                    {
                        columnsToRemovePK.Add(columnName);
                    }
                }
                else
                {
                    columnsToAdd.Add($"[{columnName}] {sqlDataType}");

                    if (isPrimaryKey)
                    {
                        columnsToSetPK.Add(columnName);
                    }
                }
            }

            return (columnsToAdd, columnsToUpdate, columnsToRemove, columnsToSetPK, columnsToRemovePK);
        }

        //применяем изменения в таблице, объединяя добавление, изменение и удаление
        private void ApplyTableModifications(string tableName, List<string> columnsToAdd, List<string> columnsToUpdate, List<string> columnsToRemove, List<string> columnsToSetPK, List<string> columnsToRemovePK, SqlTransaction transaction)
        {

            var sqlBuilder = new StringBuilder();
            //удаление первичных ключей
            var sqlBuilderToAdd = new StringBuilder();
            //обрабатываем добавление новых столбцов
            foreach (var column in columnsToAdd)
            {
                sqlBuilderToAdd.Append($"ALTER TABLE [{tableName}] ADD {column} NOT NULL; ");
            }
            string sqlQuery = sqlBuilderToAdd.ToString().TrimEnd(' ', ';');
            if (columnsToAdd.Count > 0)
            {
                using (SqlCommand command = new SqlCommand(sqlQuery, connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }
            //удаление первичных ключей
            foreach (var columnName in columnsToRemovePK)
            {
                UpdatePrimaryKeyStatus(tableName, columnName, false, transaction);
            }
            //добавление первичных ключей
            foreach (var columnName in columnsToSetPK)
            {
                UpdatePrimaryKeyStatus(tableName, columnName, true, transaction);
            }
            //обрабатываем удаление столбцов
            foreach (var columnName in columnsToRemove)
            {
                if (IsPrimaryKey(columnName, tableName, transaction))
                {
                    string primaryKeyName = GetPrimaryKeyConstraintName(tableName, columnName, transaction);
                    if (!string.IsNullOrEmpty(primaryKeyName))
                    {
                        string dropPKQuery = $"ALTER TABLE [{tableName}] DROP CONSTRAINT [{primaryKeyName}];";
                        sqlBuilder.Append(dropPKQuery);
                    }
                    sqlBuilder.Append($"ALTER TABLE [{tableName}] DROP COLUMN [{columnName}]; ");
                }
                else
                {
                    sqlBuilder.Append($"ALTER TABLE [{tableName}] DROP COLUMN [{columnName}]; ");
                }
            }

            //обрабатываем обновление существующих столбцов
            foreach (var column in columnsToUpdate)
            {
                string[] columnParts = column.Split(new[] { ' ' }, 4);
                string columnName = (columnParts[2]).Substring(1, columnParts[2].Length - 2);
                if (IsPrimaryKey(columnName, tableName, transaction))
                {
                    string primaryKeyName = GetPrimaryKeyConstraintName(tableName, columnName, transaction);

                    //удаляем первичный ключ
                    string dropPKQuery = $"ALTER TABLE [{tableName}] DROP CONSTRAINT [{primaryKeyName}];";
                    sqlBuilder.Append(dropPKQuery);
                    

                    //добавляем первичный ключ снова
                    string addPKQuery = $"ALTER TABLE [{tableName}] ADD CONSTRAINT [PK_{tableName}_{columnName}] PRIMARY KEY([{columnName}]);";
                    sqlBuilder.Append(addPKQuery);
                }
                else
                {
                    sqlBuilder.Append($"ALTER TABLE [{tableName}] ALTER COLUMN [{columnName}] {columnParts[3]} NOT NULL; ");
                }
            }
            
            //убираем последние лишние пробелы и точки с запятой из запроса
            sqlQuery = sqlBuilder.ToString().TrimEnd(' ', ';');

            using (SqlCommand command = new SqlCommand(sqlQuery, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        private void UpdatePrimaryKeyStatus(string tableName, string columnName, bool addPrimaryKey, SqlTransaction transaction)
        {
            string primaryKeyName = GetPrimaryKeyConstraintName(tableName, columnName, transaction);

            if (addPrimaryKey)
            {
                string addPKQuery = $"ALTER TABLE [{tableName}] ADD CONSTRAINT [PK_{tableName}_{columnName}] PRIMARY KEY([{columnName}]);";
                using (SqlCommand command = new SqlCommand(addPKQuery, connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }
            else
            {
                // Удаление ограничения первичного ключа
                string dropPKQuery = $"ALTER TABLE [{tableName}] DROP CONSTRAINT [{primaryKeyName}];";
                using (SqlCommand command = new SqlCommand(dropPKQuery, connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        //конвертируем тип данных из пользовательского в sql
        private string ConvertToSQLType(string dataType)
        {
            switch (dataType)
            {
                case "Целочисленный":
                    dataType = "int";
                    break;
                case "Текстовый":
                    dataType = "varchar";
                    break;
                case "Вещественный":
                    dataType = "float";
                    break;
                default:
                    dataType = "DateTime";
                    break;
            }
            return dataType;
        }

        private bool IsPrimaryKey(string columnName, string tableName, SqlTransaction transaction)
        {
            string query = @"
        SELECT COUNT(*)
        FROM [DBManaged].INFORMATION_SCHEMA.KEY_COLUMN_USAGE
        WHERE TABLE_NAME = @tableName AND COLUMN_NAME = @columnName";

            using (SqlCommand command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@tableName", tableName);
                command.Parameters.AddWithValue("@columnName", columnName);
                return (int)command.ExecuteScalar() > 0;
            }
        }

        private void RenameTable(string oldTableName, string newTableName, SqlTransaction transaction)
        {
            string query = $"EXEC sp_rename '{oldTableName}', '{newTableName}';";
            using (SqlCommand command = new SqlCommand(query, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        private string GetPrimaryKeyConstraintName(string tableName, string columnName, SqlTransaction transaction)
        {
            string query = @"
    SELECT CONSTRAINT_NAME 
    FROM [DBManaged].INFORMATION_SCHEMA.KEY_COLUMN_USAGE 
    WHERE TABLE_NAME = @tableName AND COLUMN_NAME = @columnName";

            using (SqlCommand command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@tableName", tableName);
                command.Parameters.AddWithValue("@columnName", columnName);
                return command.ExecuteScalar() as string; // This will return the constraint name or null
            }
        }

        private bool ValidateTableName()
        {
            string tableName = txtBoxTableName.Text.Trim();
            

            if (string.IsNullOrWhiteSpace(tableName))
            {
                errorProvider.SetError(txtBoxTableName, "Поле не может быть пустым!");
                return false;
            }
            if (tableName.Length > 128)
            {
                errorProvider.SetError(txtBoxTableName, "Название таблицы не может содержать больше 128 символов!");
                return false;
            }
            if (listBoxTables.Items.Contains(tableName) && originalTableName != tableName)
            {
                errorProvider.SetError(txtBoxTableName, "Уже существует таблица с заданным именем!");
                return false;
            }
            errorProvider.SetError(txtBoxTableName, "");
            return true;
        }

        private bool ValidateDataGridView()
        {
            bool isValid = true;
            List<string> columnNames = new List<string>();

            foreach (DataGridViewRow row in dataGridView.Rows)
            {
                if (row.IsNewRow && dataGridView.Rows.Count > 1) continue; //пропускаем пустую строку

                isValid &= ValidateCellNotEmpty(row.Cells[1], "Выберите значение из списка");
                isValid &= ValidateCellNotEmpty(row.Cells[0], "Значение не может быть пустым");
                if (row.Cells[0].ErrorText == "") isValid &= ValidateCellMaxLength(row.Cells[0], 128, "Название поля таблицы не может содержать больше 128 символов");
                if (row.Cells[0].ErrorText == "") isValid &= ValidateUniqueColumnName(row.Cells[0], columnNames, "Название поля таблицы должно быть уникальным");
            }

            return isValid;
        }

        //проверка ячейки на пустое значение
        private bool ValidateCellNotEmpty(DataGridViewCell cell, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(cell.Value?.ToString()))
            {
                cell.ErrorText = errorMessage;
                return false;
            }
            cell.ErrorText = "";
            return true;
        }

        //проверка ячейки на максимальную длину
        private bool ValidateCellMaxLength(DataGridViewCell cell, int maxLength, string errorMessage)
        {
            if (cell.Value?.ToString().Length > maxLength)
            {
                cell.ErrorText = errorMessage;
                return false;
            }
            cell.ErrorText = "";
            return true;
        }

        //проверка ячейки на уникальность
        private bool ValidateUniqueColumnName(DataGridViewCell cell, List<string> columnNames, string errorMessage)
        {
            string columnName = cell.Value?.ToString().Trim();
            if (columnNames.Contains(columnName))
            {
                cell.ErrorText = errorMessage;
                return false;
            }
            columnNames.Add(columnName);
            cell.ErrorText = "";
            return true;
        }
    }
}
