# ForeSITE Alerting Report

<img src="images/foreSITEAlertingReport_icon2.jpg" alt="ForeSITE Alerting Report Logo" width="200"/>


**ForeSITE Alerting Report** is a C# WPF application designed for generating and managing data-driven surveillance reports. It integrates with a Python Flask localhost web server to process data using the `epySurv` module, leveraging CDC public surveillance data for demonstration and allowing users to import custom datasets for analysis. The application supports creating PDF reports with visualizations, managing data sources, scheduling automated report generation, and emailing reports via Outlook. Built with C#, XAML, PdfSharpCore, epySurv, matplotlib, rpy2 and a Flask backend, it provides a user-friendly interface for analysts and data professionals to create and share insightful reports.

## Features

- **Report Generation**: Create PDF reports with customizable titles and data-driven plots, powered by the `epySurv` module.
- **Data Source Management**: Add, edit, and delete data sources from Providers with API integration.
- **Plot Visualization**: Generate plots using CDC public surveillance data or user-imported datasets, with configurable parameters (e.g., data source, time range, threshold, model like Farrington).
- **Data Import**: Import custom surveillance data (e.g., CSV, JSON) for processing with `epySurv` via the Flask API.
- **Automated Scheduling**: Schedule monthly report generation using Windows Task Scheduler, with automatic email delivery via Outlook.
- **Rich Text Editing**: Customize report titles with rich text formatting (bold, italic, etc.).
- **Robust Font Handling**: Fallback mechanism for fonts (Arial, Helvetica, Times New Roman) to ensure PDF compatibility across systems.
- **Flask Backend**: Local Flask server (`http://127.0.0.1:5001/epyapi`) processes surveillance data and generates plot images.
- **Jupyter-Like Notebook**: For advanced users, a notebook interface allows runtime data analysis with **Python** code cells and **R** code cells, supporting dynamic addition, execution, deletion, save, and load of cells.
- **Enable Automatical Creation and Seamless Exchnage on Time-Series Data**: Our objective is to facilitate the seamless interchange of datasets between R and Python 

## Changelog

### Version 0.7 (August 25, 2025)
- Added R support in Jupyter-like notebook to enable seamless data exchnage between R and Python
- Updated log system for users to better understand the output

### Version 0.6 (August 18, 2025)
- Added a Jupyter-like notebook for advanced users to perform data analysis at runtime.
- Users can now add, run, delete, save, and load code cells in a non-modal window.
- Enhanced integration with Python environment for code execution.
- Minor bug fixes and performance improvements in report rendering.

### Version 0.5 (July 8, 2025)
- Supported four CDC data sources, including:
- **COVID-19 Testing** （only 2020 CDC Covid Testing data;)
- **Real-time Deaths (COVID-19, Pneumonia, and Influenza)**
- Allowed users to build surveillance models with configurable sliding window parameters, and generate dynamic, customized alert reports.


## Prerequisites

- **Operating System**: Windows 10 or later
- **.NET Framework**: .NET 8.0 or higher
- **Python**: Python 3.7 or later (for Flask server and epySurv package)
- **Dependencies**:
  - **WPF Application**:
    - [PdfSharpCore](https://github.com/ststeiger/PdfSharpCore) for PDF generation
    - [Newtonsoft.Json](https://www.newtonsoft.com/json) for JSON parsing
    - **TODO** Microsoft Outlook (for automated email delivery)
  - **Flask Server**:
    - Flask
    - `epySurv` (surveillance data processing module, assumed to be a custom or external library)
    - Other Python dependencies (e.g., `pandas`, `matplotlib` for data processing and plotting)
- **Tools**:
  - Visual Studio 2022 or later (with WPF development workload)
  - **TODO** PowerShell (for scheduled task execution)
  - Python environment (e.g., Anaconda, virtualenv)
- **TODO: Administrative Privileges**: Required for creating scheduled tasks
-

## Installation From Source Code

### 1. Clone the Repository
```bash
git clone https://github.com/yourusername/ForeSITE-Alerting-Report.git
cd ForeSITE-Alerting-Report
```

### 2. Set Up the WPF Application
1. **Open in Visual Studio**:
   - Open `ForeSITETestApp.sln` in Visual Studio.
   - Restore NuGet packages:
     ```bash
     dotnet restore
     ```

2. **Build the Solution**:
   - Build in Visual Studio (`Build > Build Solution`) or via CLI:
     ```bash
     dotnet build
     ```

### 3. Set Up the epySurv environment (optional)

   - [epySurv](https://github.com/JarnoRFB/epysurv) for epySurv Setup

### 4. Set Up the Flask Server (optional)

1. **Install Dependencies**:
   - Install Flask :
     ```bash
     pip install flask pandas matplotlib 
     ```
  
2. **Just for test: Run the Flask Server**:
   - Start the server at `http://127.0.0.1:5001`:
     ```bash
     python app.py
     ```
     Our Application doesn't need to call the Flask Server manually. it will be called by app into the background.

**Step 3 and 4 are optional** User can download epySurv environment from Release 0.6

### 5. Run the Application
- Run the WPF application as administrator to enable task scheduling:
  ```bash
  dotnet run --project ForeSITETestApp
  ```

## Installation From Release Version 

This release includes the ForeSITE Alerting Report application, featuring integrated Python-based surveillance data processing envrionment and .Net 8 runtime. Key components:

**Python Environment**: Includes the epysurv-env virtual environment with all required dependencies (e.g., rpy2, pandas, Flask) pre-configured for seamless operation.
**.NET Runtime**: Bundled with the .NET 8 runtime to support the WPF application. If the runtime fails to work on your system, please install the .NET 8 runtime manually from the official .NET website.

### Setup
- Download and extract the release archive.
- Run ForeSITEAlertingReport.exe from the extracted folder.

- Logs and generated files will be saved to C:\Users\<YourUsername>\Documents\ForeSITEAlertingReportFiles.

## Usage

### Creating a Report
1. **Launch the Application**:
   - Start the application and navigate to the "Reporter" tab.
2. **Add a Title**:
   - Click "Add Title" to create a customizable report title using the rich text editor.
3. **Add Plots**:
   - Click "Add Plot" and configure:
     - Plot title
     - Data source (CDC public data or custom imported data)
     - Model (e.g., Farrington)
     - Time range, frequency (e.g., weekly), and threshold
   - Plots are fetched via the Flask API (`http://127.0.0.1:5001/epyapi`) using `epySurv`.
4. **Save as PDF**:
   - Click "Save" to generate a PDF report (`Report.pdf`) via a file dialog.


### Importing Custom Data
1. Navigate to the "Data Source Manager" tab.
2. Add a new data source:
   - Enter name, app token, and API URL (if connecting to a custom server).
   - For local data, upload CSV/JSON files (assumed to be handled by the Flask server).
3. Use the imported data in the "Add Plot" dialog by selecting the custom data source.

### Managing Data Sources
1. Add a new data source:
   - Enter name, app token, and API URL.
   - Click "Save" to add to the list.
2. Delete data sources:
   - Select sources and click "Delete".

### TODO: Scheduling Automated Reports
1. Navigate to the "Scheduler" tab.
2. Click "Scheduling" to create a Windows Task Scheduler task:
   - **Task Name**: `MonthlyReportGeneration`
   - **Schedule**: First day of each month at 8:00 AM
   - **Action**: Runs `GenerateReport.ps1` to generate a PDF (`C:\Reports\Report_YYYYMM.pdf`) and email it via Outlook.
3. Ensure the Flask server is running and the application is run as administrator.



## Flask Server

The Flask server (`epyflaServer.py`) handles:
- **API Endpoint**: `http://127.0.0.1:5001/epyapi` for plot generation.
- **epySurv Integration**: Processes CDC public surveillance data or user-imported data using the `epySurv` module.
- **Data Processing**: Generates plot images returned to the WPF application.

### Example Flask Setup
```python
from flask import Flask, request, jsonify
import epySurv  # Assumed module for surveillance data processing

app = Flask(__name__)

@app.route('/epyapi', methods=['POST'])
def generate_plot():
    data = request.get_json()
    # Process data with epySurv (e.g., CDC data or user-imported CSV)
    plot_file = epySurv.generate_plot(data['graph'])
    return jsonify({'status': 'ready', 'file': plot_file})

if __name__ == '__main__':
    app.run(host='127.0.0.1', port=5001)
```

- Ensure `epySurv` is installed or included in the repository.
- Update `requirements.txt` with all dependencies.

## Project Structure

```
ForeSITE-Alerting-Report/
├── ForeSITETestApp/
│   ├── Dashboard.xaml         # Main UI layout
│   ├── Dashboard.xaml.cs      # Main logic (report generation, scheduling)
│   ├── ReportElement.cs       # Report element model
│   ├── PlotTitleDialog.xaml   # Dialog for plot titles
│   └── ForeSITETestApp.sln    # Solution file
├── Python/
│   ├── epyflaServer.py        # Flask server for epySurv integration
│   └── requirements.txt       # Python dependencies
│   
├── README.md                  # This file
└── LICENSE                    # License file (add your preferred license)
```

## Dependencies

- **C# WPF Application**:
  - PdfSharpCore: For PDF generation
  - Newtonsoft.Json: For API response parsing

- **Flask Server**:
  - Flask: Web server framework
  - epySurv: Surveillance data processing module
  - pandas, matplotlib: Common data processing and plotting libraries (adjust based on `epySurv` requirements)
- **TODO: Windows Task Scheduler**: For scheduling automated reports

## Animation Demo
The application uses **CDC public surveillance data** for demonstration purposes, processed via the `epySurv` module through the Flask server. Users can:
- Select CDC data as a data source in the "Add Plot" dialog.
![Alerting_2](images/Alerting_2.gif)

### Advanced Data Analysis with Notebook
Advanced users can leverage the "Data Analysis" feature to open a Jupyter-like Notebook window for runtime data analysis. This non-modal window allows users to:
- **Input Python Code**: Utilize a code editor with Python syntax highlighting to write and edit Python scripts.
- **Input R Code**: Utilize a code editor with R code to write and edit R scripts.
- **Dynamic Cell Management**: Add new Python/R code cells with the "Python" or "R" button, delete cells with the "Delete" button, and execute code using the "Run" button (currently a placeholder, with plans for integration with the Python environment).
- **Save and Load**: Persist notebook content to `Documents\notebook.json` using the "Save" button and reload it with the "Load" button, enabling session continuity.
- **Real-Time Analysis**: Perform data analysis on CDC public surveillance data or imported custom datasets, enhancing exploratory data analysis capabilities.

This feature is designed for users with Python expertise, providing a flexible environment to experiment with data processing and visualization alongside the application's core reporting functionality.
![Alerting_3](images/Alerting_3.gif)

R Language cell support
![Alerting_5](images/Alerting_5.gif)

### Import customized data set
- **Customized Data**: One column with name "date" or "dates" or "start_date", and another column with name "cases" or "n_cases" or "count" or "deaths" or "case_count"
![Alerting_4](images/Alerting_4.gif)


## Screenshots

![Screenshot 2025-08-25](images/Screenshot2025-08-25-100422.png)

![Screenshot 2025-07-01](images/Screenshot-2025-7-1-01.png)

![Screenshot 2025-07-01](images/Screenshot-2025-7-1-02.png)



## Troubleshooting

- **Font Error in PDF**:
  - If Arial is unavailable, the application falls back to Helvetica or Times New Roman (see `GetSafeFont` in `Dashboard.xaml.cs`).
  - Install `ttf-mscorefonts-installer` on non-Windows systems or embed fonts.
- **Flask Server Not Running**:
  - Ensure the server is running at `http://127.0.0.1:5001` before adding plots.
  - Check Python dependencies: `pip list` and install missing packages.
- **Plots Not Generated**:
  - Verify the **Year Back** parameter. if user doesn't have 5 years history data, please set to 3 
  - Verify the Flask server is running and accessible.
  - Ensure data source configuration (CDC or custom) is correct.
  - Check `epySurv` module documentation for specific requirements.
  - Check if we generated time-series data from Data Sources

## Contributing

Contributions are welcome! Please follow these steps:
1. Fork the repository.
2. Create a feature branch (`git checkout -b feature/your-feature`).
3. Commit changes (`git commit -m "Add your feature"`).
4. Push to the branch (`git push origin feature/your-feature`).
5. Open a pull request.

## License

[MIT License](LICENSE) <!-- Replace with your preferred license -->

## Contact

For issues or feature requests, please open an issue on GitHub or contact [Dr. Makoto Jones](mailto:makoto.jones@hsc.utah.edu) and [Tao He](mailto:tao.he@utah.edu).
