using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ForeSITETestApp
{
    public class DataSource
    {
        public string? Name { get; set; }
        public string? DataUrl { get; set; }
        public string? AppToken { get; set; }
        
        public string? ResourceUrl { get; set; }

        public bool isRealtime { get; set; }
        public bool IsSelected { get; set; }
    }

    public enum ReportElementType
    {
        Title,
        Plot
    }

    public class ReportElement
    {
        public ReportElementType Type { get; set; }
        public System.Windows.Controls.Border? Element { get; set; }
    }
}
