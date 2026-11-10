#!/usr/bin/env bash
# Bootstrap a fresh clone of the Godot branch.
# Git LFS must be installed before checkout, otherwise the art files are pointers.
set -euo pipefail
cd "$(dirname "$0")/.."

if ! command -v git-lfs >/dev/null 2>&1; then
  echo "git-lfs is required: https://git-lfs.com" >&2
  exit 1
fi

git lfs install --local
git lfs pull

# Fail loudly instead of opening a project full of LFS pointer text files.
if head -c 40 assets/textures/BallTex3.png | grep -q 'version https://git-lfs'; then
  echo "Git LFS assets are still pointers; run 'git lfs pull'." >&2
  exit 1
fi

dotnet restore Ball2Godot.csproj
dotnet restore tests/Ball2.Core.Tests/Ball2.Core.Tests.csproj

echo "Ready. Build:  dotnet build Ball2Godot.csproj"
echo "       Tests:  dotnet test tests/Ball2.Core.Tests/Ball2.Core.Tests.csproj"
echo "       Game:   godot --path ."
