#!/usr/bin/env python3

import ssl
import sys
import io
import traceback
import contextlib
import subprocess
import json


from flask import Flask, request, jsonify, abort
from datetime import datetime, timedelta
import pandas as pd
import logging
import os
import tempfile
import uuid
import base64

import sqlite3

import numpy as np
from epysurv.models.timepoint import FarringtonFlexible
import matplotlib
matplotlib.use('Agg')  # Use non-interactive backend
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

# Global namespace for persistent variables across code executions
GLOBAL_NAMESPACE = {
    '__builtins__': __builtins__,
    'np': np,
    'pd': pd,
    'plt': plt,
    'datetime': datetime,
    'os': os,
    'sys': sys,
    'json': json
}

# Database configuration
DATABASE_PATH = os.path.join(os.getcwd(), "foresite_alerting.db")
print(f"Database path: {DATABASE_PATH}")

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

class SafeStringIO(io.StringIO):
    """StringIO that captures both stdout and stderr safely"""
    def __init__(self):
        super().__init__()
        self.outputs = []
    
    def write(self, s):
        if s and s.strip():
            self.outputs.append(s)
        return super().write(s)
    
    def get_output(self):
        return ''.join(self.outputs)

def split_code_into_blocks(code):
    """
    Split code into logical blocks that respect Python's indentation structure.
    This ensures multi-line constructs like if/for/while blocks stay together.
    """
    lines = code.split('\n')
    blocks = []
    current_block = []
    
    i = 0
    while i < len(lines):
        line = lines[i]
        stripped = line.strip()
        
        # Skip empty lines
        if not stripped:
            if current_block:
                current_block.append(line)
            i += 1
            continue
            
        # Skip comments but preserve them
        if stripped.startswith('#'):
            if current_block:
                current_block.append(line)
            else:
                blocks.append(line)
            i += 1
            continue
        
        # Calculate indentation
        indent = len(line) - len(line.lstrip())
        
        # Check if this line starts a block (ends with colon)
        if stripped.endswith(':'):
            # This line starts a new block, collect it and its indented content
            current_block = [line]
            base_indent = indent
            i += 1
            
            # Collect all lines that belong to this block
            while i < len(lines):
                next_line = lines[i]
                next_stripped = next_line.strip()
                
                # Skip empty lines and comments within the block
                if not next_stripped or next_stripped.startswith('#'):
                    current_block.append(next_line)
                    i += 1
                    continue
                
                next_indent = len(next_line) - len(next_line.lstrip())
                
                # If this line is indented more than the base, it belongs to the block
                if next_indent > base_indent:
                    current_block.append(next_line)
                    i += 1
                elif next_indent == base_indent and next_stripped.startswith(('elif', 'else', 'except', 'finally')):
                    # Special case for elif, else, except, finally
                    current_block.append(next_line)
                    i += 1
                else:
                    # This line is at the same or lesser indentation, end the block
                    break
            
            # Add the completed block
            blocks.append('\n'.join(current_block))
            current_block = []
        else:
            # Single line statement
            if current_block:
                blocks.append('\n'.join(current_block))
                current_block = []
            blocks.append(line)
            i += 1
    
    # Add any remaining block
    if current_block:
        blocks.append('\n'.join(current_block))
    
    return blocks

def incomplete_statement(line):
    """
    Check if a line appears to be an incomplete statement that continues on the next line.
    """
    line = line.strip()
    if not line:
        return False
    
    # Check for unmatched parentheses, brackets, or braces
    parens = line.count('(') - line.count(')')
    brackets = line.count('[') - line.count(']')
    braces = line.count('{') - line.count('}')
    
    return parens > 0 or brackets > 0 or braces > 0

def is_single_expression(code_block):
    """
    Check if a code block is a single expression that can be evaluated.
    """
    try:
        # Remove leading/trailing whitespace and split into lines
        lines = [line.strip() for line in code_block.strip().split('\n') if line.strip()]
        
        # Multi-line blocks are not single expressions
        if len(lines) > 1:
            return False

        if not lines:
            return False
        
        line = lines[0]
        
        # Check if it's a statement keyword
        statement_keywords = [
            'if', 'elif', 'else', 'for', 'while', 'def', 'class', 
            'try', 'except', 'finally', 'with', 'import', 'from',
            'return', 'yield', 'raise', 'assert', 'del', 'pass',
            'break', 'continue', 'global', 'nonlocal'
        ]
        
        first_word = line.split()[0] if line.split() else ''
        if first_word in statement_keywords:
            return False
        
        # Check if it ends with colon (usually indicates a statement)
        if line.endswith(':'):
            return False
        
        # Check if it contains assignment (but not comparison operators)
        if '=' in line:
            # More sophisticated assignment detection
            # Skip if it's a comparison operator
            assignment_ops = ['=', '+=', '-=', '*=', '/=', '%=', '**=', '//=', '&=', '|=', '^=', '<<=', '>>=']
            comparison_ops = ['==', '!=', '<=', '>=', '<', '>']
            
            # Check if any assignment operator is present (but not comparison)
            has_assignment = False
            for op in assignment_ops:
                if op in line:
                    # Make sure it's not part of a comparison operator
                    if op == '=' and ('==' in line or '!=' in line or '<=' in line or '>=' in line):
                        continue
                    has_assignment = True
                    break
            
            if has_assignment:
                return False
        
        # Try to compile as expression
        try:
            compile(line, '<string>', 'eval')
            return True
        except SyntaxError:
            return False
            
    except:
        return False

def execute_python_code(code, timeout=30):
    """
    Execute Python code safely and return the result.
    
    Args:
        code (str): Python code to execute
        timeout (int): Maximum execution time in seconds
        
    Returns:
        dict: Contains success status, output, error, and result
    """
    try:
        # Create string buffers to capture output
        stdout_capture = SafeStringIO()
        stderr_capture = SafeStringIO()
        
        # Store original stdout/stderr
        original_stdout = sys.stdout
        original_stderr = sys.stderr
        
        result = {
            'success': True,
            'output': '',
            'error': '',
            'result': ''
        }
        
        # Initialize variables that might be referenced in finally block
        last_expr_result = None
        local_namespace = {}

        try:
            # Redirect stdout and stderr
            sys.stdout = stdout_capture
            sys.stderr = stderr_capture

            # Clean and validate the code
            code = code.strip()
            if not code:
                result['output'] = "No code to execute"
                return result
            
            # First, try to validate the syntax
            try:
                compile(code, '<string>', 'exec')
            except SyntaxError as e:
                result['success'] = False
                result['error'] = f"Syntax Error: {str(e)}"
                return result

            
            
            # Split code into logical blocks (respecting indentation)
            code_blocks = split_code_into_blocks(code)


            for i, block in enumerate(code_blocks):
                if not block.strip() or block.strip().startswith('#'):
                    continue
                    
                try:
                    # For the last block, try to evaluate as expression first
                    if i == len(code_blocks) - 1 and is_single_expression(block):
                        try:
                            compiled_expr = compile(block, '<string>', 'eval')
                            last_expr_result = eval(compiled_expr, GLOBAL_NAMESPACE, local_namespace)
                            if last_expr_result is not None:
                                print(repr(last_expr_result))
                        except SyntaxError:
                            # If it's not an expression, execute as statement
                            compiled_stmt = compile(block, '<string>', 'exec')
                            exec(compiled_stmt, GLOBAL_NAMESPACE, local_namespace)
                    else:
                        # Execute as statement
                        compiled_stmt = compile(block, '<string>', 'exec')
                        exec(compiled_stmt, GLOBAL_NAMESPACE, local_namespace)
                        
                except Exception as e:
                    error_msg = f"Error in code block: {str(e)}\nBlock content:\n{block}"
                    print(error_msg, file=sys.stderr)
                    print(traceback.format_exc(), file=sys.stderr)
                    result['success'] = False
                    break
            
            # Update global namespace with local variables
            GLOBAL_NAMESPACE.update(local_namespace)
            
        except Exception as e:
            print(f"Execution error: {str(e)}", file=sys.stderr)
            print(traceback.format_exc(), file=sys.stderr)
            result['success'] = False
            
        finally:
            # Restore original stdout/stderr
            sys.stdout = original_stdout
            sys.stderr = original_stderr
            
            # Get captured output
            result['output'] = stdout_capture.get_output()
            result['error'] = stderr_capture.get_output()
            
            if last_expr_result is not None and result['success']:
                result['result'] = format_result_for_display(last_expr_result)
            
        return result
        
    except Exception as e:
        return {
            'success': False,
            'output': '',
            'error': f"System error: {str(e)}",
            'result': ''
        }

def display_result(obj):
    """
    Enhanced display function for different Python objects.
    """
    if isinstance(obj, pd.DataFrame):
        print("DataFrame Info:")
        print(f"Shape: {obj.shape}")
        print(f"Columns: {list(obj.columns)}")
        print("\nData:")
        # Use pandas' to_string() for better formatting
        if len(obj) > 0:
            # Show first few rows with proper formatting
            display_df = obj.head(10)  # Show up to 10 rows
            print(display_df.to_string())
        else:
            print("DataFrame is empty")
            
        # Show index info if it's a DatetimeIndex
        if isinstance(obj.index, pd.DatetimeIndex):
            print(f"\nIndex: DatetimeIndex")
            print(f"Date range: {obj.index.min()} to {obj.index.max()}")
            
    elif isinstance(obj, pd.Series):
        print("Series Info:")
        print(f"Length: {len(obj)}")
        print(f"Name: {obj.name}")
        print("\nData:")
        if len(obj) > 0:
            # Show first few values
            display_series = obj.head(10)
            print(display_series.to_string())
        else:
            print("Series is empty")
            
    elif isinstance(obj, (list, tuple)) and len(obj) > 0:
        print(f"{type(obj).__name__} with {len(obj)} items:")
        # Show first few items
        for i, item in enumerate(obj[:10]):
            print(f"  [{i}]: {repr(item)}")
        if len(obj) > 10:
            print(f"  ... and {len(obj) - 10} more items")
            
    elif isinstance(obj, dict):
        print(f"Dictionary with {len(obj)} keys:")
        for i, (key, value) in enumerate(obj.items()):
            if i >= 10:  # Limit display
                print(f"  ... and {len(obj) - 10} more items")
                break
            print(f"  {repr(key)}: {repr(value)}")
            
    elif hasattr(obj, '__array__'):  # NumPy arrays
        print(f"Array Info:")
        print(f"Shape: {obj.shape}")
        print(f"Dtype: {obj.dtype}")
        print("Data:")
        print(obj)
        
    else:
        # Default representation
        print(repr(obj))

def format_result_for_display(obj):
    """
    Format the result object for the result field in JSON response.
    """
    if isinstance(obj, pd.DataFrame):
        if len(obj) > 0:
            return f"DataFrame({obj.shape[0]} rows × {obj.shape[1]} columns)"
        else:
            return "Empty DataFrame"
    elif isinstance(obj, pd.Series):
        return f"Series(length={len(obj)}, name='{obj.name}')"
    elif isinstance(obj, (list, tuple)):
        return f"{type(obj).__name__}(length={len(obj)})"
    elif isinstance(obj, dict):
        return f"dict(keys={len(obj)})"
    elif hasattr(obj, '__array__'):
        return f"Array(shape={obj.shape}, dtype={obj.dtype})"
    else:
        return str(obj)

def get_data_source_by_name_from_db(name):
    """
    Get a specific data source by name from the database
    """
    try:
        with sqlite3.connect(DATABASE_PATH) as conn:
            cursor = conn.cursor()
            cursor.execute('''
                SELECT Name, DataURL, ResourceURL, IsRealtime, CreatedDate, LastUpdated 
                FROM DataSources 
                WHERE Name = ? COLLATE NOCASE
            ''', (name,))
            
            row = cursor.fetchone()
            if row:
                return {
                    'name': row[0],
                    'data_url': row[1] if row[1] else "",
                    'resource_url': row[2] if row[2] else "",
                    'is_realtime': bool(row[3]),
                    'created_date': row[4] if row[4] else "",
                    'last_updated': row[5] if row[5] else ""
                }
            return None
            
    except Exception as e:
        print(f"Error retrieving data source '{name}' from database: {e}")
        return None

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
    if datasource in ["COVID-19 Deaths", "Pneumonia Deaths", "Flu Deaths"]:
        return "r8kw-7aab"
    else:
        return "local"

def generate_cdc_data(datasource="COVID-19 Deaths", threshold=4000,):
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
             if datasource == "COVID-19 Deaths":
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
    output_plot_path = (f"farrington_plot_{unique_id}.png")
    print(f"Output plot path: {output_plot_path}")

    save_img = os.path.join(save_folder, output_plot_path) 
    try:
        # 7. 根据数据源处理数据
        print(datasource)
        if datasource == "COVID-19 Tests":
            print("Using local data source for COVID-19 test data.")
            print("Current working directory:", os.getcwd())
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
        elif datasource in ["COVID-19 Deaths", "Pneumonia Deaths", "Flu Deaths"]:
        # 7. 获取数据
            print(datasource)
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
        else:
            # Check database for custom data source
            print(f"Looking up custom data source '{datasource}' in database...")
            db_datasource = get_data_source_by_name_from_db(datasource)
            
            if db_datasource:
                print(f"Found data source in database: {db_datasource}")
                
                # Get the DataURL from database
                data_url = db_datasource.get('data_url', '')
                
                if not data_url:
                    raise ValueError(f"Data source '{datasource}' found in database but has no DataURL")
                
                print(f"Loading CSV data from: {data_url}")
                
                # Check if it's a local file path or URL
                if os.path.isfile(data_url):
                    # Local file
                    print(f"Loading local CSV file: {data_url}")
                    custom_data = pd.read_csv(data_url)
                elif data_url.startswith(('http://', 'https://')):
                    # Remote URL
                    print(f"Loading CSV from URL: {data_url}")
                    custom_data = pd.read_csv(data_url)
                else:
                    # Try as relative path
                    print(f"Trying as relative path: {data_url}")
                    if os.path.isfile(data_url):
                        custom_data = pd.read_csv(data_url)
                    else:
                        raise FileNotFoundError(f"CSV file not found: {data_url}")
                
                # Process the loaded data
                print(f"Loaded CSV data with shape: {custom_data.shape}")
                print(f"Columns: {custom_data.columns.tolist()}")

                
                
                # Try to identify date and case columns
                date_column = None
                case_column = None
                
                # Look for common date column names
                for col in custom_data.columns:
                    if col.lower() in ['date', 'dates', 'time', 'timestamp', 'start_date']:
                        date_column = col
                        break
                
                # Look for common case column names
                for col in custom_data.columns:
                    if col.lower() in ['cases', 'n_cases', 'count', 'value', 'deaths', 'cases_count']:
                        case_column = col
                        break
                
                if not date_column:
                    # Use first column as date
                    date_column = custom_data.columns[0]
                    print(f"No date column found, using first column as date: {date_column}")
                
                if not case_column:
                    # Use second column as cases, or first numeric column
                    numeric_cols = custom_data.select_dtypes(include=[np.number]).columns
                    if len(numeric_cols) > 0:
                        case_column = numeric_cols[0]
                    else:
                        case_column = custom_data.columns[1] if len(custom_data.columns) > 1 else custom_data.columns[0]
                    print(f"No case column found, using: {case_column}")
                
                # Process the data
                try:
                    custom_data[date_column] = pd.to_datetime(custom_data[date_column])
                    custom_data = custom_data.set_index(date_column)
                    
                    # Rename case column to standard name
                    if case_column != 'n_cases':
                        custom_data = custom_data.rename(columns={case_column: 'n_cases'})
                    
                    # Ensure n_cases is numeric
                    custom_data['n_cases'] = pd.to_numeric(custom_data['n_cases'], errors='coerce')
                    
                    # Add outbreak cases column
                    custom_data['n_outbreak_cases'] = custom_data['n_cases'].apply(
                        lambda x: max(0, x - threshold) if pd.notna(x) else 0
                    )
                    
                    # Remove any rows with NaN values
                    custom_data = custom_data.dropna(subset=['n_cases'])
                    
                    print(f"Processed data shape: {custom_data.shape}")
                    print(f"Date range: {custom_data.index.min()} to {custom_data.index.max()}")
                    print(f"Case range: {custom_data['n_cases'].min()} to {custom_data['n_cases'].max()}")
                    
                except Exception as e:
                    print(f"Error processing CSV data: {e}")
                    raise ValueError(f"Failed to process CSV data from '{data_url}': {str(e)}")
                
                # Generate plot using the custom data
                if useTrainSplit:
                    df_full, predictions, train = run_farrington_model(
                        custom_data,
                        train_split_ratio=trainSplitRatio,
                        alpha=0.05,
                        years_back=yearback
                    )
                else:
                    df_full, predictions, train = run_farrington_model_bydatesplit(
                        custom_data,
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
                
            else:
                # Data source not found in database
                error_msg = f"Data source '{datasource}' not found in known sources or database"
                print(error_msg)
                abort(400, description=error_msg)

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

def handle_matplotlib_plots():
    """
    Handle matplotlib plots by saving them and returning base64 data
    """
    try:
        if plt.get_fignums():  # Check if there are any open figures
            # Save current figure to base64
            buffer = io.BytesIO()
            plt.savefig(buffer, format='png', dpi=150, bbox_inches='tight')
            buffer.seek(0)
            
            # Convert to base64
            plot_data = base64.b64encode(buffer.getvalue()).decode()
            
            # Also save to file
            plot_filename = f"plot_{uuid.uuid4().hex[:8]}.png"
            plot_path = os.path.join(save_folder, plot_filename)
            plt.savefig(plot_path, dpi=150, bbox_inches='tight')
            
            plt.close('all')  # Close all figures
            
            return {
                'has_plot': True,
                'plot_data': plot_data,
                'plot_path': plot_path
            }
    except Exception as e:
        print(f"Error handling matplotlib plots: {str(e)}")
    
    return {'has_plot': False}

# New route for code execution
@app.route('/execute', methods=['POST'])
def execute_code():
    """
    API endpoint to execute Python code from the notebook client.
    """
    # Check if request is from localhost
    if request.remote_addr != '127.0.0.1':
        print(f"Rejected request from non-localhost: {request.remote_addr}")
        abort(403, description="Only localhost requests are allowed.")

    # Check Content-Type
    if not request.is_json:
        print(f"Invalid Content-Type: {request.content_type}")
        abort(400, description="Content-Type must be application/json.")

    try:
        received_data = request.get_json()
        print(f"Received execution request: {received_data}")

        if received_data is None:
            raise ValueError("Empty or malformed JSON.")
    except Exception as e:
        print(f"Error parsing JSON: {e}")
        abort(400, description=f"Invalid JSON: {e}")

    # Extract code from request
    if "code" not in received_data:
        abort(400, description="Missing required field: 'code'")

    code = received_data["code"]
    cell_type = received_data.get("cell_type", "code")

    print(f"Executing {cell_type} cell with code length: {len(code)}")

    try:
        # Execute the code
        if cell_type == "code":
            result = execute_python_code(code)
            
            # Check for matplotlib plots
            plot_info = handle_matplotlib_plots()
            if plot_info['has_plot']:
                result['plot_data'] = plot_info['plot_data']
                result['plot_path'] = plot_info['plot_path']
                result['has_plot'] = True
            else:
                result['has_plot'] = False
            
        else:
            # For non-code cells, just return the content
            result = {
                'success': True,
                'output': f"Rendered {cell_type} cell",
                'error': '',
                'result': code
            }

        print(f"Execution completed. Success: {result['success']}")
        return jsonify(result), 200

    except Exception as e:
        print(f"Code execution error: {e}")
        error_result = {
            'success': False,
            'output': '',
            'error': f"Server error: {str(e)}",
            'result': ''
        }
        return jsonify(error_result), 500


# New route for getting available variables/namespace info
@app.route('/namespace', methods=['GET'])
def get_namespace():
    """
    Get information about available variables in the global namespace.
    """
    if request.remote_addr != '127.0.0.1':
        abort(403, description="Only localhost requests are allowed.")
    
    # Filter out built-ins and modules for cleaner output
    user_vars = {}
    for key, value in GLOBAL_NAMESPACE.items():
        if not key.startswith('_') and key not in ['__builtins__', 'np', 'pd', 'plt', 'datetime', 'os', 'sys', 'json']:
            try:
                # Try to get a string representation
                str_repr = str(value)
                if len(str_repr) > 100:
                    str_repr = str_repr[:100] + "..."
                user_vars[key] = {
                    'type': type(value).__name__,
                    'value': str_repr
                }
            except:
                user_vars[key] = {
                    'type': type(value).__name__,
                    'value': '<unable to display>'
                }
    
    return jsonify({
        'variables': user_vars,
        'available_modules': ['numpy as np', 'pandas as pd', 'matplotlib.pyplot as plt', 'datetime', 'os', 'sys', 'json']
    })

# New route for clearing the namespace
@app.route('/clear_namespace', methods=['POST'])
def clear_namespace():
    """
    Clear user-defined variables from the global namespace.
    """
    if request.remote_addr != '127.0.0.1':
        abort(403, description="Only localhost requests are allowed.")
    
    # Keep only the essential modules and built-ins
    keys_to_remove = []
    for key in GLOBAL_NAMESPACE.keys():
        if key not in ['__builtins__', 'np', 'pd', 'plt', 'datetime', 'os', 'sys', 'json']:
            keys_to_remove.append(key)
    
    for key in keys_to_remove:
        del GLOBAL_NAMESPACE[key]
    
    return jsonify({'status': 'success', 'message': 'Namespace cleared'})



# New route for adding data source variables to namespace
@app.route('/addvariable', methods=['POST'])
def add_variable():
    """
    API endpoint to add data source variables to the global namespace.
    """
    # Check if request is from localhost
    if request.remote_addr != '127.0.0.1':
        print(f"Rejected request from non-localhost: {request.remote_addr}")
        abort(403, description="Only localhost requests are allowed.")

    # Check Content-Type
    if not request.is_json:
        print(f"Invalid Content-Type: {request.content_type}")
        abort(400, description="Content-Type must be application/json.")

    try:
        received_data = request.get_json()
        print(f"Received add variable request: {received_data}")

        if received_data is None:
            raise ValueError("Empty or malformed JSON.")
    except Exception as e:
        print(f"Error parsing JSON: {e}")
        abort(400, description=f"Invalid JSON: {e}")

    # Extract required parameters
    if "datasource" not in received_data:
        abort(400, description="Missing required field: 'datasource'")
    
    if "variable_name" not in received_data:
        abort(400, description="Missing required field: 'variable_name'")

    datasource = received_data["datasource"]
    variable_name = received_data["variable_name"]
    threshold = int(received_data.get("threshold", 1500))

    # Validate variable name (must be valid Python identifier)
    if not variable_name.isidentifier():
        abort(400, description=f"Invalid variable name: '{variable_name}'. Must be a valid Python identifier.")

    # Check if variable already exists
    if variable_name in GLOBAL_NAMESPACE:
        return jsonify({
            'status': 'warning',
            'message': f"Variable '{variable_name}' already exists and will be overwritten.",
            'variable_name': variable_name,
            'datasource': datasource,
            'overwritten': True
        }), 200

    try:
        print(f"Creating data source variable: {variable_name} from {datasource}")

        # Generate data based on datasource
        if datasource == "COVID-19 Tests":
            print("Using local data source for COVID-19 test data.")
            print("Current working directory:", os.getcwd())
            df = pd.read_csv("local_covid_19_test_data.csv")
            df['date'] = pd.to_datetime(df['date'])  # Ensure 'date' is datetime type
            df = df.set_index('date')
        elif datasource in ["COVID-19 Deaths", "Pneumonia Deaths", "Flu Deaths"]:
            # Create a complete date range (daily frequency)
            # Use CDC data
            df = generate_cdc_data(datasource, threshold=threshold)
            df['start_date'] = pd.to_datetime(df['start_date'])  # Ensure 'date' is datetime type
            df = df.set_index('start_date')
            df.index = pd.date_range(start=df.index[0], periods=len(df), freq='W-SUN')

            
            if df is None:
                raise ValueError(f"Failed to generate data for datasource: {datasource}")
       
        else:
            # Check database for custom data source
            print(f"Looking up custom data source '{datasource}' in database...")
            db_datasource = get_data_source_by_name_from_db(datasource)
            
            if db_datasource:
                print(f"Found data source in database: {db_datasource}")

                # Get the DataURL from database
                data_url = db_datasource.get('data_url', '')
                
                if not data_url:
                    raise ValueError(f"Data source '{datasource}' found in database but has no DataURL")
                
                print(f"Loading CSV data from: {data_url}")
                
                # Check if it's a local file path or URL
                if os.path.isfile(data_url):
                    # Local file
                    print(f"Loading local CSV file: {data_url}")
                    df = pd.read_csv(data_url)
                elif data_url.startswith(('http://', 'https://')):
                    # Remote URL
                    print(f"Loading CSV from URL: {data_url}")
                    df = pd.read_csv(data_url)
                else:
                    # Try as relative path
                    print(f"Trying as relative path: {data_url}")
                    if os.path.isfile(data_url):
                        df = pd.read_csv(data_url)
                    else:
                        raise FileNotFoundError(f"CSV file not found: {data_url}")
                
                # Process the loaded data
                print(f"Loaded CSV data with shape: {df.shape}")
                print(f"Columns: {df.columns.tolist()}")
                
                # Try to identify date and case columns
                date_column = None
                case_column = None
                
                # Look for common date column names
                for col in df.columns:
                    if col.lower() in ['date', 'dates', 'time', 'timestamp', 'start_date']:
                        date_column = col
                        break
                
                # Look for common case column names
                for col in df.columns:
                    if col.lower() in ['cases', 'n_cases', 'count', 'value', 'deaths', 'cases_count']:
                        case_column = col
                        break
                
                if not date_column:
                    # Use first column as date
                    date_column = df.columns[0]
                    print(f"No date column found, using first column as date: {date_column}")
                
                if not case_column:
                    # Use second column as cases, or first numeric column
                    numeric_cols = df.select_dtypes(include=[np.number]).columns
                    if len(numeric_cols) > 0:
                        case_column = numeric_cols[0]
                    else:
                        case_column = df.columns[1] if len(df.columns) > 1 else df.columns[0]
                    print(f"No case column found, using: {case_column}")
                
                # Process the data
                try:
                    df[date_column] = pd.to_datetime(df[date_column])
                    df = df.set_index(date_column)
                    
                    # Rename case column to standard name
                    if case_column != 'n_cases':
                        df = df.rename(columns={case_column: 'n_cases'})
                    
                    # Ensure n_cases is numeric
                    df['n_cases'] = pd.to_numeric(df['n_cases'], errors='coerce')
                    
                    # Add outbreak cases column
                    df['n_outbreak_cases'] = df['n_cases'].apply(
                        lambda x: max(0, x - threshold) if pd.notna(x) else 0
                    )
                    
                    # Remove any rows with NaN values
                    df = df.dropna(subset=['n_cases'])
                    
                    print(f"Processed data shape: {df.shape}")
                    print(f"Date range: {df.index.min()} to {df.index.max()}")
                    print(f"Case range: {df['n_cases'].min()} to {df['n_cases'].max()}")
                except Exception as e:
                    print(f"Error processing CSV data: {e}")
                    raise ValueError(f"Failed to process CSV data from '{data_url}': {str(e)}")
                    
            else:
                  # Data source not found in database, try simulation as fallback
                print(f"Data source '{datasource}' not found in database, generating simulation data")
                raise ValueError(f"Unknown datasource: {datasource}")

        # Add the dataframe to global namespace
        GLOBAL_NAMESPACE[variable_name] = df
        
        # Get basic information about the created variable
        data_info = {
            'shape': df.shape,
            'columns': list(df.columns),
            'index_type': str(type(df.index).__name__),
            'date_range': {
                'start': str(df.index.min()) if hasattr(df.index, 'min') else 'N/A',
                'end': str(df.index.max()) if hasattr(df.index, 'max') else 'N/A'
            } if hasattr(df, 'index') else 'N/A',
            'memory_usage': f"{df.memory_usage(deep=True).sum() / 1024:.2f} KB" if hasattr(df, 'memory_usage') else 'N/A'
        }

        print(f"Successfully created variable '{variable_name}' with shape {df.shape}")

        return jsonify({
            'status': 'success',
            'message': f"Variable '{variable_name}' created successfully from {datasource}",
            'variable_name': variable_name,
            'datasource': datasource,
            'data_info': data_info,
            'threshold': threshold,
            'overwritten': False
        }), 200

    except Exception as e:
        error_message = f"Failed to create variable from datasource: {str(e)}"
        print(f"Add variable error: {error_message}")
        
        return jsonify({
            'status': 'error',
            'message': error_message,
            'variable_name': variable_name,
            'datasource': datasource
        }), 500


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