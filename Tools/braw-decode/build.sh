#!/bin/zsh
# Builds braw-decode into Tools/bin. Needs only the Xcode command line tools;
# the Blackmagic RAW library itself is loaded at runtime (Player, SDK or Resolve install).
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p ../bin
clang++ -std=c++17 -O2 -Wall -Wno-unused-parameter \
    -arch "$(uname -m)" \
    braw-decode.cpp BlackmagicRawAPIDispatch.cpp \
    -framework CoreFoundation \
    -o ../bin/braw-decode
echo "built $(cd ../bin && pwd)/braw-decode"
