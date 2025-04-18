# epidemic_surveillance_tools.py

import pandas as pd
import numpy as np
from epysurv.models.timepoint import FarringtonFlexible
import matplotlib.pyplot as plt
import os

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


def generate_plot_from_data(df,
                            save_path,
                            train_split_ratio=0.8,
                            alpha=0.05,
                            years_back=1,
                            plot_title='FarringtonFlexible Model: Case Detection',
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


# --- Example Usage ---
if __name__ == "__main__":
    print("Running example usage...")

    # 1. Generate simulation data
    simulated_data = generate_simulation_data(
        start_date='2020-01-01',
        end_date='2022-12-31',
        lam=8,
        outbreak_threshold=15
    )
    print("\nGenerated Data Head:")
    print(simulated_data.head())

    # 2. Define where to save the plot
    # Make sure the path is correct for your system
    # Using a relative path here for simplicity
    output_plot_path = 'farrington_simulation_plot.png'
    # For a specific path like in the original example:
    # output_plot_path = r'C:\Users\YourUser\Documents\YourFolder\farrington_simulation_plot.png'
    # Ensure the directory exists or the script has permission to create it.

    # 3. Generate and save the plot using the simulated data
    try:
        generate_plot_from_data(
            df=simulated_data,
            save_path=output_plot_path,
            train_split_ratio=0.75, # Example: changed ratio
            alpha=0.05,
            years_back=1
        )
    except Exception as e:
        print(f"\nAn error occurred during plot generation: {e}")

    print("\nExample usage finished.")