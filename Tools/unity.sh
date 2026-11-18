#!/usr/bin/env bash
# Runs Unity from the command line for this project, so Claude (or you) can check work without the editor.
#
#   Tools/unity.sh check      compile the project; lists compiler errors, exit code 1 if any
#   Tools/unity.sh capture    play the Boot scene and save screenshots + log.txt to Screenshots/
#   Tools/unity.sh scenes     rebuild the generated scenes in Assets/DuckAndCover/Scenes
#   Tools/unity.sh netsmoke   build a player, run it twice (host + client, no graphics), compare the results
#   Tools/unity.sh netsmoke flag_rush   the same with another mode (king_of_the_hill, capture_hold, flag_rush, overrun)
#
# Close the Unity editor first: Unity can't open a project that's already open.
# Set UNITY_PATH to the Unity executable if it isn't in the default Unity Hub folder.
set -u

command="${1:-}"
mode="${2:-king_of_the_hill}"
case "$mode" in king_of_the_hill|capture_hold|flag_rush|overrun) ;; *) echo "Unknown mode $mode"; exit 2 ;; esac
case "$command" in check|capture|scenes|netsmoke) ;; *) sed -n '2,10p' "$0"; exit 2 ;; esac

project="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$project/Logs"
log="$project/Logs/unity-$command.log"

find_unity() {
  if [ -n "${UNITY_PATH:-}" ] && [ -x "$UNITY_PATH" ]; then echo "$UNITY_PATH"; return; fi
  local version=""
  if [ -f "$project/ProjectSettings/ProjectVersion.txt" ]; then
    version="$(sed -n 's/^m_EditorVersion: *//p' "$project/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
  fi
  for candidate in \
    "/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity" \
    "$HOME/Unity/Hub/Editor/$version/Editor/Unity"; do
    if [ -n "$version" ] && [ -x "$candidate" ]; then echo "$candidate"; return; fi
  done
  for candidate in /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity "$HOME"/Unity/Hub/Editor/*/Editor/Unity; do
    if [ -x "$candidate" ]; then echo "$candidate"; return; fi
  done
  echo "Unity not found. Set UNITY_PATH to the Unity executable." >&2
  exit 1
}

unity="$(find_unity)"
echo "Unity: $unity"
echo "Log:   $log"

case "$command" in
  check)   args=(-batchmode -nographics -quit -projectPath "$project" -logFile "$log") ;;
  # No -nographics: screenshots need a graphics device. No -quit: the capture quits by itself.
  capture) args=(-batchmode -projectPath "$project" -logFile "$log"
                 -executeMethod DuckAndCover.EditorTools.Automation.CaptureScreenshots -captureDir "$project/Screenshots") ;;
  scenes)  args=(-batchmode -nographics -projectPath "$project" -logFile "$log"
                 -executeMethod DuckAndCover.EditorTools.Automation.CreateScenes) ;;
  netsmoke) player="$project/Builds/NetSmoke/DuckAndCover"
           args=(-batchmode -nographics -projectPath "$project" -logFile "$log"
                 -executeMethod DuckAndCover.EditorTools.Automation.BuildSmokePlayer -smokeBuild "$player") ;;
esac

"$unity" "${args[@]}"
code=$?

if grep -qE "error CS[0-9]+" "$log" 2>/dev/null; then
  echo
  echo "Compiler errors:"
  grep -oE "[^ ]*\.cs\([0-9]+,[0-9]+\): error CS[0-9]+:.*" "$log" | sort -u | sed 's/^/  /'
  [ "$code" -eq 0 ] && code=1
fi
if [ "$command" = capture ] && [ -f "$project/Screenshots/log.txt" ]; then
  echo
  cat "$project/Screenshots/log.txt"
fi
echo
echo "Unity exited with code $code"

if [ "$command" = netsmoke ] && [ "$code" -eq 0 ]; then
  # Two copies of the built game with no graphics: one hosts, one joins, scripted players run a
  # whole match, and each writes what it ended up believing. The files must be identical.
  run="$player"
  [ -d "$player.app" ] && run="$player.app/Contents/MacOS/$(ls "$player.app/Contents/MacOS" | head -1)"
  port=7790
  host_out="$project/Logs/netsmoke-host.txt"; join_out="$project/Logs/netsmoke-client.txt"
  rm -f "$host_out" "$join_out"
  echo; echo "Starting host and client ($mode)..."
  "$run" -batchmode -nographics -duckPort $port -duckSmoke host -duckMode "$mode" -duckPlayers 2 -duckSmokeOut "$host_out" -logFile "$project/Logs/netsmoke-host.log" &
  host_pid=$!
  sleep 4
  "$run" -batchmode -nographics -duckPort $port -duckSmoke join -duckAddress 127.0.0.1 -duckSmokeOut "$join_out" -logFile "$project/Logs/netsmoke-client.log" &
  join_pid=$!
  wait "$host_pid" || code=1
  wait "$join_pid" || code=1
  if [ ! -f "$host_out" ] || [ ! -f "$join_out" ]; then
    echo "A smoke instance wrote no result. See Logs/netsmoke-host.log and Logs/netsmoke-client.log."; code=1
  else
    echo; echo "Host:"; cat "$host_out"; echo "Client:"; cat "$join_out"
    if ! cmp -s "$host_out" "$join_out"; then echo "NETSMOKE FAILED: host and client disagree."; code=1
    elif [ "$code" -eq 0 ]; then echo "NETSMOKE PASSED: host and client agree."
    else echo "NETSMOKE FAILED: an instance reported errors."; fi
  fi
fi
exit "$code"
