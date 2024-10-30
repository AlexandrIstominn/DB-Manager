using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Windows.Forms;

namespace DB_Manager
{
    public partial class CreateTableForm : Form
    {
        ListBox listBoxTables;
        private SqlConnection connection;
        public CreateTableForm(SqlConnection connection, ListBox listBoxTables)
        {
            InitializeComponent();
            this.connection = connection;
            this.listBoxTables = listBoxTables;
        }
        
        private void btnOK_Click(object sender, EventArgs e)
        {
            errorProvider.Clear();
            bool isValid = ValidateTableName() && ValidateDataGridView();

            if (isValid)
            {
                CreateTable();
                Close();
            }
            else
            {
                MessageBox.Show("Исправьте ошибки в указанных полях перед продолжением!", "Ошибка ввода", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void dataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)  //удаление по кнопке в datagridview
        {
            DataGridView senderGrid = (DataGridView)sender;
            if (senderGrid.Columns[e.ColumnIndex] is DataGridViewImageColumn &&
                e.RowIndex >= 0 && !dataGridView.Rows[e.RowIndex].IsNewRow)
            {
                dataGridView.Rows.RemoveAt(e.RowIndex);
            }
        }

        private void dataGridView_CellEndEdit(object sender, DataGridViewCellEventArgs e)  
        {
            if (e.RowIndex >= 0 && e.RowIndex < dataGridView.Rows.Count)
            {
                DataGridViewCell cell = dataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex];
                cell.ErrorText = "";
            }
        }

        private void CreateTable()
        {
            string tableName = txtBoxTableName.Text;
            string query = $"CREATE TABLE [{tableName}] (";
            List<string> primaryKeys = new List<string>();

            foreach (DataGridViewRow dataGridViewRow in dataGridView.Rows)  //добавляем все поля таблицы к запросу
            {
                if (dataGridViewRow.IsNewRow) continue;

                string columnName = dataGridViewRow.Cells["columnName"].Value.ToString();
                string dataTypeColumn = ConvertToSQLType(dataGridViewRow.Cells["dataTypeColumn"].Value.ToString());

                query += $"[{columnName}] {dataTypeColumn} NOT NULL, ";

                bool isPrimaryKey = Convert.ToBoolean(dataGridViewRow.Cells["primaryKeyColumn"].Value);
                if (isPrimaryKey)
                {
                    primaryKeys.Add(columnName);
                }
            }
            if (primaryKeys.Count > 0)  //добавляем первичные ключи к запросу
            {
                query += $"PRIMARY KEY ({string.Join(", ", primaryKeys)})";
            }
            else
            {
                query = query.TrimEnd(',', ' ');
            }
            query += ")";
            try
            {
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.ExecuteNonQuery();
                    MessageBox.Show($"Таблица '{tableName}' успешно создана.");
                }
            }
            catch (SqlException exception)
            {
                MessageBox.Show($"Ошибка при создании таблицы '{tableName}': {exception.Message}");
            }
        }

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
            if (listBoxTables.Items.Contains(tableName))
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
