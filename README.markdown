# ForeSITE Alerting Report

![ForeSITE Alerting Report](https://via.placeholder.com/800x200.png?text=ForeSITE+Alerting+Report) <!-- Replace with actual screenshot or logo -->

**ForeSITE Alerting Report** is a WPF application designed for generating and managing data-driven surveillance reports. It integrates with a Python Flask localhost web server to process data using the `epySurv` module, leveraging CDC public surveillance data for demonstration and allowing users to import custom datasets for analysis. The application supports creating PDF reports with visualizations, managing data sources, scheduling automated report generation, and emailing reports via Outlook. Built with C#, XAML, PdfSharpCore, and a Flask backend, it provides a user-friendly interface for analysts and public health professionals.

## Features

- **Report Generation**: Create PDF reports with customizable titles and data-driven plots, powered by the `epySurv` module.
- **Data Source Management**: Add, edit, and delete data sources with API integration to a Flask server.
- **Plot Visualization**: Generate plots using CDC public surveillance data or user-imported datasets, with configurable parameters (e.g., data source, time range, threshold, model like Farrington).
- **Data Import**: Import custom surveillance data (e.g., CSV, JSON) for processing with `epySurv` via the Flask API.
- **Automated Scheduling**: Schedule monthly report generation using Windows Task Scheduler, with automatic email delivery via Outlook.
- **Rich Text Editing**: Customize report titles with rich text formatting (bold, italic, etc.).
- **Robust Font Handling**: Fallback mechanism for fonts (Arial, Helvetica, Times New Roman) to ensure PDF compatibility across systems.
- **Flask Backend**: Local Flask server (`http://127.0.0.1:5001/epyapi`) processes surveillance data and generates plot images.

## Prerequisites

- **Operating System**: Windows 10 or later
- **.NET Framework**: .NET 6.0 or higher
- **Python**: Python 3.8 or later (for Flask server)
- **Dependencies**:
  - **WPF Application**:
    - [PdfSharpCore](https://github.com/ststeiger/PdfSharpCore) for PDF generation
    - [Newtonsoft.Json](https://www.newtonsoft.com/json) for JSON parsing
    - Microsoft Outlook (for automated email delivery)
  - **Flask Server**:
    - Flask
    - `epySurv` (surveillance data processing module, assumed to be a custom or external library)
    - Other Python dependencies (e.g., `pandas`, `matplotlib` for data processing and plotting)
- **Tools**:
  - Visual Studio 2022 or later (with WPF development workload)
  - PowerShell (for scheduled task execution)
  - Python environment (e.g., Anaconda, virtualenv)
- **Administrative Privileges**: Required for creating scheduled tasks
- **Output Directory**: `C:\Reports` for generated PDFs
- **Scripts Directory**: `C:\Scripts` for PowerShell script (`GenerateReport.ps1`)

## Installation

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
2. **Configure Output and Script Directories**:
   - Create `C:\Reports` for storing generated PDFs.
   - Create `C:\Scripts` and place `GenerateReport.ps1` there (see [Scripts](#scripts)).
   - Update `GenerateReport.ps1` with the correct path to `ForeSITETestApp.exe`:
     ```powershell
     $exePath = "C:\Program Files\ForeSITETestApp\ForeSITETestApp.exe"
     ```
3. **Build the Solution**:
   - Build in Visual Studio (`Build > Build Solution`) or via CLI:
     ```bash
     dotnet build
     ```

### 3. Set Up the Flask Server
1. **Navigate to the Flask Server Directory**:
   - Assumes a `server` folder in the repository with the Flask application (e.g., `app.py`).
   ```bash
   cd server
   ```
2. **Create a Python Virtual Environment**:
   ```bash
   python -m venv venv
   .\venv\Scripts\activate
   ```
3. **Install Dependencies**:
   - Install Flask and `epySurv` (replace with actual dependencies if `epySurv` is custom):
     ```bash
     pip install flask pandas matplotlib epySurv
     ```
   - Create a `requirements.txt` if provided:
     ```bash
     pip install -r requirements.txt
     ```
4. **Run the Flask Server**:
   - Start the server at `http://127.0.0.1:5001`:
     ```bash
     python app.py
     ```
   - Ensure the server is running before using the WPF application.

### 4. Configure Outlook
- Ensure Microsoft Outlook is installed and configured with a default email account.
- Update `GenerateReport.ps1` with the target recipient email:
  ```powershell
  $recipient = "recipient@example.com"
  ```

### 5. Run the Application
- Run the WPF application as administrator to enable task scheduling:
  ```bash
  dotnet run --project ForeSITETestApp
  ```

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

### Scheduling Automated Reports
1. Navigate to the "Scheduler" tab.
2. Click "Scheduling" to create a Windows Task Scheduler task:
   - **Task Name**: `MonthlyReportGeneration`
   - **Schedule**: First day of each month at 8:00 AM
   - **Action**: Runs `GenerateReport.ps1` to generate a PDF (`C:\Reports\Report_YYYYMM.pdf`) and email it via Outlook.
3. Ensure the Flask server is running and the application is run as administrator.

### Manual Task Execution
- Run the scheduled task manually:
  ```cmd
  schtasks /Run /TN MonthlyReportGeneration
  ```
- Check `C:\Reports` for the PDF and the recipient’s email for the attachment.

## Scripts

### GenerateReport.ps1
Located at `C:\Scripts\GenerateReport.ps1`, this PowerShell script:
- Runs `ForeSITETestApp.exe` with the `--generate-report` argument to create a PDF.
- Sends the PDF via Outlook to a specified recipient.
- Example configuration:
  ```powershell
  $exePath = "C:\Program Files\ForeSITETestApp\ForeSITETestApp.exe"
  $outputDir = "C:\Reports"
  $outputFile = Join-Path -Path $outputDir -ChildPath ("Report_" + (Get-Date -Format "yyyyMM") + ".pdf")
  $recipient = "recipient@example.com"
  $subject = "Monthly Report " + (Get-Date -Format "yyyy-MM")
  $body = "Please find the monthly report attached."
  ```

## Flask Server

The Flask server (`server/app.py`) handles:
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
├── server/
│   ├── app.py                 # Flask server for epySurv integration
│   └── requirements.txt       # Python dependencies
├── Scripts/
│   └── GenerateReport.ps1     # PowerShell script for automated report generation and emailing
├── README.md                  # This file
└── LICENSE                    # License file (add your preferred license)
```

## Dependencies

- **WPF Application**:
  - PdfSharpCore: For PDF generation
  - Newtonsoft.Json: For API response parsing
  - Microsoft.Office.Interop.Outlook: For email sending (requires Outlook installed)
- **Flask Server**:
  - Flask: Web server framework
  - epySurv: Surveillance data processing module
  - pandas, matplotlib: Common data processing and plotting libraries (adjust based on `epySurv` requirements)
- **Windows Task Scheduler**: For scheduling automated reports

## Demo Data

The application uses **CDC public surveillance data** for demonstration purposes, processed via the `epySurv` module through the Flask server. Users can:
- Select CDC data as a data source in the "Add Plot" dialog.
- Import custom datasets (e.g., CSV, JSON) via the "Data Source Manager" for surveillance analysis.

## Troubleshooting

- **Font Error in PDF**:
  - If Arial is unavailable, the application falls back to Helvetica or Times New Roman (see `GetSafeFont` in `Dashboard.xaml.cs`).
  - Install `ttf-mscorefonts-installer` on non-Windows systems or embed fonts.
- **Flask Server Not Running**:
  - Ensure the server is running at `http://127.0.0.1:5001` before adding plots.
  - Check Python dependencies: `pip list` and install missing packages.
- **Task Scheduler Fails**:
  - Ensure the application is run as administrator.
  - Verify `C:\Scripts\GenerateReport.ps1` exists and `$exePath` is correct.
  - Run `schtasks /Query /TN MonthlyReportGeneration` to check task status.
- **Email Not Sent**:
  - Confirm Outlook is installed and configured with a default account.
  - Update `$recipient` in `GenerateReport.ps1`.
  - Check for Outlook security prompts and disable via group policy if needed.
- **Plots Not Generated**:
  - Verify the Flask server is running and accessible.
  - Ensure data source configuration (CDC or custom) is correct.
  - Check `epySurv` module documentation for specific requirements.

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

For issues or feature requests, please open an issue on GitHub or contact [your.email@example.com](mailto:your.email@example.com).