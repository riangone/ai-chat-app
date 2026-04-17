#!/bin/bash
PID_FILE="app.pid"

if [ -f "$PID_FILE" ]; then
    echo "App is already running (PID: $(cat $PID_FILE))"
    exit 1
fi

echo "Starting AiChatApp..."
nohup dotnet run --project AiChatApp > app.log 2>&1 &
echo $! > "$PID_FILE"
echo "App started with PID: $(cat $PID_FILE)"
echo "Logs are being written to app.log"
