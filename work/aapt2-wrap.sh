#!/bin/bash
# 过滤掉 Debian aapt2 shim 不支持的 --legacy 参数
args=()
for a in "$@"; do
  if [ "$a" != "--legacy" ]; then args+=("$a"); fi
done
exec /usr/lib/android-sdk/build-tools/debian/aapt2 "${args[@]}"
