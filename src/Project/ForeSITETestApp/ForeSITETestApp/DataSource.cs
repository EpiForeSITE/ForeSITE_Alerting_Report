using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace ForeSITETestApp
{
    public class DataSource
    {
        public string? Name { get; set; }
        public string? DataURL { get; set; }
        public string? AppToken { get; set; }
        
        public string? ResourceURL { get; set; }

        public bool IsRealtime { get; set; }
        public bool IsSelected { get; set; } = false;

        public string? CreatedDate { get; set; } // ISO 8601 format
        public string? LastUpdated { get; set; } // ISO 8601 format
    }

    public class ModelProperty : INotifyPropertyChanged
    {
        private string _name;
        private string _type;
        private string _defaultValue;
        private string _title;

        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged("Name");
                }
            }
        }

        public string Type
        {
            get { return _type; }
            set
            {
                if (_type != value)
                {
                    _type = value;
                    OnPropertyChanged("Type");
                }
            }
        }

        public string DefaultValue
        {
            get { return _defaultValue; }
            set
            {
                if (_defaultValue != value)
                {
                    _defaultValue = value;
                    OnPropertyChanged("DefaultValue");
                }
            }
        }



        public string Title
        {
            get { return _title; }
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged("Title");
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class Model : INotifyPropertyChanged
    {
        private string _name;
        private string _fullname;
        private string _type;
        private string _description;
        private bool _enabled;
        private ObservableCollection<ModelProperty> _properties;
        private string _createdDate;
        private string _lastUpdated;

        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public string FullName
        {
            get { return _fullname; }
            set
            {
                if (_fullname != value)
                {
                    _fullname = value;
                    OnPropertyChanged(nameof(FullName));
                }
            }
        }

        public string Type
        {
            get { return _type; }
            set
            {
                if (_type != value)
                {
                    _type = value;
                    OnPropertyChanged(nameof(Type));
                }
            }
        }

        public string Description
        {
            get { return _description; }
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged(nameof(Description));
                }
            }
        }

        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (_enabled != value)
                {
                    _enabled = value;
                    OnPropertyChanged(nameof(Enabled));
                }
            }
        }

        public ObservableCollection<ModelProperty> Properties
        {
            get { return _properties; }
            set
            {
                if (_properties != value)
                {
                    _properties = value;
                    OnPropertyChanged(nameof(Properties));
                }
            }
        }

        public string CreatedDate
        {
            get { return _createdDate; }
            set
            {
                if (_createdDate != value)
                {
                    _createdDate = value;
                    OnPropertyChanged(nameof(CreatedDate));
                }
            }
        }

        public string LastUpdated
        {
            get { return _lastUpdated; }
            set
            {
                if (_lastUpdated != value)
                {
                    _lastUpdated = value;
                    OnPropertyChanged(nameof(LastUpdated));
                }
            }
        }

        public Model()
        {
            Properties = new ObservableCollection<ModelProperty>();
        }

        public Model(string name, string type, string description = "")
        {
            Name = name;
            Type = type;
            Description = description;
            Properties = new ObservableCollection<ModelProperty>();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // 深度复制模块
        public Model Clone()
        {
            Model clone = new Model
            {
                Name = this.Name,
                Type = this.Type,
                Description = this.Description,
                Enabled = this.Enabled,
                CreatedDate = this.CreatedDate,
                LastUpdated = this.LastUpdated,
                Properties = new ObservableCollection<ModelProperty>()
            };

            foreach (var prop in this.Properties)
            {
                clone.Properties.Add(new ModelProperty
                {
                    Name = prop.Name,
                    Type = prop.Type,
                    DefaultValue = prop.DefaultValue,
                   
                });
            }

            return clone;
        }
    }

    public class InitialModelsData
    {
        public List<Model> InitialModels { get; set; } = new List<Model>();
    }

    public enum ReportElementType
    {
        Title,
        Comment,
        Plot
    }

    public class ReportElement
    {
        public ReportElementType Type { get; set; }
        public System.Windows.Controls.Border? Element { get; set; }
    }


    public class VariableDisplayItem
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public enum CellType
    {
        Code,
        Markdown
    }

    // Enhanced NotebookWindow with R support
    public enum CellLanguage
    {
        Python,
        R,
        Markdown
    }


    public class NotebookCell
    {
        public string CellType { get; set; }

        public string Language { get; set; }
        public string Source { get; set; }
        public string Output { get; set; }
    }

    public class NotebookDocument
    {
        public List<NotebookCell> Cells { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
    }


    /// <summary>
    /// Result from code execution
    /// </summary>
    public class ExecutionResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("output")]
        public string Output { get; set; } = "";

        [JsonProperty("error")]
        public string Error { get; set; } = "";

        [JsonProperty("result")]
        public string Result { get; set; } = "";

        [JsonProperty("has_plot")]
        public bool HasPlot { get; set; }

        [JsonProperty("plot_data")]
        public string PlotData { get; set; } = "";

        [JsonProperty("plot_path")]
        public string PlotPath { get; set; } = "";
    }

    /// <summary>
    /// Information about the server's namespace
    /// </summary>
    public class NamespaceInfo
    {
        [JsonProperty("variables")]
        public Dictionary<string, VariableInfo> Variables { get; set; } = new Dictionary<string, VariableInfo>();

        [JsonProperty("available_modules")]
        public List<string> AvailableModules { get; set; } = new List<string>();

        [JsonProperty("r_available")]
        public bool RAvailable { get; set; } = false;
        public string Error { get; set; } = "";
    }

    /// <summary>
    /// Information about a variable in the namespace
    /// </summary>
    public class VariableInfo
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "";

        [JsonProperty("value")]
        public string Value { get; set; } = "";

        public string Language { get; set; } = "python";
    }

   

    /// <summary>
    /// Request for epidemiological analysis
    /// </summary>
    public class EpidemiologicalRequest
    {
        [JsonProperty("graph")]
        public GraphParameters Graph { get; set; } = new GraphParameters();
    }

    /// <summary>
    /// Parameters for epidemiological graph generation
    /// </summary>
    public class GraphParameters
    {
        [JsonProperty("Model")]
        public string Model { get; set; } = "farrington";

        [JsonProperty("DataSource")]
        public string DataSource { get; set; } = "Covid-19 Deaths";

        [JsonProperty("Title")]
        public string Title { get; set; } = "Farrington Outbreak Detection Simulation";

        [JsonProperty("YearBack")]
        public int YearBack { get; set; } = 3;

        [JsonProperty("UseTrainSplit")]
        public bool UseTrainSplit { get; set; } = false;

        [JsonProperty("Threshold")]
        public int Threshold { get; set; } = 1500;

        [JsonProperty("TrainSplitRatio")]
        public double TrainSplitRatio { get; set; } = 0.70;

        [JsonProperty("TrainEndDate")]
        public string TrainEndDate { get; set; } = "2024-12-31";
    }

    /// <summary>
    /// Result from epidemiological analysis
    /// </summary>
    public class EpidemiologicalResult
    {
        [JsonProperty("status")]
        public string Status { get; set; } = "";

        [JsonProperty("message")]
        public string Message { get; set; } = "";

        [JsonProperty("plot_path")]
        public string PlotPath { get; set; } = "";

        [JsonProperty("graph")]
        public GraphParameters Graph { get; set; }
    }

    /// <summary>
    /// Types of output for styling purposes
    /// </summary>
    public enum OutputType
    {
        Success,    // Green - successful execution
        Error,      // Red - errors occurred
        Warning,    // Yellow - warnings or issues
        Info,       // Blue - informational messages
        Default     // Gray - default styling
    }

    /// <summary>
    /// Result from adding a variable to the namespace
    /// </summary>
    public class AddVariableResult
    {
        [JsonProperty("status")]
        public string Status { get; set; } = "";

        [JsonProperty("message")]
        public string Message { get; set; } = "";

        [JsonProperty("variable_name")]
        public string VariableName { get; set; } = "";

        [JsonProperty("datasource")]
        public string DataSource { get; set; } = "";

        [JsonProperty("data_info")]
        public DataInfo DataInfo { get; set; } = new DataInfo();

        [JsonProperty("threshold")]
        public int Threshold { get; set; }

        [JsonProperty("overwritten")]
        public bool Overwritten { get; set; }
    }

    /// Information about the created data
    /// </summary>
    public class DataInfo
    {
        [JsonProperty("shape")]
        public int[] Shape { get; set; } = new int[0];

        [JsonProperty("columns")]
        public List<string> Columns { get; set; } = new List<string>();

        [JsonProperty("index_type")]
        public string IndexType { get; set; } = "";

        [JsonProperty("date_range")]
        public DateRange DateRange { get; set; } = new DateRange();

        [JsonProperty("memory_usage")]
        public string MemoryUsage { get; set; } = "";
    }


    /// <summary>
    /// Date range information
    /// </summary>
    public class DateRange
    {
        [JsonProperty("start")]
        public string Start { get; set; } = "";

        [JsonProperty("end")]
        public string End { get; set; } = "";
    }

 

    public class DataSourcesResult
    {
        [JsonProperty("datasources")]
        public List<DataSource> DataSources { get; set; } = new List<DataSource>();

        [JsonProperty("default_threshold")]
        public int DefaultThreshold { get; set; } = 1500;

        public string Error { get; set; } = "";
    }



    /// <summary>
    /// Result of Python code validation
    /// </summary>
    public class CodeValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }


    public class SchedulerTask
    {
        public int Id { get; set; }
        public string? Recipients { get; set; }
        public string? AttachmentPath { get; set; }
        public string? StartDate { get; set; }   // 存 YYYY-MM-DD 格式
        public string? Freq { get; set; }

        public bool IsSelected { get; set; }   // 绑定到 DataGridCheckBoxColumn
    }



}
