#!/bin/bash
export PATH="/home/ubuntu/.dotnet:/home/ubuntu/.local/bin:$PATH"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PID_FILE="$SCRIPT_DIR/app.pid"
LOG_FILE="$SCRIPT_DIR/app.log"

echo "[$(date '+%Y-%m-%d %H:%M:%S')] === Restarting AiChatApp ==="

# --- Step 1: Force stop any running instance ---
# Via PID file
if [ -f "$PID_FILE" ]; then
    PID=$(cat "$PID_FILE")
    if kill -0 "$PID" 2>/dev/null; then
        echo "Stopping PID $PID (from PID file)..."
        kill "$PID"
        for i in $(seq 1 10); do
            kill -0 "$PID" 2>/dev/null || break
            sleep 1
        done
        kill -0 "$PID" 2>/dev/null && kill -9 "$PID" 2>/dev/null
    fi
    rm -f "$PID_FILE"
fi

# Also kill any remaining dotnet process running AiChatApp (belt-and-suspenders)
LEFTOVER_PIDS=$(pgrep -f "AiChatApp" 2>/dev/null)
if [ -n "$LEFTOVER_PIDS" ]; then
    echo "Force-killing leftover processes: $LEFTOVER_PIDS"
    kill -9 $LEFTOVER_PIDS 2>/dev/null
fi

# Wait for port 5000 to be released
for i in $(seq 1 10); do
    ss -tlnp | grep -q ':5000' || break
    sleep 1
done
if ss -tlnp | grep -q ':5000'; then
    echo "ERROR: Port 5000 still in use after 10s. Aborting."
    exit 1
fi

echo "All processes stopped."

# --- Step 2: Start (dotnet run builds automatically) ---
cd "$SCRIPT_DIR" || { echo "Cannot cd to $SCRIPT_DIR"; exit 1; }

echo "Starting AiChatApp..."
nohup dotnet run --project AiChatApp > "$LOG_FILE" 2>&1 &

APP_PID=$!
echo $APP_PID > "$PID_FILE"

echo "AiChatApp started (PID: $APP_PID)"
echo "Log: $LOG_FILE"
