using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace ForeSITEScheduler
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
        private bool _displayInUI;
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

        public bool DisplayInUI
        {
            get { return _displayInUI; }
            set
            {
                if (_displayInUI != value)
                {
                    _displayInUI = value;
                    OnPropertyChanged("DisplayInUI");
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
                    DisplayInUI = prop.DisplayInUI
                });
            }

            return clone;
        }
    }

    public class InitialModelsData
    {
        public List<Model> InitialModels { get; set; } = new List<Model>();
    }



    public class SchedulerTask
    {
        public int Id { get; set; }
        public string? Recipients { get; set; }
        public string? AttachmentPath { get; set; }
        public string? StartDate { get; set; }   // YYYY-MM-DD
        public string? Freq { get; set; }
        public bool IsSelected { get; set; }
    }

   
}
