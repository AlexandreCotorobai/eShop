#!/bin/bash

# Simple load test script for eShop Order API
# This script sends multiple order creation requests

# Base URL of the API
API_URL="http://localhost:5224"

# Number of requests to send
NUM_REQUESTS=25

# Delay between requests in seconds
DELAY=5

# Function to send order request
send_order_request() {
    request_id=$(uuidgen)  # Generate a unique request ID
    echo "Sending order request with ID: $request_id"
    
    random_user_id="test-user-$((RANDOM % 9000 + 1000))"
    expiration_date="2028-01-01T00:00:00Z"
    
    # Create JSON payload
    body=$(cat <<EOF
{
    "userId": "$random_user_id",
    "userName": "Test User",
    "city": "Seattle",
    "street": "123 Main St",
    "state": "WA",
    "country": "USA",
    "zipCode": "98101",
    "cardNumber": "4111111111111111",
    "cardHolderName": "Test User",
    "cardExpiration": "$expiration_date",
    "cardSecurityNumber": "123",
    "cardTypeId": 1,
    "buyer": "Test Buyer",
    "items": [
        {
            "productId": "$((RANDOM % 100 + 1))",
            "productName": "Test Product",
            "unitPrice": "$((RANDOM % 100 + 1)).99",
            "quantity": 2
        }
    ]
}
EOF
)

    # Send POST request
    response=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API_URL/api/orders?api-version=1.0" \
        -H "Content-Type: application/json" \
        -H "x-requestid: $request_id" \
        -d "$body")

    if [ "$response" -eq 200 ] || [ "$response" -eq 201 ]; then
        echo -e "\e[32mRequest successful (HTTP $response)\e[0m"
    else
        echo -e "\e[31mRequest failed (HTTP $response)\e[0m"
    fi
    
    sleep $DELAY  # Wait for defined delay between requests
}

# Start load test
echo "Starting load test - sending $NUM_REQUESTS requests..."

# Loop to send multiple requests
for ((i=1; i<=NUM_REQUESTS; i++)); do
    echo "Request $i of $NUM_REQUESTS"
    send_order_request
done

echo "Load test completed."
