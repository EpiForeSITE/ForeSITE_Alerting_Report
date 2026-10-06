using ForeSITEScheduler;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;


namespace SchedulerRunner
{
    public class DBHelper
    {

        private static readonly string DatabasePath;
        private static readonly string ConnectionString;

        static DBHelper()
        {
            string userDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForeSITE", "Data");
            DatabasePath = Path.Combine(userDataDirectory, "foresite_alerting.db");
            ConnectionString = $"Data Source={DatabasePath}";
            Console.WriteLine($"Database path set to: {DatabasePath}");

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
            Directory.CreateDirectory(pythonDirectory);
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

                var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"PRAGMA journal_mode=WAL;      -- ??????,???
                                        PRAGMA synchronous=NORMAL;     -- ??/?????
                                        PRAGMA busy_timeout=5000;"; //--5 ?????
                    cmd.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand()) { 
                       command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS DataSources (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL UNIQUE,
                        DataURL TEXT,
                        ResourceURL TEXT,
                        AppToken TEXT,
                        IsRealtime INTEGER NOT NULL DEFAULT 0,
                        CreatedDate TEXT DEFAULT CURRENT_TIMESTAMP,
                        LastUpdated TEXT DEFAULT CURRENT_TIMESTAMP
                    )";
                       command.ExecuteNonQuery();
                }


                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS models (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL UNIQUE,
                        FullName TEXT,
                        Description TEXT,
                        Type TEXT,
                        Enabled INTEGER NOT NULL DEFAULT 1,
                        CreatedDate TEXT DEFAULT CURRENT_TIMESTAMP,
                        LastUpdated TEXT DEFAULT CURRENT_TIMESTAMP
                    )";
                    command.ExecuteNonQuery();
                }

                // New: Create scheduler table
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                CREATE TABLE IF NOT EXISTS scheduler (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Recipients TEXT,
                        AttachmentPath TEXT,
                        StartDate TEXT,
                        Freq TEXT,
                        DeliveryMethod TEXT NOT NULL DEFAULT 'Email'
                )";
                    command.ExecuteNonQuery();
                }
                EnsureSchedulerDeliveryMethodColumn(connection);
                EnsureSchedulerColumn(connection, "ReportId", "INTEGER");
                EnsureSchedulerColumn(connection, "Enabled", "INTEGER NOT NULL DEFAULT 1");

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS reports (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        DefinitionJson TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        IsArchived INTEGER NOT NULL DEFAULT 0
                    )";
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS report_runs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ReportId INTEGER NOT NULL,
                        SchedulerId INTEGER,
                        StartedAt TEXT NOT NULL,
                        CompletedAt TEXT,
                        DataAsOf TEXT,
                        Status TEXT NOT NULL,
                        IsAbnormal INTEGER NOT NULL DEFAULT 0,
                        PdfPath TEXT,
                        SnapshotJson TEXT NOT NULL DEFAULT '{}',
                        ErrorMessage TEXT
                    )";
                    command.ExecuteNonQuery();
                }


                // New: Create modelproperties table
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                CREATE TABLE IF NOT EXISTS modelproperties (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ModelId INTEGER NOT NULL,
                    Name TEXT,
                    Title TEXT,
                    Type TEXT,
                    DefaultValue TEXT,
                    FOREIGN KEY (modelId) REFERENCES models(Id) ON DELETE CASCADE
                )";
                    command.ExecuteNonQuery();
                }

                // Check if table is empty and insert initial data if needed
                if (GetDataSourceCount() == 0)
                {
                    InsertInitialDataSources();
                }

                // Check if table is empty and insert initial data if needed
                if (GetmodelsCount() == 0)
                {
                    InsertInitialmodels();
                }
                else
                {
                    EnsureMissingInitialModels();
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

        private static string? ResolveInitialModelsPath()
        {
            string baseDir = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "models.json"),
                Path.Combine(baseDir, "initial_models.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "models.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "initial_models.json")
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        public static int InsertInitialmodels()
        {
            try
            {
                string? jsonFilePath = ResolveInitialModelsPath();

                if (string.IsNullOrWhiteSpace(jsonFilePath))
                {
                    Console.WriteLine("Initial models file not found. Expected models.json or initial_models.json.");
                    return 0;
                }

                string jsonContent = File.ReadAllText(jsonFilePath);
                var modelData = JsonConvert.DeserializeObject<InitialModelsData>(jsonContent);

                if (modelData?.InitialModels == null || !modelData.InitialModels.Any())
                {
                    Console.WriteLine("No initial models found in JSON file");
                    return 0;
                }

                int insertedCount = 0;
                foreach (var model in modelData.InitialModels)
                {
                    // Insert into models table (without Properties)
                    int modelId = -1;
                    try
                    {
                        using var connection = new SqliteConnection(ConnectionString);
                        connection.Open();

                        using var command = connection.CreateCommand();
                        command.CommandText = @"
                    INSERT INTO models (Name, FullName, Description, Type, Enabled, CreatedDate, LastUpdated)
                    VALUES ($modelName, $fullModelName, $description, $type, $enabled, $createdDate, $lastUpdated);
                    SELECT last_insert_rowid();";
                        command.Parameters.AddWithValue("$modelName", model.Name ?? "");
                        command.Parameters.AddWithValue("$fullModelName", model.FullName ?? "");
                        command.Parameters.AddWithValue("$description", model.Description ?? "");
                        command.Parameters.AddWithValue("$type", model.Type ?? "");
                        command.Parameters.AddWithValue("$enabled", model.Enabled ? 1 : 0);
                        command.Parameters.AddWithValue("$createdDate", string.IsNullOrWhiteSpace(model.CreatedDate) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : model.CreatedDate);
                        command.Parameters.AddWithValue("$lastUpdated", string.IsNullOrWhiteSpace(model.LastUpdated) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : model.LastUpdated);

                        object? result = command.ExecuteScalar();
                        modelId = Convert.ToInt32(result);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error inserting model '{model.Name}': {ex.Message}");
                        continue;
                    }

                    if (modelId > 0)
                    {
                        insertedCount++;

                        // Insert each property into modelproperties table
                        if (model.Properties != null)
                        {
                            foreach (var prop in model.Properties)
                            {
                                try
                                {
                                    using var connection = new SqliteConnection(ConnectionString);
                                    connection.Open();

                                    using var command = connection.CreateCommand();
                                    command.CommandText = @"
                                INSERT INTO modelproperties (ModelId, Name, Type, DefaultValue, Title)
                                VALUES ($modelId, $name, $type, $defaultValue, $title)";
                                    command.Parameters.AddWithValue("$modelId", modelId);
                                    command.Parameters.AddWithValue("$name", prop.Name ?? "");
                                    command.Parameters.AddWithValue("$type", prop.Type ?? "");
                                    command.Parameters.AddWithValue("$defaultValue", prop.DefaultValue ?? "");
                                    command.Parameters.AddWithValue("$title", prop.Title ?? "");
                                    command.ExecuteNonQuery();
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error inserting property '{prop.Name}' for model '{model.Name}': {ex.Message}");
                                }
                            }
                        }
                    }
                }

                Console.WriteLine($"Inserted {insertedCount} initial models from JSON file");
                return insertedCount;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inserting initial models: {ex.Message}");
                return 0;
            }
        }
        private static int EnsureMissingInitialModels()
        {
            try
            {
                string? jsonFilePath = ResolveInitialModelsPath();
                if (string.IsNullOrWhiteSpace(jsonFilePath))
                    return 0;

                string jsonContent = File.ReadAllText(jsonFilePath);
                var modelData = JsonConvert.DeserializeObject<InitialModelsData>(jsonContent);
                if (modelData?.InitialModels == null || !modelData.InitialModels.Any())
                    return 0;

                int insertedCount = 0;
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();

                foreach (var model in modelData.InitialModels)
                {
                    if (model == null || string.IsNullOrWhiteSpace(model.Name))
                        continue;

                    int modelId;
                    using (var existingCmd = connection.CreateCommand())
                    {
                        existingCmd.Transaction = transaction;
                        existingCmd.CommandText = "SELECT Id FROM models WHERE Name = $name";
                        existingCmd.Parameters.AddWithValue("$name", model.Name.Trim());
                        var existing = existingCmd.ExecuteScalar();
                        if (existing == null || existing == DBNull.Value)
                        {
                            using var insertCmd = connection.CreateCommand();
                            insertCmd.Transaction = transaction;
                            insertCmd.CommandText = @"
                                INSERT INTO models (Name, FullName, Description, Type, Enabled, CreatedDate, LastUpdated)
                                VALUES ($modelName, $fullModelName, $description, $type, $enabled, $createdDate, $lastUpdated);
                                SELECT last_insert_rowid();";
                            insertCmd.Parameters.AddWithValue("$modelName", model.Name ?? "");
                            insertCmd.Parameters.AddWithValue("$fullModelName", model.FullName ?? "");
                            insertCmd.Parameters.AddWithValue("$description", model.Description ?? "");
                            insertCmd.Parameters.AddWithValue("$type", model.Type ?? "");
                            insertCmd.Parameters.AddWithValue("$enabled", model.Enabled ? 1 : 0);
                            insertCmd.Parameters.AddWithValue("$createdDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            insertCmd.Parameters.AddWithValue("$lastUpdated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            modelId = Convert.ToInt32(insertCmd.ExecuteScalar());
                            insertedCount++;
                        }
                        else
                        {
                            modelId = Convert.ToInt32(existing);
                        }
                    }

                    if (modelId <= 0 || model.Properties == null)
                        continue;

                    foreach (var prop in model.Properties)
                    {
                        if (prop == null || string.IsNullOrWhiteSpace(prop.Name))
                            continue;

                        using var propCheck = connection.CreateCommand();
                        propCheck.Transaction = transaction;
                        propCheck.CommandText = @"
                            SELECT 1 FROM modelproperties
                            WHERE modelId = $modelId AND Name = $name";
                        propCheck.Parameters.AddWithValue("$modelId", modelId);
                        propCheck.Parameters.AddWithValue("$name", prop.Name ?? "");
                        var propExists = propCheck.ExecuteScalar();
                        if (propExists != null && propExists != DBNull.Value)
                            continue;

                        using var propInsert = connection.CreateCommand();
                        propInsert.Transaction = transaction;
                        propInsert.CommandText = @"
                            INSERT INTO modelproperties (ModelId, Name, Type, DefaultValue, Title)
                            VALUES ($modelId, $name, $type, $defaultValue, $title)";
                        propInsert.Parameters.AddWithValue("$modelId", modelId);
                        propInsert.Parameters.AddWithValue("$name", prop.Name ?? "");
                        propInsert.Parameters.AddWithValue("$type", prop.Type ?? "");
                        propInsert.Parameters.AddWithValue("$defaultValue", prop.DefaultValue ?? "");
                        propInsert.Parameters.AddWithValue("$title", prop.Title ?? "");
                        propInsert.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
                return insertedCount;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error ensuring missing initial models: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Insert a new model into the database
        /// </summary>
        /// <param name="model">model record to insert</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool InsertModel(Model model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Name))
                return false;

            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO models (Name, FullName, Description, Type, Enabled, CreatedDate, LastUpdated) 
                    VALUES ($modelName, $fullmodelName, $description, $type,  $enabled,$createdDate, $lastUpdated)";

                command.Parameters.AddWithValue("$modelName", model.Name.Trim());
                command.Parameters.AddWithValue("$fullmodelName", model.FullName ?? "");
                command.Parameters.AddWithValue("$description", model.Description ?? "");
                command.Parameters.AddWithValue("$type", model.Type ?? "");
                command.Parameters.AddWithValue("$enabled", model.Enabled ? 1 : 0);


                command.Parameters.AddWithValue("$createdDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("$lastUpdated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                command.ExecuteNonQuery();
                Console.WriteLine($"Successfully inserted model: {model.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inserting model '{model.Name}': {ex.Message}");
                return false;
            }
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

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO DataSources (Name, DataURL, ResourceURL, AppToken, IsRealtime, CreatedDate, LastUpdated) 
                    VALUES ($name, $dataUrl, $resourceUrl, $appToken, $isRealtime, $createdDate, $lastUpdated)";

                command.Parameters.AddWithValue("$name", dataSource.Name.Trim());
                command.Parameters.AddWithValue("$dataUrl", dataSource.DataURL ?? "");
                command.Parameters.AddWithValue("$resourceUrl", dataSource.ResourceURL ?? "");
                command.Parameters.AddWithValue("$appToken", dataSource.AppToken ?? "");
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
        public static bool InsertDataSource(string name, string dataUrl, string resourceUrl, string appToken, bool isRealtime)
        {
            return InsertDataSource(new DataSource { Name = name, DataURL = dataUrl, ResourceURL = resourceUrl, AppToken = appToken, IsRealtime = isRealtime });
        }

        /// <summary>
        /// Safely get string value from SqliteDataReader
        /// </summary>
        private static string? SafeGetString(SqliteDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return null;
            try
            {
                return reader.GetString(ordinal);
            }
            catch
            {
                return reader.GetValue(ordinal)?.ToString();
            }
        }

        /// <summary>
        /// Safely get integer value from SqliteDataReader
        /// </summary>
        private static int? SafeGetInt(SqliteDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return null;
            try
            {
                return reader.GetInt32(ordinal);
            }
            catch
            {
                var val = reader.GetValue(ordinal)?.ToString();
                return int.TryParse(val, out var result) ? result : (int?)null;
            }
        }

        /// <summary>
        /// Safely get DateTime value from SqliteDataReader
        /// </summary>
        private static DateTime? SafeGetDateTime(SqliteDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return null;
            try
            {
                return reader.GetDateTime(ordinal);
            }
            catch
            {
                var val = reader.GetValue(ordinal)?.ToString();
                return DateTime.TryParse(val, out var dt) ? dt : (DateTime?)null;
            }
        }

        /// <summary>
        /// Safely deserialize JSON string into an ObservableCollection of modelProperty.
        /// Returns an empty collection if JSON is null, empty, or invalid.
        /// </summary>
        private static ObservableCollection<ModelProperty> DeserializeProperties(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new ObservableCollection<ModelProperty>();

            try
            {
                var list = JsonConvert.DeserializeObject<List<ModelProperty>>(json) ?? new List<ModelProperty>();
                return new ObservableCollection<ModelProperty>(list);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error deserializing Properties JSON: {ex.Message}");
                return new ObservableCollection<ModelProperty>();
            }
        }

        /// <summary>Get all models.</summary>
        public static ObservableCollection<Model> GetAllmodels()
        {
            var models = new ObservableCollection<Model>();

            try
            {
                if (!File.Exists(DatabasePath))
                {
                    Console.WriteLine("Database file not found, initializing...");
                    InitializeDatabase();
                }

                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                // 1. Read all models (without Properties column)
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT Id, Name, FullName, Description, Type, Enabled, CreatedDate, LastUpdated
                    FROM models
                    ORDER BY Name";

                using var reader = command.ExecuteReader();

                // Prepare a list to hold modelId to model mapping for later property assignment
                var modelIdTomodel = new Dictionary<int, Model>();

                int iId = reader.GetOrdinal("Id");
                int iName = reader.GetOrdinal("Name");
                int iFullName = reader.GetOrdinal("FullName");
                int iDesc = reader.GetOrdinal("Description");
                int iType = reader.GetOrdinal("Type");
                int iEnabled = reader.GetOrdinal("Enabled");
                int iCreated = reader.GetOrdinal("CreatedDate");
                int iUpdated = reader.GetOrdinal("LastUpdated");

                while (reader.Read())
                {
                    int modelId = reader.GetInt32(iId);
                    var m = new Model
                    {
                        Name = SafeGetString(reader, iName) ?? "",
                        FullName = SafeGetString(reader, iFullName) ?? "",
                        Description = SafeGetString(reader, iDesc) ?? "",
                        Type = SafeGetString(reader, iType) ?? "",
                        Enabled = SafeGetInt(reader, iEnabled) == 1,
                        CreatedDate = SafeGetString(reader, iCreated) ?? "",
                        LastUpdated = SafeGetString(reader, iUpdated) ?? "",
                        Properties = new ObservableCollection<ModelProperty>()
                    };
                    models.Add(m);
                    modelIdTomodel[modelId] = m;
                }

                // 2. Read all modelproperties and assign to corresponding models
                using var propCommand = connection.CreateCommand();
                propCommand.CommandText = @"
                    SELECT Id, modelId, Name, Title, Type, DefaultValue
                    FROM modelproperties";
                using var propReader = propCommand.ExecuteReader();
                while (propReader.Read())
                {
                    int modelId = Convert.ToInt32(propReader["modelId"]);
                    if (modelIdTomodel.TryGetValue(modelId, out var model))
                    {
                        var prop = new ModelProperty
                        {
                            Name = propReader["Name"]?.ToString() ?? "",
                            Title = propReader["Title"]?.ToString() ?? "",
                            Type = propReader["Type"]?.ToString() ?? "",
                            DefaultValue = propReader["DefaultValue"]?.ToString() ?? "",                        };
                        model.Properties.Add(prop);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving models: {ex.Message}");
            }

            return models;
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

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT Id, Name, DataURL, ResourceURL, AppToken, IsRealtime, CreatedDate, LastUpdated 
                    FROM DataSources 
                    ORDER BY Name";

                using var reader = command.ExecuteReader();

                int iName = reader.GetOrdinal("Name");
                int iDataURL = reader.GetOrdinal("DataURL");
                int iResourceURL = reader.GetOrdinal("ResourceURL");
                int iAppToken = reader.GetOrdinal("AppToken");
                int iIsRealtime = reader.GetOrdinal("IsRealtime");
                int iCreatedDate = reader.GetOrdinal("CreatedDate");
                int iLastUpdated = reader.GetOrdinal("LastUpdated");

                while (reader.Read())
                {
                    var ds = new DataSource
                    {
                        Name = SafeGetString(reader, iName),
                        DataURL = SafeGetString(reader, iDataURL),
                        ResourceURL = SafeGetString(reader, iResourceURL),
                        AppToken = SafeGetString(reader, iAppToken),
                        IsRealtime = SafeGetInt(reader, iIsRealtime) == 1,
                        CreatedDate = SafeGetString(reader, iCreatedDate),
                        LastUpdated = SafeGetString(reader, iLastUpdated)
                    };
                    dataSources.Add(ds);
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

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM DataSources";
                return Convert.ToInt32(command.ExecuteScalar());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting data source count: {ex.Message}");
                return 0;
            }
        }

        public static bool InsertScheduler(SchedulerTask task)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            INSERT INTO scheduler (Recipients, AttachmentPath, StartDate, Freq, DeliveryMethod)
            VALUES ($recipients, $path, $startDate, $freq, $deliveryMethod)";

                command.Parameters.AddWithValue("$recipients", task.Recipients ?? "");
                command.Parameters.AddWithValue("$path", task.AttachmentPath ?? "");
                command.Parameters.AddWithValue("$startDate", task.StartDate ?? "");
                command.Parameters.AddWithValue("$freq", task.Freq ?? "");
                command.Parameters.AddWithValue("$deliveryMethod", NormalizeDeliveryMethod(task.DeliveryMethod));

                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inserting scheduler task: {ex.Message}");
                return false;
            }
        }

        public static bool DeleteSchedulerById(int id)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM scheduler WHERE Id = $id";
                command.Parameters.AddWithValue("$id", id);

                return command.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting scheduler record: {ex.Message}");
                return false;
            }
        }

        public static bool UpdateScheduler(int id, string recipients, string attachmentPath, string startDate, string freq, string deliveryMethod)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
            UPDATE scheduler
               SET Recipients     = $recipients,
                   AttachmentPath = $path,
                   StartDate      = $startDate,
                   Freq           = $freq,
                   DeliveryMethod = $deliveryMethod
             WHERE Id             = $id";
                command.Parameters.AddWithValue("$recipients", recipients ?? string.Empty);
                command.Parameters.AddWithValue("$path", attachmentPath ?? string.Empty);
                command.Parameters.AddWithValue("$startDate", startDate ?? string.Empty);
                command.Parameters.AddWithValue("$freq", freq ?? string.Empty);
                command.Parameters.AddWithValue("$deliveryMethod", NormalizeDeliveryMethod(deliveryMethod));
                command.Parameters.AddWithValue("$id", id);

                return command.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating scheduler row: {ex.Message}");
                return false;
            }
        }


        public static ObservableCollection<SchedulerTask> GetAllSchedulers()
        {
            var schedulers = new ObservableCollection<SchedulerTask>();
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, ReportId, Recipients, AttachmentPath, StartDate, Freq, DeliveryMethod, Enabled FROM scheduler";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var task = new SchedulerTask
                    {
                        Id = reader.GetInt32(0),
                        ReportId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                        Recipients = reader.IsDBNull(2) ? null : reader.GetString(2),
                        AttachmentPath = reader.IsDBNull(3) ? null : reader.GetString(3),
                        StartDate = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Freq = reader.IsDBNull(5) ? null : reader.GetString(5),
                        DeliveryMethod = reader.IsDBNull(6) ? "Email" : NormalizeDeliveryMethod(reader.GetString(6)),
                        IsEnabled = reader.IsDBNull(7) || reader.GetInt32(7) != 0,
                        IsSelected = false   // 
                    };
                    schedulers.Add(task);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving schedulers: {ex.Message}");
            }
            return schedulers;
        }

        private static void EnsureSchedulerDeliveryMethodColumn(SqliteConnection connection)
        {
            bool exists = false;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(scheduler)";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), "DeliveryMethod", StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (!exists)
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE scheduler ADD COLUMN DeliveryMethod TEXT NOT NULL DEFAULT 'Email'";
                alter.ExecuteNonQuery();
            }
        }

        private static void EnsureSchedulerColumn(SqliteConnection connection, string columnName, string definition)
        {
            bool exists = false;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(scheduler)";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (!exists)
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE scheduler ADD COLUMN {columnName} {definition}";
                alter.ExecuteNonQuery();
            }
        }

        public static int EnsureReportForScheduler(int schedulerId, string templatePath, string definitionJson)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            using var lookup = connection.CreateCommand();
            lookup.Transaction = transaction;
            lookup.CommandText = "SELECT ReportId FROM scheduler WHERE Id = $id";
            lookup.Parameters.AddWithValue("$id", schedulerId);
            object? current = lookup.ExecuteScalar();
            if (current is not null && current != DBNull.Value)
            {
                transaction.Commit();
                return Convert.ToInt32(current, CultureInfo.InvariantCulture);
            }

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = @"INSERT INTO reports (Name, DefinitionJson, CreatedAt, UpdatedAt)
                                   VALUES ($name, $json, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
                                   SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("$name", Path.GetFileNameWithoutExtension(templatePath));
            insert.Parameters.AddWithValue("$json", definitionJson);
            int reportId = Convert.ToInt32(insert.ExecuteScalar(), CultureInfo.InvariantCulture);

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE scheduler SET ReportId = $reportId WHERE Id = $id";
            update.Parameters.AddWithValue("$reportId", reportId);
            update.Parameters.AddWithValue("$id", schedulerId);
            update.ExecuteNonQuery();
            transaction.Commit();
            return reportId;
        }

        public static void InsertReportRun(int reportId, int? schedulerId, DateTime startedAt,
            DateTime completedAt, string status, bool isAbnormal, string? pdfPath,
            string snapshotJson, string? errorMessage)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO report_runs
                (ReportId, SchedulerId, StartedAt, CompletedAt, DataAsOf, Status, IsAbnormal, PdfPath, SnapshotJson, ErrorMessage)
                VALUES ($reportId, $schedulerId, $startedAt, $completedAt, $dataAsOf, $status, $isAbnormal, $pdfPath, $snapshotJson, $errorMessage)";
            command.Parameters.AddWithValue("$reportId", reportId);
            command.Parameters.AddWithValue("$schedulerId", (object?)schedulerId ?? DBNull.Value);
            command.Parameters.AddWithValue("$startedAt", startedAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$completedAt", completedAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$dataAsOf", completedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$isAbnormal", isAbnormal ? 1 : 0);
            command.Parameters.AddWithValue("$pdfPath", (object?)pdfPath ?? DBNull.Value);
            command.Parameters.AddWithValue("$snapshotJson", snapshotJson ?? "{}");
            command.Parameters.AddWithValue("$errorMessage", (object?)errorMessage ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        private static string NormalizeDeliveryMethod(string? deliveryMethod) =>
            string.Equals(deliveryMethod, "Notification", StringComparison.OrdinalIgnoreCase)
                ? "Notification"
                : "Email";


        public static int GetmodelsCount()
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM models";
                return Convert.ToInt32(command.ExecuteScalar());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting data source count: {ex.Message}");
                return 0;
            }
        }

        /// <summary>Update model by unique modelName, including upsert of its modelProperty collection.</summary>
        public static bool Updatemodel(Model model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Name))
                return false;

            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                // 1. Upsert the model (without Properties column)
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT INTO models (Name, FullName, Description, Type, Enabled, CreatedDate, LastUpdated)
                        VALUES ($name, $fullName, $desc, $type, $ena, COALESCE($created,''), $updated)
                        ON CONFLICT(Name) DO UPDATE SET
                            FullmodelName = excluded.FullName,
                            Description    = excluded.Description,
                            Type           = excluded.Type,
                            Enabled        = excluded.Enabled,
                            LastUpdated    = excluded.LastUpdated";
                    command.Parameters.AddWithValue("$name", model.Name.Trim());
                    command.Parameters.AddWithValue("$fullName", model.FullName ?? "");
                    command.Parameters.AddWithValue("$desc", model.Description ?? "");
                    command.Parameters.AddWithValue("$type", model.Type ?? "");
                    command.Parameters.AddWithValue("$ena", model.Enabled ? 1 : 0);

                    var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    command.Parameters.AddWithValue("$created", string.IsNullOrWhiteSpace(model.CreatedDate) ? now : model.CreatedDate);
                    command.Parameters.AddWithValue("$updated", now);

                    command.ExecuteNonQuery();
                }

                // 2. Get the model's Id
                int modelId = -1;
                using (var idCommand = connection.CreateCommand())
                {
                    idCommand.CommandText = "SELECT Id FROM models WHERE Name = $name";
                    idCommand.Parameters.AddWithValue("$name", model.Name.Trim());
                    var result = idCommand.ExecuteScalar();
                    if (result == null || result == DBNull.Value)
                        throw new Exception("Failed to retrieve model Id after upsert.");
                    modelId = Convert.ToInt32(result);
                }

                // 3. Upsert each property in the model's Properties collection
                if (model.Properties != null)
                {
                    foreach (var prop in model.Properties)
                    {
                        // Check if property exists for this model by Name
                        using var checkCmd = connection.CreateCommand();
                        checkCmd.CommandText = @"
                            SELECT Id FROM modelproperties
                            WHERE modelId = $modelId AND Name = $name";
                        checkCmd.Parameters.AddWithValue("$modelId", modelId);
                        checkCmd.Parameters.AddWithValue("$name", prop.Name ?? "");

                        var propResult = checkCmd.ExecuteScalar();

                        if (propResult != null && propResult != DBNull.Value)
                        {
                            // Update existing property
                            int propertyId = Convert.ToInt32(propResult);
                            using var updateCmd = connection.CreateCommand();
                            updateCmd.CommandText = @"
                                UPDATE modelproperties
                                SET Title = $title,
                                    Type = $type,
                                    DefaultValue = $defaultValue
                                WHERE Id = $id";
                            updateCmd.Parameters.AddWithValue("$title", prop.Title ?? "");
                            updateCmd.Parameters.AddWithValue("$type", prop.Type ?? "");
                            updateCmd.Parameters.AddWithValue("$defaultValue", prop.DefaultValue ?? "");
                            updateCmd.Parameters.AddWithValue("$id", propertyId);

                            updateCmd.ExecuteNonQuery();
                        }
                        else
                        {
                            // Insert new property
                            using var insertCmd = connection.CreateCommand();
                            insertCmd.CommandText = @"
                                INSERT INTO modelproperties (modelId, Name, Title, Type, DefaultValue)
                                VALUES ($modelId, $name, $title, $type, $defaultValue)";
                            insertCmd.Parameters.AddWithValue("$modelId", modelId);
                            insertCmd.Parameters.AddWithValue("$name", prop.Name ?? "");
                            insertCmd.Parameters.AddWithValue("$title", prop.Title ?? "");
                            insertCmd.Parameters.AddWithValue("$type", prop.Type ?? "");
                            insertCmd.Parameters.AddWithValue("$defaultValue", prop.DefaultValue ?? "");

                            insertCmd.ExecuteNonQuery();
                        }
                    }
                }

                Console.WriteLine($"Successfully upserted model and properties: {model.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error upserting model '{model?.Name}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Delete model by name, including all related modelproperties.
        /// </summary>
        public static bool DeletemodelByName(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return false;

            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                // 1. Get the model's Id
                int modelId = -1;
                using (var idCommand = connection.CreateCommand())
                {
                    idCommand.CommandText = "SELECT Id FROM models WHERE Name = $name COLLATE NOCASE";
                    idCommand.Parameters.AddWithValue("$name", modelName.Trim());
                    var result = idCommand.ExecuteScalar();
                    if (result == null || result == DBNull.Value)
                    {
                        Console.WriteLine($"No model found with name: {modelName}");
                        return false;
                    }
                    modelId = Convert.ToInt32(result);
                }

                // 2. Delete all related modelproperties (optional, since ON DELETE CASCADE is set, but explicit for clarity)
                using (var delPropsCommand = connection.CreateCommand())
                {
                    delPropsCommand.CommandText = "DELETE FROM modelproperties WHERE modelId = $modelId";
                    delPropsCommand.Parameters.AddWithValue("$modelId", modelId);
                    delPropsCommand.ExecuteNonQuery();
                }

                // 3. Delete the model
                using (var delmodelCommand = connection.CreateCommand())
                {
                    delmodelCommand.CommandText = "DELETE FROM models WHERE Id = $id";
                    delmodelCommand.Parameters.AddWithValue("$id", modelId);
                    int rows = delmodelCommand.ExecuteNonQuery();
                    Console.WriteLine(rows > 0
                        ? $"Successfully deleted model and its properties: {modelName}"
                        : $"No model found with name: {modelName}");
                    return rows > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting model '{modelName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get a model by name (case-insensitive), including all its properties from the modelproperties table.
        /// </summary>
        public static Model? GetmodelByName(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return null;

            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                // 1. Get the model row
                int modelId = -1;
                Model? model = null;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT Id, Name, FullName, Description, Type, Enabled, CreatedDate, LastUpdated
                        FROM models
                        WHERE Name = $name COLLATE NOCASE";
                    command.Parameters.AddWithValue("$name", modelName.Trim());

                    using var reader = command.ExecuteReader();
                    if (!reader.Read()) return null;

                    int iId = reader.GetOrdinal("Id");
                    int iName = reader.GetOrdinal("Name");
                    int iFullName = reader.GetOrdinal("FullName");
                    int iDesc = reader.GetOrdinal("Description");
                    int iType = reader.GetOrdinal("Type");
                    int iEnabled = reader.GetOrdinal("Enabled");
                    int iCreated = reader.GetOrdinal("CreatedDate");
                    int iUpdated = reader.GetOrdinal("LastUpdated");

                    modelId = reader.GetInt32(iId);
                    model = new Model
                    {
                        Name = SafeGetString(reader, iName) ?? "",
                        FullName = SafeGetString(reader, iFullName) ?? "",
                        Description = SafeGetString(reader, iDesc) ?? "",
                        Type = SafeGetString(reader, iType) ?? "",
                        Enabled = SafeGetInt(reader, iEnabled) == 1,
                        CreatedDate = SafeGetString(reader, iCreated) ?? "",
                        LastUpdated = SafeGetString(reader, iUpdated) ?? "",
                        Properties = new ObservableCollection<ModelProperty>()
                    };
                }

                // 2. Get all properties for this model
                using (var propCommand = connection.CreateCommand())
                {
                    propCommand.CommandText = @"
                        SELECT Name, Title, Type, DefaultValue
                        FROM modelproperties
                        WHERE modelId = $modelId";
                    propCommand.Parameters.AddWithValue("$modelId", modelId);

                    using var propReader = propCommand.ExecuteReader();
                    while (propReader.Read())
                    {
                        var prop = new ModelProperty
                        {
                            Name = propReader["Name"]?.ToString() ?? "",
                            Title = propReader["Title"]?.ToString() ?? "",
                            Type = propReader["Type"]?.ToString() ?? "",
                            DefaultValue = propReader["DefaultValue"]?.ToString() ?? "",                        };
                        model.Properties.Add(prop);
                    }
                }

                return model;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving model '{modelName}': {ex.Message}");
                return null;
            }
        }


        /// <summary>
        /// Update an existing data source or insert if it doesn't exist
        /// </summary>
        /// <param name="dataSource">Data source record to upsert</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool UpdateDataSource(DataSource dataSource)
        {
            if (dataSource == null || string.IsNullOrWhiteSpace(dataSource.Name))
                return false;

            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT OR REPLACE INTO DataSources (Name, DataURL, ResourceURL, AppToken, IsRealtime, LastUpdated) 
                    VALUES ($name, $dataUrl, $resourceUrl, $appToken, $isRealtime, $lastUpdated)";

                command.Parameters.AddWithValue("$name", dataSource.Name.Trim());
                command.Parameters.AddWithValue("$dataUrl", dataSource.DataURL ?? "");
                command.Parameters.AddWithValue("$resourceUrl", dataSource.ResourceURL ?? "");
                command.Parameters.AddWithValue("$appToken", dataSource.AppToken ?? "");
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

                using var command = connection.CreateCommand();
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

        // --- modelProperty CRUD Methods ---

        /// <summary>
        /// Get all modelProperties for a given modelId.
        /// </summary>
        public static ObservableCollection<ModelProperty> GetmodelProperties(int modelId)
        {
            var properties = new ObservableCollection<ModelProperty>();
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                SELECT Id, modelId, Name, Type, DefaultValue, Title
                FROM modelproperties
                WHERE modelId = $modelId";
                command.Parameters.AddWithValue("$modelId", modelId);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    properties.Add(new ModelProperty
                    {
                        Name = reader["Name"]?.ToString() ?? "",
                        Type = reader["Type"]?.ToString() ?? "",
                        DefaultValue = reader["DefaultValue"]?.ToString() ?? "",                        Title = reader["Title"]?.ToString() ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving model properties: {ex.Message}");
            }
            return properties;
        }

        /// <summary>
        /// Insert a modelProperty for a given modelId.
        /// </summary>
        public static bool InsertmodelProperty(int modelId, ModelProperty property)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                INSERT INTO modelproperties (modelId, Name, Type, DefaultValue, Title)
                VALUES ($modelId, $name, $type, $defaultValue, $title)";
                command.Parameters.AddWithValue("$modelId", modelId);
                command.Parameters.AddWithValue("$name", property.Name ?? "");
                command.Parameters.AddWithValue("$type", property.Type ?? "");
                command.Parameters.AddWithValue("$defaultValue", property.DefaultValue ?? "");
                command.Parameters.AddWithValue("$title", property.Title ?? "");

                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inserting model property: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Update a modelProperty by its Id.
        /// </summary>
        public static bool UpdatemodelProperty(int propertyId, ModelProperty property)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                UPDATE modelproperties
                SET Name = $name,
                    Type = $type,
                    DefaultValue = $defaultValue,
                    Title = $title
                WHERE Id = $id";
                command.Parameters.AddWithValue("$name", property.Name ?? "");
                command.Parameters.AddWithValue("$type", property.Type ?? "");
                command.Parameters.AddWithValue("$defaultValue", property.DefaultValue ?? "");
                command.Parameters.AddWithValue("$title", property.Title ?? "");
                command.Parameters.AddWithValue("$id", propertyId);

                int rows = command.ExecuteNonQuery();
                return rows > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating model property: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Delete a modelProperty by its Id.
        /// </summary>
        public static bool DeletemodelProperty(int propertyId)
        {
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM modelproperties WHERE Id = $id";
                command.Parameters.AddWithValue("$id", propertyId);

                int rows = command.ExecuteNonQuery();
                return rows > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting model property: {ex.Message}");
                return false;
            }
        }

       
    }

}


