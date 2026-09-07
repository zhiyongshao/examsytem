#!/usr/bin/env bash
cd /e/Workbuddy/ExamSystem
export EXAM_DB="Data Source=E:/Workbuddy/ExamSystem/exam_test_$$.db"
"E:/Workbuddy/dotnet-sdk/dotnet.exe" run -c Release --no-build > server.log 2>&1 &
SRV=$!
echo "server pid=$SRV"
for i in $(seq 1 40); do
  if grep -q "Now listening" server.log 2>/dev/null; then echo "listening after ${i}s"; break; fi
  sleep 1
done
if ! grep -q "Now listening" server.log 2>/dev/null; then
  echo "!! server did not start; tail:"; tail -20 server.log; kill $SRV 2>/dev/null; exit 2
fi
"C:/Python314/python.exe" selftest.py
RC=$?
kill $SRV 2>/dev/null
exit $RC
