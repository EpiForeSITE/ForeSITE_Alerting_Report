import pandas as pd
import numpy as np
from epysurv.models.timepoint import FarringtonFlexible
import matplotlib.pyplot as plt

# Set random seed
np.random.seed(42)

# Create two years of data
dates = pd.date_range(start='2019-01-01', end='2021-01-05', freq='D')
df = pd.DataFrame({'n_cases': np.random.poisson(lam=5, size=len(dates))}, index=dates)

# Add n_outbreak_cases column
threshold = 10
df['n_outbreak_cases'] = df['n_cases'].apply(lambda x: max(0, x - threshold))

# Split training and testing data
train_size = int(len(df) * 0.8)
train = df.iloc[:train_size].copy()
test = df.iloc[train_size:].copy()

print("Training data:", train)
# Initialize and fit the model
model = FarringtonFlexible(alpha=0.05, years_back=1)
model.fit(train)

# Predict
predictions = model.predict(test)
print("Prediction Columns：", predictions.columns)  # Check column names
print("Prediction data:", predictions)

# Prepare data for visualization
df_full = df.copy()
# Use actual column names
df_full['threshold'] = predictions['upperbound']  # Threshold
# Approximate expected (use mean of training data)
df_full['expected'] = train['n_cases'].mean()  # Default value for the entire time range
df_full.loc[test.index, 'expected'] = train['n_cases'].mean()  # Ensure consistency for the test portion

# Visualization
plt.figure(figsize=(12, 6))
plt.plot(df_full.index, df_full['n_cases'], label='Actual Cases', color='blue', marker='o', markersize=4)
plt.plot(df_full.index, df_full['expected'], label='Expected Cases', color='green', linestyle='--')
plt.plot(df_full.index, df_full['threshold'], label='Threshold', color='red', linestyle='--')
plt.fill_between(df_full.index, df_full['expected'], df_full['threshold'], color='red', alpha=0.1, label='Alert Zone')

# Highlight outliers
outliers = df_full.loc[predictions[predictions['alarm']].index]
plt.scatter(outliers.index, outliers['n_cases'], color='purple', label='Outliers', zorder=5)

plt.legend()
plt.title('FarringtonFlexible Model: Case Detection', fontsize=14)
plt.xlabel('Date', fontsize=12)
plt.ylabel('Number of Cases', fontsize=12)
plt.grid(True, linestyle='--', alpha=0.7)
plt.tight_layout()

# Save the plot to a file
import os
save_path = r'C:\Users\taohe\Documents\PyProjects\farrington_plot.png'
plt.savefig(save_path, dpi=300, bbox_inches='tight')
plt.close()
