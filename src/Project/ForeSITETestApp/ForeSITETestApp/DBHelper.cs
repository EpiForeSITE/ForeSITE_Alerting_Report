using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ForeSITETestApp
{
    public class DBHelper
    {

        private static readonly string DatabasePath;
        private static readonly string ConnectionString;

        static DBHelper()
        {
            // Initialize database path in Python subdirectory
            string currentDirectory = Directory.GetCurrentDirectory();
            string pythonDirectory = Path.Combine(currentDirectory, "Python");
            DatabasePath = Path.Combine(pythonDirectory, "foresite_alerting.db");
            ConnectionString = $"Data Source={DatabasePath}";

            // Ensure Python directory exists
            EnsurePythonDirectoryExists();
        }

        /// <summary>
        /// Ensure the Python directory exists
        /// </summary>
        private static void EnsurePythonDirectoryExists()
        {
            string? pythonDirectory = Path.GetDirectoryName(DatabasePath);
            if (pythonDirectory == null)
            {
                throw new InvalidOperationException("Could not determine the Python directory from the database path.");
            }

        }


        /// <summary>
        /// Initialize the database and create tables if they don't exist
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public static bool InitializeDatabase()
        {
            try
            {
                EnsurePythonDirectoryExists();

                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS DataSources (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL UNIQUE,
                        DataURL TEXT,
                        ResourceURL TEXT,
                        IsRealtime INTEGER NOT NULL DEFAULT 0,
                        CreatedDate TEXT DEFAULT CURRENT_TIMESTAMP,
                        LastUpdated TEXT DEFAULT CURRENT_TIMESTAMP
                    )";
                command.ExecuteNonQuery();

                // Check if table is empty and insert initial data if needed
                if (GetDataSourceCount() == 0)
                {
                    InsertInitialDataSources();
                }

                Console.WriteLine($"Database initialized successfully at: {DatabasePath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing database: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Insert initial sample data sources into the database
        /// </summary>
        /// <returns>Number of rows inserted</returns>
        public static int InsertInitialDataSources()
        {
            var initialDataSources = new[]
           {
             new DataSource { Name = "COVID-19 Deaths", DataURL = "https://data.cdc.gov", ResourceURL = "r8kw-7aab", IsRealtime = true },
             new DataSource{ Name = "Pneumonia Deaths", DataURL = "https://data.cdc.gov", ResourceURL = "r8kw-7aab", IsRealtime = true },
             new DataSource{ Name = "Flu Deaths", DataURL = "https://data.cdc.gov", ResourceURL = "r8kw-7aab", IsRealtime = true },
             new DataSource{ Name = "COVID-19 Tests", DataURL = "local_covid_19_test_data.csv", ResourceURL = "local", IsRealtime = false }
           };

            int insertedCount = 0;
            foreach (var dataSource in initialDataSources)
            {
                if (InsertDataSource(dataSource))
                {
                    insertedCount++;
                }
            }

            Console.WriteLine($"Inserted {insertedCount} initial data sources");
            return insertedCount;
        }


        /// <summary>
        /// Insert a new data source into the database
        /// </summary>
        /// <param name="dataSource">Data source record to insert</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool InsertDataSource(DataSource dataSource)
        {
            if (dataSource == null || string.IsNullOrWhiteSpace(dataSource.Name))
                return false;

            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO DataSources (Name, DataURL, ResourceURL, IsRealtime, CreatedDate, LastUpdated) 
                    VALUES ($name, $dataUrl, $resourceUrl, $isRealtime, $createdDate, $lastUpdated)";

                command.Parameters.AddWithValue("$name", dataSource.Name.Trim());
                command.Parameters.AddWithValue("$dataUrl", dataSource.DataURL ?? "");
                command.Parameters.AddWithValue("$resourceUrl", dataSource.ResourceURL ?? "");
                command.Parameters.AddWithValue("$isRealtime", dataSource.IsRealtime ? 1 : 0);
                command.Parameters.AddWithValue("$createdDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("$lastUpdated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                command.ExecuteNonQuery();
                Console.WriteLine($"Successfully inserted data source: {dataSource.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inserting data source '{dataSource.Name}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Insert a new data source with individual parameters
        /// </summary>
        /// <param name="name">Data source name</param>
        /// <param name="dataUrl">Data URL</param>
        /// <param name="resourceUrl">Resource URL</param>
        /// <param name="isRealtime">Whether the data source is real-time</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool InsertDataSource(string name, string dataUrl, string resourceUrl, bool isRealtime)
        {
            return InsertDataSource(new DataSource { Name = name, DataURL = dataUrl, ResourceURL = resourceUrl, IsRealtime = isRealtime });
        }


        /// <summary>
        /// Get all data sources from the database
        /// </summary>
        /// <returns>List of data source records</returns>
        public static ObservableCollection<DataSource> GetAllDataSources()
        {

            var dataSources = new ObservableCollection<DataSource>();

            try
            {
                if (!File.Exists(DatabasePath))
                {
                    Console.WriteLine("Database file not found, initializing...");
                    InitializeDatabase();
                }

                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT Id, Name, DataURL, ResourceURL, IsRealtime, CreatedDate, LastUpdated 
                    FROM DataSources 
                    ORDER BY Name";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    dataSources.Add(new DataSource
                    {
                        Name = reader.GetString("Name"),
                        DataURL = reader.IsDBNull("DataURL") ? "" : reader.GetString("DataURL"),
                        ResourceURL = reader.IsDBNull("ResourceURL") ? "" : reader.GetString("ResourceURL"),
                        IsRealtime = reader.GetInt32("IsRealtime") == 1,
                        CreatedDate = reader.IsDBNull("CreatedDate") ? "" : reader.GetString("CreatedDate"),
                        LastUpdated = reader.IsDBNull("LastUpdated") ? "" : reader.GetString("LastUpdated")
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving data sources: {ex.Message}");
            }

            return dataSources;
        }

        /// <summary>
        /// Get the total count of data sources
        /// </summary>
        /// <returns>Number of data sources in database</returns>
        public static int GetDataSourceCount()
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM DataSources";
                return Convert.ToInt32(command.ExecuteScalar());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting data source count: {ex.Message}");
                return 0;
            }
        }


        /// <summary>
        /// Update an existing data source or insert if it doesn't exist
        /// </summary>
        /// <param name="dataSource">Data source record to upsert</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool UpsertDataSource(DataSource dataSource)
        {
            if (dataSource == null || string.IsNullOrWhiteSpace(dataSource.Name))
                return false;

            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT OR REPLACE INTO DataSources (Name, DataURL, ResourceURL, IsRealtime, LastUpdated) 
                    VALUES ($name, $dataUrl, $resourceUrl, $isRealtime, $lastUpdated)";

                command.Parameters.AddWithValue("$name", dataSource.Name.Trim());
                command.Parameters.AddWithValue("$dataUrl", dataSource.DataURL ?? "");
                command.Parameters.AddWithValue("$resourceUrl", dataSource.ResourceURL ?? "");
                command.Parameters.AddWithValue("$isRealtime", dataSource.IsRealtime ? 1 : 0);
                command.Parameters.AddWithValue("$lastUpdated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                command.ExecuteNonQuery();
                Console.WriteLine($"Successfully upserted data source: {dataSource.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error upserting data source '{dataSource.Name}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Delete a data source by name
        /// </summary>
        /// <param name="name">Data source name to delete</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool DeleteDataSourceByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM DataSources WHERE Name = $name COLLATE NOCASE";
                command.Parameters.AddWithValue("$name", name.Trim());

                int rowsAffected = command.ExecuteNonQuery();
                if (rowsAffected > 0)
                {
                    Console.WriteLine($"Successfully deleted data source: {name}");
                    return true;
                }
                else
                {
                    Console.WriteLine($"No data source found with name: {name}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting data source '{name}': {ex.Message}");
                return false;
            }
        }



    }
}
