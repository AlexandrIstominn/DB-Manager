using System;
using System.Windows.Forms;
using System.Configuration;
using System.Data.SqlClient;
using System.Data;


namespace DB_Manager
{
    public partial class MainForm : Form
    {
        public static string connectionString { get; set; } = ConfigurationManager.ConnectionStrings["ExampleConnectionString"].ConnectionString;
        private SqlConnection connection = new SqlConnection(connectionString);
        public MainForm()
        {
            InitializeComponent();
        }
        private void MainForm_Load(object sender, EventArgs e)
        {
            try
            {
                connection.Open();
                LoadTables();
            }
            catch (SqlException exception)
            {
                Console.WriteLine(exception.Message);
            }
        }
        
        private void btnCreateTable_Click(object sender, EventArgs e)
        {
            CreateTableForm createTableForm = new CreateTableForm(connection, listBoxTables);
            createTableForm.ShowDialog();
            LoadTables();
        }

        private void btnAbout_Click(object sender, EventArgs e)  //отображение окна "О программе"
        {
            AboutForm aboutForm = new AboutForm(); 
            aboutForm.ShowDialog();
        }

        private void btnDeleteTable_Click(object sender, EventArgs e)
        {
            string tableName = listBoxTables.SelectedItem?.ToString();
            if (tableName != null)
            {
                DialogResult deleteTableConfirm = MessageBox.Show($"Вы уверены, что хотите удалить таблицу '{tableName}'?", 
                    "Требуется подтверждение действия", MessageBoxButtons.YesNo);

                if (deleteTableConfirm == DialogResult.Yes)
                {
                    DeleteTable(tableName);
                    listBoxTables.Items.Remove(tableName);
                }
            }
            else if (listBoxTables.Items.Count == 0)
            {
                MessageBox.Show("Нет таблиц для удаления");
            }
            else
            {
                MessageBox.Show("Выберите таблицу для удаления");
            }
        }

        private void btnEditTable_Click(object sender, EventArgs e)
        {
            if (listBoxTables.SelectedItem != null && listBoxTables.Items.Count > 0)
            {
                EditTableForm editTableForm = new EditTableForm(connection, listBoxTables, listBoxTables.SelectedItem.ToString());
                editTableForm.ShowDialog();
                LoadTables();
            }
            else if (listBoxTables.Items.Count == 0)
            {
                MessageBox.Show("Нет таблиц для редактирования");
            }
            else
            {
                MessageBox.Show("Выберите таблицу для редактирования");
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }

        private void LoadTables()
        {
            try
            {
                string query = "SELECT TABLE_NAME FROM [DBManaged].INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
                SqlCommand command = new SqlCommand(query, connection);
                SqlDataReader reader = command.ExecuteReader();
                listBoxTables.Items.Clear();
                while (reader.Read())
                {
                    listBoxTables.Items.Add(reader["TABLE_NAME"].ToString());
                }

                reader.Close();
            }
            catch (SqlException ex)
            {
                MessageBox.Show($"Ошибка при загрузке таблиц: {ex.Message}");
            }

        }  //загрузка данных в ListBox

        private void DeleteTable(string tableName)
        {
            string query = $"DROP TABLE [{tableName}]";

            try
            {
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.ExecuteNonQuery();
                    MessageBox.Show($"Таблица '{tableName}' успешно удалена", "Удаление завершено", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (SqlException ex)
            {
                MessageBox.Show($"Ошибка при удалении таблицы '{tableName}': {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
