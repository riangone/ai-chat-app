#!/bin/bash
cd /home/ubuntu/ws/ai-chat-app/mmm

# Create virtual environment if it doesn't exist
if [ ! -d ".venv" ]; then
    echo "Creating virtual environment..."
    python3 -m venv .venv
fi

# Activate virtual environment
source .venv/bin/activate

# Install requirements
echo "Installing requirements..."
pip install -r requirements.txt --quiet

# Start the application
echo "Starting MMM on port 8000..."
uvicorn main:app --host 0.0.0.0 --port 8000 --reload
