#!/bin/bash
RUNTIME=osx-x64
OUT=out/$RUNTIME
ECHO === Publishing $RUNTIME package ===
dotnet publish -c Release -r $RUNTIME -o $OUT /p:CrossGenDuringPublish=false
rm -rf $OUT/out
rm -rf $OUT/native
rm $OUT/secp256k1.dll
rm $OUT/solc.dll
find $OUT -name "*.so" -type f -delete
ECHO === Published $RUNTIME package ===