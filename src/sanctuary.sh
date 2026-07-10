#!/bin/bash
# Sanctuary server control script
# Usage: ./sanctuary.sh {start|stop|restart|status|logs <name>}
# Runs each server in a tmux session: login, gateway, webapi, assets

DIR="$HOME/Release"
ASSETS_DIR="$HOME/osfr-manifest"
ASSETS_PORT=8080

declare -A CMDS=(
    [login]="cd '$DIR' && dotnet Sanctuary.Login.dll"
    [gateway]="cd '$DIR' && dotnet Sanctuary.Gateway.dll"
    [webapi]="cd '$DIR' && dotnet Sanctuary.WebAPI.dll"
    [assets]="cd '$ASSETS_DIR' && python3 -m http.server $ASSETS_PORT --bind 0.0.0.0"
)

# start order matters: gateway registers itself with login
START_ORDER=(login gateway webapi assets)
STOP_ORDER=(assets webapi gateway login)

running() {
    tmux has-session -t "$1" 2>/dev/null
}

start_one() {
    local name="$1"
    if running "$name"; then
        echo "  $name: already running"
    else
        tmux new-session -d -s "$name" "${CMDS[$name]}; echo; echo '*** $name exited, press enter to close ***'; read"
        echo "  $name: started"
    fi
}

stop_one() {
    local name="$1"
    if running "$name"; then
        tmux kill-session -t "$name"
        echo "  $name: stopped"
    else
        echo "  $name: not running"
    fi
}

case "$1" in
    start)
        echo "Starting Sanctuary servers..."
        start_one login
        sleep 2   # give login a moment before gateway registers with it
        start_one gateway
        start_one webapi
        start_one assets
        ;;
    stop)
        echo "Stopping Sanctuary servers..."
        for s in "${STOP_ORDER[@]}"; do stop_one "$s"; done
        ;;
    restart)
        "$0" stop
        sleep 1
        "$0" start
        ;;
    status)
        for s in "${START_ORDER[@]}"; do
            if running "$s"; then
                echo "  $s: RUNNING"
            else
                echo "  $s: stopped"
            fi
        done
        echo
        echo "Listening ports:"
        ss -tulpn 2>/dev/null | grep -E ":(20042|20260|20041|5055|$ASSETS_PORT)\s" || echo "  (none of the expected ports are listening)"
        ;;
    logs)
        if [ -z "$2" ] || [ -z "${CMDS[$2]}" ]; then
            echo "Usage: $0 logs {login|gateway|webapi|assets}"
            exit 1
        fi
        if running "$2"; then
            echo "(attaching to $2 - detach with Ctrl+B then D)"
            sleep 1
            tmux attach -t "$2"
        else
            echo "$2 is not running"
        fi
        ;;
    *)
        echo "Usage: $0 {start|stop|restart|status|logs <login|gateway|webapi|assets>}"
        exit 1
        ;;
esac
