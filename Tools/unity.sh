#!/usr/bin/env bash
# Runs Unity from the command line for this project, so Claude (or you) can check work without the editor.
#
#   Tools/unity.sh check      compile the project; lists compiler errors, exit code 1 if any
#   Tools/unity.sh capture    play the Boot scene and save screenshots + log.txt to Screenshots/
#   Tools/unity.sh scenes     rebuild the generated scenes in Assets/DuckAndCover/Scenes
#
# Close the Unity editor first: Unity can't open a project that's already open.
# Set UNITY_PATH to the Unity executable if it isn't in the default Unity Hub folder.
set -u

command="${1:-}"
case "$command" in check|capture|scenes) ;; *) sed -n '2,9p' "$0"; exit 2 ;; esac

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
exit "$code"
