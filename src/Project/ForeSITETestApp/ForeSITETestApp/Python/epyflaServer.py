#!/usr/bin/env python3

import ssl

from flask import Flask, request, jsonify, abort
from datetime import datetime, timedelta
import pandas as pd
import logging
import os

import numpy as np
from epysurv.models.timepoint import FarringtonFlexible
import matplotlib.pyplot as plt
import warnings
import requests
from sodapy import Socrata

# -----------------------------------------------------------------------------
# Author: Tao He (tao.he.2008@gmail.com)
#         Jasmine He (he.jasmine.000@gmail.com)
# Copyright (c) 2025 
# -----------------------------------------------------------------------------
# This script implements a Flask server that processes epidemiological data
# and generates plots using the FarringtonFlexible model. It supports both
# simulated data and real CDC data, allowing for outbreak detection and
# visualization of epidemiological trends.

# Create the Flask application instance
app = Flask(__name__)

# Define the port the application will run on
PORT = 5001 # Using 5001 to avoid potential conflicts with default 5000

warnings.filterwarnings("ignore", category=FutureWarning)

documents_path = os.path.join(os.path.expanduser("~"), "Documents")
log_file_path = os.path.join(documents_path, "flask_py_log.txt")
save_folder = os.path.join(documents_path, "ForeSITEAlertingReportFiles")

# Ensure the directory exists and create the log file
os.makedirs(documents_path, exist_ok=True)
os.makedirs(save_folder, exist_ok=True)

if not os.path.exists(log_file_path):
    with open(log_file_path, 'a', encoding='utf-8') as f:
        pass  # Create an empty file

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='[%(asctime)s] [%(levelname)s] %(message)s',
    handlers=[
        logging.FileHandler(log_file_path, mode='a', encoding='utf-8'),
        logging.StreamHandler()  # Also log to console
    ]
)

# Replace print so that print output also goes to the log
def print(*args, **kwargs):
    logging.info(' '.join(str(arg) for arg in args))

def generate_simulation_data(start_date='2019-01-01',
                             end_date='2021-01-05',
                             freq='D',
                             lam=5,
                             outbreak_threshold=10,
                             seed=42):
    """
    Generates simulated epidemiological case data.

    Args:
        start_date (str): Start date for the time series (YYYY-MM-DD).
        end_date (str): End date for the time series (YYYY-MM-DD).
        freq (str): Frequency of data points (pandas frequency string, e.g., 'D' for daily).
        lam (float): Lambda parameter for the Poisson distribution (average number of cases).
        outbreak_threshold (int): Threshold above which cases are considered part of an 'outbreak'
                                 for the 'n_outbreak_cases' calculation.
        seed (int): Random seed for reproducibility.

    Returns:
        pandas.DataFrame: A DataFrame with dates as index and columns 'n_cases'
                          and 'n_outbreak_cases'.
    """
    # Set random seed for reproducibility
    np.random.seed(seed)

    # Create date range
    dates = pd.date_range(start=start_date, end=end_date, freq=freq)

    # Generate case counts using Poisson distribution
    n_cases = np.random.poisson(lam=lam, size=len(dates))
    df = pd.DataFrame({'n_cases': n_cases}, index=dates)

    # Add n_outbreak_cases column based on the threshold
    df['n_outbreak_cases'] = df['n_cases'].apply(lambda x: max(0, x - outbreak_threshold))

    print(f"Generated simulation data from {start_date} to {end_date}.")
    return df

def getCdcData(resourceUri):
    """
    Author: Jasmine He
    Fetches CDC epidemiological case data from a public API.
    Returns:
        pandas.DataFrame: A DataFrame containing the fetched data, or None if an error occurs.
    """
    try:
        app_token="Wa9PucgUy1cHNJgzoTZwhg9AY"
        client = Socrata("data.cdc.gov", app_token=app_token, timeout=60)

        all_results = []
        offset = 0
        while True:
            results = client.get(resourceUri, limit=5000, offset=offset)
            if not results:  # Break if no more data is returned
                break
            all_results.extend(results)
            offset += 5000  # Increment the offset for the next chunk
        results_df = pd.DataFrame.from_records(all_results)
        return results_df
    except requests.exceptions.RequestException as e:
        print(f"Error fetching data: {e}")
        return None

def get_resource_uri(datasource):
    if datasource in ["Covid-19 Deaths", "Pneumonia Deaths", "Flu Deaths"]:
        return "r8kw-7aab"
    else:
        return "local"

def generate_cdc_data(datasource="Covid-19 Deaths", threshold=4000,):
    """
    Author: Jasmine He

    Generates simulated CDC-like epidemiological case data. 
    Args:
        datasource (str): The data source to fetch from CDC. Default is "Covid-19 Deaths".
       
    Returns:
        pandas.DataFrame: A DataFrame with dates as index and columns 'n_cases'
                          and 'n_outbreak_cases'.
    """

    if get_resource_uri(datasource) == "local":
        df2020 = pd.read_csv("local_covid_19_test_data.csv")
        df2020['date'] = pd.to_datetime(df2020['date'])  # Ensure 'date' is datetime type
        df2020 = df2020.set_index('date')
        return df2020
    else:
         cdcdf = getCdcData(resourceUri=get_resource_uri(datasource))
         print("CDC DataFrame:", cdcdf)
         if cdcdf is not None:
             df_week=cdcdf[cdcdf['mmwr_week']>='1']
             df_week_us=df_week[df_week['state']=='United States']
             if datasource == "Covid-19 Deaths":
                   cdcdf=df_week_us[['start_date', 'end_date', 'mmwr_week',  'covid_19_deaths' ]]
                   cdcdf = cdcdf.rename(columns={"covid_19_deaths": "n_cases"})
             elif datasource == "Pneumonia Deaths":   
                   cdcdf=df_week_us[['start_date', 'end_date', 'mmwr_week',  'pneumonia_deaths' ]]
                   cdcdf = cdcdf.rename(columns={"pneumonia_deaths": "n_cases"})
             elif datasource == "Flu Deaths":
                   cdcdf=df_week_us[['start_date', 'end_date', 'mmwr_week',  'influenza_deaths' ]]
                   cdcdf = cdcdf.rename(columns={"influenza_deaths": "n_cases"})        
             else:
                print("Invalid data source provided.")
                return None
             # 转换为 datetime 类型
             cdcdf["start_date"] = pd.to_datetime(cdcdf["start_date"])
             cdcdf["end_date"] = pd.to_datetime(cdcdf["end_date"])
             # 设置 index 为每周开始时间（推荐）
             df = cdcdf.set_index("start_date")
             df = cdcdf.sort_index()
             df['n_cases'] = df['n_cases'].astype(int)
             # 计算每周的病例数
             df['n_outbreak_cases'] = df['n_cases'].apply(lambda x: 0 if x <= threshold else x - threshold)
             return df
         else:
             print("No data found for the specified data source.")
             return None

   
 
    
def run_farrington_model(df, train_split_ratio=0.8, alpha=0.05, years_back=1):
    """
    Trains the FarringtonFlexible model and generates predictions.

    Args:
        df (pd.DataFrame): Input data with DatetimeIndex and 'n_cases' column.
        train_split_ratio (float): Proportion of data for training.
        alpha (float): Significance level.
        years_back (int): Years for baseline reference.

    Returns:
        df_full (pd.DataFrame): Original dataframe with expected and threshold values.
        predictions (pd.DataFrame): Model predictions, including 'alarm' and 'upperbound'.
        train (pd.DataFrame): Training portion of the original data.
    """
    if not isinstance(df.index, pd.DatetimeIndex):
        raise ValueError("Input DataFrame must have a DatetimeIndex.")
    if 'n_cases' not in df.columns:
        raise ValueError("Input DataFrame must contain an 'n_cases' column.")
    if not (0 < train_split_ratio < 1):
        raise ValueError("train_split_ratio must be between 0 and 1 (exclusive).")

    train_size = int(len(df) * train_split_ratio)
    if train_size == 0 or train_size == len(df):
        raise ValueError("Data size or train_split_ratio results in empty train/test set.")

    train = df.iloc[:train_size].copy()
    test = df.iloc[train_size:].copy()

    model = FarringtonFlexible(alpha=alpha, years_back=years_back)
    model.fit(train)
    predictions = model.predict(test)

    df_full = df.copy()
    df_full['threshold'] = np.nan
    df_full.loc[predictions.index, 'threshold'] = predictions['upperbound']
    df_full['expected'] = train['n_cases'].mean()

    return df_full, predictions, train

def run_farrington_model_bydatesplit(df, train_end_date, alpha=0.05, years_back=1):
    """
    Trains the FarringtonFlexible model and generates predictions.

    Args:
        df (pd.DataFrame): Input data with DatetimeIndex and 'n_cases' column.
        train_end_date (str): End date for the training set (YYYY-MM-DD).
        alpha (float): Significance level.
        years_back (int): Years for baseline reference.

    Returns:
        df_full (pd.DataFrame): Original dataframe with expected and threshold values.
        predictions (pd.DataFrame): Model predictions, including 'alarm' and 'upperbound'.
        train (pd.DataFrame): Training portion of the original data.
    """
    if not isinstance(df.index, pd.DatetimeIndex):
        raise ValueError("Input DataFrame must have a DatetimeIndex.")
    if 'n_cases' not in df.columns:
        raise ValueError("Input DataFrame must contain an 'n_cases' column.")
 
    print(df.index)
    # 用 train/test date 训练
    train = df.loc[:train_end_date, ['n_cases', 'n_outbreak_cases']].copy()
    test = df.loc[train_end_date:, ['n_cases', 'n_outbreak_cases']].copy()
    print(train.index)
    print("Train size:", len(train), "Test size:", len(test))
    print(test.index)

    model = FarringtonFlexible(alpha=alpha, years_back=years_back)
    print("Fitting FarringtonFlexible model...", model)
    model.fit(train)
    print("Model fitting complete.")
    predictions = model.predict(test)
    print("Predictions:", predictions)
  
    df_full = df.copy()
    df_full['threshold'] = np.nan
    df_full.loc[predictions.index, 'threshold'] = predictions['upperbound']
    df_full['expected'] = train['n_cases'].mean()
    print(df_full)

    return df_full, predictions, train


def plot_farrington_results(df_full,
                            predictions,
                            train,
                            save_path,
                            alpha=0.05,
                            plot_title='FarringtonFlexible Model: Case Detection Plot',
                            xlabel='Date',
                            ylabel='Number of Cases'):
    """
    Generates and saves a plot based on the output of the FarringtonFlexible model.

    Args:
        df_full (pd.DataFrame): Full dataset with 'n_cases', 'expected', and 'threshold'.
        predictions (pd.DataFrame): Prediction output with 'upperbound' and optionally 'alarm'.
        train (pd.DataFrame): The training set used for computing expectations.
        save_path (str): File path for saving the plot image.
        alpha (float): Significance level for labeling.
        plot_title (str): Title of the plot.
        xlabel (str): Label for the x-axis.
        ylabel (str): Label for the y-axis.

    Returns:
        None
    """
    expected_value = train['n_cases'].mean()
    plt.figure(figsize=(12, 6))

    # Actual cases
    plt.plot(df_full.index, df_full['n_cases'], label='Actual Cases', color='blue', marker='o', markersize=4, linestyle='-')

    # Expected
    plt.plot(df_full.index, df_full['expected'], label=f'Expected Cases (Train Mean = {expected_value:.2f})', color='green', linestyle='--')

    # Threshold
    plt.plot(df_full.index, df_full['threshold'], label=f'Threshold (alpha={alpha})', color='red', linestyle='--')

    # Alert zone
    fill_indices = df_full['threshold'].dropna().index
    if not fill_indices.empty:
        plt.fill_between(fill_indices,
                         df_full.loc[fill_indices, 'expected'],
                         df_full.loc[fill_indices, 'threshold'],
                         where=df_full.loc[fill_indices, 'threshold'] >= df_full.loc[fill_indices, 'expected'],
                         color='red', alpha=0.1, label='Alert Zone')

    # Alarms
    if 'alarm' in predictions.columns:
        alarm_indices = predictions[predictions['alarm']].index
        outliers = df_full.loc[alarm_indices]
        if not outliers.empty:
            plt.scatter(outliers.index, outliers['n_cases'], color='purple', label='Alarms', zorder=5, s=50)

    # Decorations
    plt.legend()
    plt.title(plot_title, fontsize=14)
    plt.xlabel(xlabel, fontsize=12)
    plt.ylabel(ylabel, fontsize=12)
    plt.grid(True, linestyle='--', alpha=0.7)
    plt.tight_layout()

    save_dir = os.path.dirname(save_path)
    if save_dir and not os.path.exists(save_dir):
        os.makedirs(save_dir)

    try:
        plt.savefig(save_path, dpi=300, bbox_inches='tight')
        print(f"Plot saved to: {save_path}")
    except Exception as e:
        print(f"Failed to save plot: {e}")
    finally:
        plt.close()

def generate_plot_from_data(df,
                            save_path,
                            train_split_ratio=0.8,
                            alpha=0.05,
                            years_back=1,
                            plot_title='FarringtonFlexible Model: Case Detection Plot',
                            xlabel='Date',
                            ylabel='Number of Cases'):
    """
    Trains a FarringtonFlexible model on the provided data, makes predictions,
    and generates a plot visualizing the results, saving it to a file.

    Args:
        df (pandas.DataFrame): DataFrame containing the time series data.
                               Must have a DateTimeIndex and a column named 'n_cases'.
        save_path (str): The full path (including filename and extension, e.g., .png)
                         where the plot image will be saved.
        train_split_ratio (float): Proportion of the data to use for training (0 to 1).
        alpha (float): Significance level for the Farrington model's threshold calculation.
        years_back (int): Number of previous years' data to consider for the baseline
                          in the Farrington model.
        plot_title (str): Title for the generated plot.
        xlabel (str): Label for the x-axis.
        ylabel (str): Label for the y-axis.

    Returns:
        None: The function saves the plot to the specified file path.
    """
    if not isinstance(df.index, pd.DatetimeIndex):
        raise ValueError("Input DataFrame must have a DatetimeIndex.")
    if 'n_cases' not in df.columns:
        raise ValueError("Input DataFrame must contain an 'n_cases' column.")
    if not (0 < train_split_ratio < 1):
        raise ValueError("train_split_ratio must be between 0 and 1 (exclusive).")

    # Split training and testing data
    train_size = int(len(df) * train_split_ratio)
    if train_size == 0 or train_size == len(df):
        raise ValueError("Data size or train_split_ratio results in an empty train or test set.")

    train = df.iloc[:train_size].copy()
    test = df.iloc[train_size:].copy()

    print(f"Splitting data: {len(train)} training points, {len(test)} testing points.")

    # Initialize and fit the FarringtonFlexible model
    


    model = FarringtonFlexible(alpha=alpha, years_back=years_back)
    print("Fitting FarringtonFlexible model...")
    model.fit(train)
    print("Model fitting complete.")

    # Predict on the test set
    print("Making predictions...")
    predictions = model.predict(test)
    print("Predictions complete.")
    # print("Prediction Columns:", predictions.columns) # Optional: for debugging

    # Prepare data for visualization
    df_full = df.copy()

    # Add threshold column - only for the test period where predictions exist
    df_full['threshold'] = np.nan # Initialize with NaN
    # Align prediction index with df_full index before assigning
    common_index = predictions.index.intersection(df_full.index)
    df_full.loc[common_index, 'threshold'] = predictions.loc[common_index, 'upperbound']

    # Approximate expected cases using the mean of the training data
    expected_value = train['n_cases'].mean()
    df_full['expected'] = expected_value # Apply to the whole series for plotting continuity

    # Visualization
    plt.figure(figsize=(12, 6))

    # Plot actual cases
    plt.plot(df_full.index, df_full['n_cases'], label='Actual Cases', color='blue', marker='o', markersize=4, linestyle='-')

    # Plot expected cases
    plt.plot(df_full.index, df_full['expected'], label=f'Expected Cases (Train Mean = {expected_value:.2f})', color='green', linestyle='--')

    # Plot threshold line (only where it exists - test period)
    plt.plot(df_full.index, df_full['threshold'], label=f'Threshold (alpha={alpha})', color='red', linestyle='--')

    # Fill the alert zone (between expected and threshold, only in the test period)
    # Ensure we only fill where threshold is not NaN
    fill_indices = df_full['threshold'].dropna().index
    if not fill_indices.empty:
         # Ensure 'expected' values are available for these indices
        expected_for_fill = df_full.loc[fill_indices, 'expected']
        threshold_for_fill = df_full.loc[fill_indices, 'threshold']
        plt.fill_between(fill_indices, expected_for_fill, threshold_for_fill,
                         where=threshold_for_fill >= expected_for_fill, # Only fill where threshold > expected
                         color='red', alpha=0.1, label='Alert Zone')

    # Highlight outliers/alarms found in the prediction period
    if 'alarm' in predictions.columns:
        alarm_indices = predictions[predictions['alarm']].index
        outliers = df_full.loc[alarm_indices]
        if not outliers.empty:
            plt.scatter(outliers.index, outliers['n_cases'], color='purple', label='Alarms', zorder=5, s=50) # Increased size

    # Add plot elements
    plt.legend()
    plt.title(plot_title, fontsize=14)
    plt.xlabel(xlabel, fontsize=12)
    plt.ylabel(ylabel, fontsize=12)
    plt.grid(True, linestyle='--', alpha=0.7)
    plt.tight_layout()

    # Ensure the save directory exists
    save_dir = os.path.dirname(save_path)
    if save_dir and not os.path.exists(save_dir):
        os.makedirs(save_dir)
        print(f"Created directory: {save_dir}")

    # Save the plot
    try:
        plt.savefig(save_path, dpi=300, bbox_inches='tight')
        print(f"Plot saved successfully to: {save_path}")
    except Exception as e:
        print(f"Error saving plot to {save_path}: {e}")
    finally:
        plt.close() # Close the plot to free memory


@app.route('/epyapi', methods=['POST'])
def process_json():
    """
    API endpoint to process incoming JSON data over HTTPS from localhost.
    """
    # 1. 检查是否来自 localhost
    if request.remote_addr != '127.0.0.1':
        print(f"Rejected request from non-localhost: {request.remote_addr}")
        abort(403, description="Only localhost requests are allowed.")

    # 2. 检查 Content-Type
    if not request.is_json:
        print(f"Invalid Content-Type: {request.content_type}")
        abort(400, description="Content-Type must be application/json.")

    # 3. 尝试获取 JSON 数据
    try:
        received_data = request.get_json()
        print(f"Received JSON data: {received_data}")

        if received_data is None:
            raise ValueError("Empty or malformed JSON.")
    except Exception as e:
        print(f"Error parsing JSON: {e}")
        abort(400, description=f"Invalid JSON: {e}")

    # 4. 检查 graph 字段
    if "graph" not in received_data:
        abort(400, description="Missing required field: 'graph'")

  

    # 5. 提取参数（带默认值）
    try:
        graph = received_data["graph"]
        print(f"Graph type: {graph}")
        model = graph.get("Model", "farrington")
        datasource = graph.get("DataSource", "Covid-19 Deaths")
        title = graph.get("Title", "Farrington Outbreak Detection Simulation")
        yearback = int(graph.get("YearBack", 3))
        useTrainSplit = graph.get("UseTrainSplit", False)
        threshold = int(graph.get("Threshold", 1500))
        trainSplitRatio = float(graph.get("TrainSplitRatio", 0.70))
        train_end_date = datetime(2024, 12, 31)

        print(f"Model: {model}, DataSource: {datasource}, Title: {title}, YearBack: {yearback}, \
                UseTrainSplit: {useTrainSplit}, Threshold: {threshold}, TrainSplitRatio: {trainSplitRatio}")

        if not useTrainSplit:
            train_end_date = pd.to_datetime(graph.get("TrainEndDate"))
           
            print(f"Using TrainEndDate: {train_end_date}")

    except Exception as e:
        print(f"Parameter error: {e}")
        abort(400, description=f"Invalid parameters: {e}")

    import uuid

    unique_id = uuid.uuid4().hex[:8]

    # 6. 生成图像路径
    output_plot_path = (
    f"farrington_covid19test_plot_{unique_id}.png"
    if datasource == "Covid-19 Tests"
    else f"farrington_death_plot_{unique_id}.png")
    print(f"Output plot path: {output_plot_path}")

    save_img = os.path.join(save_folder, output_plot_path) 
    try:
        # 7. 根据数据源处理数据
        print(datasource)
        if datasource == "Covid-19 Tests":
            print("Using local data source for COVID-19 test data.")
            df2020 = pd.read_csv("local_covid_19_test_data.csv")
            df2020['date'] = pd.to_datetime(df2020['date'])  # Ensure 'date' is datetime type
            df2020 = df2020.set_index('date')
            #print(df2020)
            generate_plot_from_data(
                df=df2020,
                save_path=save_img,
                train_split_ratio=trainSplitRatio,
                alpha=0.05,
                years_back=yearback,
                plot_title=title
            )
        else:
                # 7. 获取数据
            try:
               cdc_data = generate_cdc_data(datasource, threshold=threshold)
               # Create a complete date range (daily frequency)
              
               cdc_data['start_date'] = pd.to_datetime(cdc_data['start_date'])  # Ensure 'date' is datetime type
               cdc_data = cdc_data.set_index('start_date')
               cdc_data.index = pd.date_range(start=cdc_data.index[0], periods=len(cdc_data), freq='W-SUN')

               
               print(cdc_data)
               print(cdc_data.index)
        
            except Exception as e:
               print(f"Data generation failed: {e}")
               abort(500, description="Data generation failed.")

            if useTrainSplit:
                df_full, predictions, train = run_farrington_model(
                    cdc_data,
                    train_split_ratio=trainSplitRatio,
                    alpha=0.05,
                    years_back=yearback
                )
            else:
                
                df_full, predictions, train = run_farrington_model_bydatesplit(
                    cdc_data,
                    train_end_date=train_end_date.strftime("%Y-%m-%d"),
                    alpha=0.05,
                    years_back=yearback
                )

            plot_farrington_results(
                df_full, predictions, train,
                save_path=save_img,
                alpha=0.05,
                plot_title=title,
                xlabel='Date',
                ylabel='Number of Cases'
            )
    except Exception as e:
        print(f"Plot generation error: {e}")
        abort(500, description=f"Plot generation failed: {e}")

    # 8. 返回响应
    response_data = received_data.copy()
    response_data.update({
        'status': 'processed',
        'message': 'Plot generated successfully.',
        'plot_path':  save_img,

    })

    print(f"Response data: {response_data}")
    return jsonify(response_data), 200

@app.route('/shutdown', methods=['POST'])
def shutdown():
    os._exit(0)  # Forcefully exit the Flask process
    return 'Server shutting down...'

@app.errorhandler(400)
def bad_request(error):
    response = jsonify({'error': 'Bad Request', 'message': error.description})
    response.status_code = 400
    return response

@app.errorhandler(403)
def forbidden(error):
    response = jsonify({'error': 'Forbidden', 'message': error.description})
    response.status_code = 403
    return response

@app.errorhandler(405) # Method Not Allowed (e.g., GET request to /process)
def method_not_allowed(error):
     response = jsonify({'error': 'Method Not Allowed', 'message': 'This endpoint only supports POST requests.'})
     response.status_code = 405
     return response

if __name__ == '__main__':
    print(f"Starting Epy Flask server on https://localhost:{PORT}")
    print("Only accepting JSON POST requests to /process from localhost.")

    # --- Option 1: Use Flask's ad-hoc SSL certificate (Easy for Development) ---
    # This generates temporary self-signed certificates.
    # Your browser/client will likely show warnings.
    context = 'adhoc'

    # --- Option 2: Use your own self-signed certificates (More Stable Dev) ---
    # Generate with openssl:
    # openssl req -x509 -newkey rsa:4096 -nodes -out cert.pem -keyout key.pem -days 365 \
    #   -subj "/C=US/ST=YourState/L=YourCity/O=YourOrg/OU=Dev/CN=localhost"
    # context = ('cert.pem', 'key.pem')
    # Make sure cert.pem and key.pem are in the same directory as the script.

    try:
         # Run the app:
         # host='127.0.0.1' ensures it only listens on the loopback interface.
         # ssl_context enables HTTPS.
         app.run(host='127.0.0.1', port=PORT, debug=True)
    except ImportError:
         print("Error: 'cryptography' library not found.")
         print("Please install it for ad-hoc SSL certificate generation:")
         print("  pip install cryptography")
    except FileNotFoundError:
         print("Error: Could not find 'cert.pem' or 'key.pem'.")
         print("Make sure certificate files are generated and in the correct path if using Option 2.")
    except Exception as e:
         print(f"An error occurred during server startup: {e}")