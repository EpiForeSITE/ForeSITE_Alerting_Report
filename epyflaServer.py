#!/usr/bin/env python3

import ssl
import epidemic_surveillance_tools as est
from flask import Flask, request, jsonify, abort

# Create the Flask application instance
app = Flask(__name__)

# Define the port the application will run on
PORT = 5001 # Using 5001 to avoid potential conflicts with default 5000

@app.route('/epyapi', methods=['POST'])
def process_json():
    """
    API endpoint to process incoming JSON data over HTTPS from localhost.
    """
    # 1. Check if the request originated from localhost
    # Note: request.remote_addr should be '127.0.0.1' when accessed via localhost
    if request.remote_addr != '127.0.0.1':
        print(f"Rejected request from non-localhost address: {request.remote_addr}")
        # 403 Forbidden
        abort(403, description="Access forbidden: Requests only allowed from localhost.")

    # 2. Check if the request Content-Type is application/json
    if not request.is_json:
        print(f"Rejected request with invalid Content-Type: {request.content_type}")
        # 400 Bad Request
        abort(400, description="Invalid request: Content-Type must be application/json.")

    # 3. Try to get the JSON data
    try:
        # request.get_json() parses the incoming JSON request data and returns it
        received_data = request.get_json()
        if received_data is None:
             # This case might happen if JSON is malformed but Content-Type was set
             raise ValueError("Malformed JSON received.")
        print(f"Received JSON data from {request.remote_addr}: {received_data}")

        if "init" in received_data:
            # This is a special case where we might want to initialize something
            # For example, if the JSON contains an 'init' key, we could log it or perform an action
            print(f"Initialization request received: {received_data['init']}")
            # 1. Generate simulation data
            simulated_data = est.generate_simulation_data(
                start_date='2020-01-01',
                end_date='2022-12-31',
                lam=8,
                outbreak_threshold=15
            )
            print("\nGenerated Data Head:")
            print(simulated_data.head())
            jsonify({'message': 'Simulation data generated successfully.'}), 200 # 200 OK status code})

        if "graph" in received_data:

            # 2. Define where to save the plot
            # Make sure the path is correct for your system
            # Using a relative path here for simplicity
            output_plot_path = 'farrington_simulation_plot.png'
            # For a specific path like in the original example:
            # output_plot_path = r'C:\Users\taohe\Documents\pyproject\farrington_simulation_plot.png'
            # Ensure the directory exists or the script has permission to create it.

            # 3. Generate and save the plot using the simulated data
            try:
                est.generate_plot_from_data(
                      df=simulated_data,
                      save_path=output_plot_path,
                      train_split_ratio=0.75, # Example: changed ratio
                      alpha=0.05,
                      years_back=1
                )
                jsonify({'message': 'Plot generated successfully.', 'plot_path': output_plot_path}), 200 # 200 OK status code
            except Exception as e:
               print(f"\nAn error occurred during plot generation: {e}")
            



        
    except Exception as e:
        print(f"Error processing JSON data: {e}")
        # 400 Bad Request
        abort(400, description=f"Invalid JSON data: {e}")

    # --- Placeholder for your actual processing logic ---
    # Example: Add a 'status' field to the received data
    response_data = received_data.copy() # Avoid modifying original directly
    response_data['status'] = 'processed'
    response_data['message'] = 'Data received successfully via HTTPS on localhost.'
    # --- End of placeholder logic ---

    # 4. Send back a JSON response
    # jsonify() converts the dict into a JSON response object
    # with the correct Content-Type header (application/json)
    print(f"Sending JSON response: {response_data}")
    return jsonify(response_data), 200 # 200 OK status code

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
         app.run(host='127.0.0.1', port=PORT, ssl_context=context, debug=True)
    except ImportError:
         print("Error: 'cryptography' library not found.")
         print("Please install it for ad-hoc SSL certificate generation:")
         print("  pip install cryptography")
    except FileNotFoundError:
         print("Error: Could not find 'cert.pem' or 'key.pem'.")
         print("Make sure certificate files are generated and in the correct path if using Option 2.")
    except Exception as e:
         print(f"An error occurred during server startup: {e}")