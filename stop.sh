#!/bin/bash
PID_FILE="app.pid"

if [ -f "$PID_FILE" ]; then
    PID=$(cat "$PID_FILE")
    echo "Stopping AiChatApp (PID: $PID)..."
    kill "$PID" 2>/dev/null
    sleep 1
    kill -9 "$PID" 2>/dev/null
    rm "$PID_FILE"
fi

# Cleanup any remaining processes on port 5000
PIDS=$(lsof -ti:5000)
if [ ! -z "$PIDS" ]; then
    echo "Cleaning up port 5000..."
    kill -9 $PIDS 2>/dev/null
fi

echo "App stopped."
